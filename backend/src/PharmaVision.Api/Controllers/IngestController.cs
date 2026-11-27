using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmaVision.Api.Hubs;
using PharmaVision.Api.Services;
using PharmaVision.Domain;
using PharmaVision.Infrastructure;

namespace PharmaVision.Api.Controllers;

/// <summary>Point d'entrée des boîtiers locaux, authentifiés par la clé « X-Api-Key ».</summary>
[ApiController]
[AllowAnonymous] // pas de session : la clé du boîtier est vérifiée dans chaque action
[Route("api/ingest")]
public class IngestController(PharmaDbContext db, LiveNotifier notifier, KpiService kpis, TimeProvider clock) : ControllerBase
{
    private const int MaxBatch = 5000;
    private const int MaxImageBytes = 2_000_000;

    private async Task<EdgeDevice?> DeviceAsync(CancellationToken ct)
    {
        var key = Request.Headers["X-Api-Key"].ToString();
        if (key.Length == 0) return null;
        var hash = DeviceKeys.Hash(key);
        return await db.Devices.Include(d => d.Pharmacy).FirstOrDefaultAsync(d => d.ApiKeyHash == hash, ct);
    }

    private IQueryable<Zone> ZonesOf(int deviceId) =>
        db.Zones.Where(z => z.Camera!.DeviceId == deviceId && z.Camera.Status == CameraStatus.Online
                            && z.Type != ZoneType.PrivacyMask);

    [HttpGet("config")]
    public async Task<ActionResult<DeviceConfig>> Config(CancellationToken ct)
    {
        if (await DeviceAsync(ct) is not { } device) return Unauthorized();
        var zones = await ZonesOf(device.Id).AsNoTracking().OrderBy(z => z.Id).ToListAsync(ct);
        var ids = zones.Select(z => z.Id).ToList();
        var fills = await kpis.FillAtAsync(ids, clock.GetUtcNow().UtcDateTime, ct);
        var restock = await db.Alerts
            .Where(a => a.Status == AlertStatus.Acknowledged && a.ActionTaken == "restock"
                        && a.ZoneId != null && ids.Contains(a.ZoneId.Value))
            .Select(a => a.ZoneId!.Value).ToListAsync(ct);

        var cameras = await db.Cameras.AsNoTracking().Include(c => c.Zones)
            .Where(c => c.DeviceId == device.Id && c.Status == CameraStatus.Online)
            .OrderBy(c => c.Id).ToListAsync(ct);

        return new DeviceConfig(device.Id, device.PharmacyId, device.Pharmacy!.TimeZone,
            zones.Select(z => new DeviceZone(z.Id, z.Name, z.Type, z.Category,
                fills.TryGetValue(z.Id, out var f) ? f : null, z.CameraId, z.Polygon)).ToList(),
            restock,
            cameras.Select(c => new DeviceCamera(c.Id, c.Name, c.RtspUrl,
                c.Zones.Where(z => z.Type == ZoneType.PrivacyMask).Select(z => z.Polygon).ToList(),
                c.ReferencePhotoAt)).ToList());
    }

    /// <summary>Image de référence d'une caméra (JPEG, personnes retirées ou floutées par le boîtier).</summary>
    [HttpPut("cameras/{id:int}/image")]
    [RequestSizeLimit(MaxImageBytes)]
    public async Task<ActionResult<ImageReceived>> Image(int id, CancellationToken ct)
    {
        if (await DeviceAsync(ct) is not { } device) return Unauthorized();
        var camera = await db.Cameras.FirstOrDefaultAsync(c => c.Id == id && c.DeviceId == device.Id, ct);
        if (camera is null) return NotFound();

        using var buffer = new MemoryStream();
        // La taille est aussi vérifiée ici : la limite de requête dépend du serveur d'hébergement.
        var chunk = new byte[81920];
        int read;
        while ((read = await Request.Body.ReadAsync(chunk, ct)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > MaxImageBytes) return StatusCode(StatusCodes.Status413PayloadTooLarge);
        }
        var jpeg = buffer.ToArray();
        if (jpeg is not [0xFF, 0xD8, 0xFF, ..]) return BadRequest("Image JPEG attendue");

        var image = await db.CameraImages.FindAsync([id], ct);
        if (image is null) db.CameraImages.Add(new CameraImage { CameraId = id, Jpeg = jpeg });
        else image.Jpeg = jpeg;
        // PostgreSQL garde la microseconde : la date renvoyée doit être identique à celle relue.
        var now = clock.GetUtcNow().UtcDateTime;
        camera.ReferencePhotoAt = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMicrosecond));
        await db.SaveChangesAsync(ct);
        return new ImageReceived(camera.ReferencePhotoAt.Value);
    }

    [HttpPost("heartbeat")]
    public async Task<IActionResult> Heartbeat(CancellationToken ct)
    {
        if (await DeviceAsync(ct) is not { } device) return Unauthorized();
        var version = Request.Headers["X-Device-Version"].ToString();
        if (version.Length is > 0 and <= 40) device.Version = version;
        device.LastSeen = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("events")]
    public async Task<IActionResult> Events(IngestBatch batch, CancellationToken ct)
    {
        if (await DeviceAsync(ct) is not { } device) return Unauthorized();
        var sales = batch.Sales ?? [];
        if (batch.Events.Count > MaxBatch || sales.Count > MaxBatch) return BadRequest($"Lot limité à {MaxBatch} lignes");

        var allowed = (await ZonesOf(device.Id).Select(z => z.Id).ToListAsync(ct)).ToHashSet();
        var now = clock.GetUtcNow().UtcDateTime;
        var latest = now.AddMinutes(5);
        // Les événements d'une zone supprimée ou désactivée entre-temps sont ignorés, pas rejetés.
        var accepted = batch.Events
            .Where(e => allowed.Contains(e.ZoneId) && Enum.IsDefined(e.Type) && double.IsFinite(e.Value)
                        && e.Ts.ToUniversalTime() <= latest)
            .ToList();

        db.Events.AddRange(accepted.Select(e => new ZoneEvent
        {
            Ts = e.Ts.ToUniversalTime(), ZoneId = e.ZoneId, Type = e.Type, Value = e.Value, TrackId = e.TrackId,
        }));
        db.Sales.AddRange(sales.Where(s => s.Ts.ToUniversalTime() <= latest).Select(s => new Sale
        {
            PharmacyId = device.PharmacyId, Ts = s.Ts.ToUniversalTime(), TicketNo = s.TicketNo,
            ProductCode = s.ProductCode, Family = s.Family, Quantity = s.Quantity, Amount = s.Amount,
        }));
        device.LastSeen = now;
        await db.SaveChangesAsync(ct);

        var fills = accepted.Where(e => e.Type == EventType.Fill)
            .GroupBy(e => e.ZoneId)
            .Select(g => new ZoneFill(g.Key, g.MaxBy(e => e.Ts)!.Value))
            .ToList();
        await notifier.EventsReceivedAsync(device.PharmacyId, accepted.Count, fills, ct);
        return Accepted(new { accepted = accepted.Count, ignored = batch.Events.Count - accepted.Count });
    }
}
