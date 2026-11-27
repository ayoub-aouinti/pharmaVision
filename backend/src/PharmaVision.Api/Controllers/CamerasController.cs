using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmaVision.Api.Hubs;
using PharmaVision.Api.Security;
using PharmaVision.Api.Services;
using PharmaVision.Domain;
using PharmaVision.Infrastructure;
using PharmaVision.Infrastructure.Seeding;

namespace PharmaVision.Api.Controllers;

public record RuleDto(AlertKind Kind, double Threshold, [Range(0, 86400)] int DurationSec, NotificationChannel Channel, bool Enabled);

public record ZoneDto(
    int Id, int CameraId,
    [Required, StringLength(80)] string Name,
    ZoneType Type,
    [Required] List<ZonePoint> Polygon,
    string? Category, string? Brands, string? PlanogramRef, int? FacingsExpected,
    List<RuleDto>? Rules);

public record CameraDto(
    int Id, string Name, string Brand, string Protocol, string RtspUrl, string Resolution, CameraStatus Status,
    DateTime? ReferencePhotoAt, int PeopleNow, List<ZoneDto> Zones);

public record CameraInput(
    int PharmacyId,
    [Required, StringLength(80)] string Name,
    [Required, StringLength(60)] string Brand,
    [Required, RegularExpression("^rtsp://.+", ErrorMessage = "L'URL doit commencer par rtsp://")] string RtspUrl,
    string? Resolution,
    CameraStatus Status = CameraStatus.Online);

public record DiscoveredCamera(string Name, string Brand, string RtspUrl, string Resolution);

public static class Mapping
{
    public static ZoneDto ToDto(this Zone z) => new(
        z.Id, z.CameraId, z.Name, z.Type, z.Polygon, z.Category, z.Brands, z.PlanogramRef, z.FacingsExpected,
        z.Rules.OrderBy(r => r.Kind).Select(r => new RuleDto(r.Kind, r.Threshold, r.DurationSec, r.Channel, r.Enabled)).ToList());
}

[ApiController]
[Route("api/cameras")]
public class CamerasController(PharmaDbContext db, TimeProvider clock) : ControllerBase
{
    /// <summary>Caméras des officines accessibles au compte connecté.</summary>
    private IQueryable<Camera> Scoped()
    {
        var allowed = User.PharmacyIds();
        return db.Cameras.Where(c => allowed.Contains(c.Device!.PharmacyId));
    }

    [HttpGet]
    public async Task<List<CameraDto>> List(int pharmacyId, CancellationToken ct)
    {
        var cameras = await db.Cameras.AsNoTracking()
            .Where(c => c.Device!.PharmacyId == pharmacyId)
            .Include(c => c.Zones).ThenInclude(z => z.Rules)
            .OrderBy(c => c.Id).AsSplitQuery().ToListAsync(ct);

        var owner = User.IsInRole(Roles.Titulaire);
        var now = clock.GetUtcNow().UtcDateTime;
        var since = now.AddSeconds(-60);
        var zoneIds = cameras.SelectMany(c => c.Zones).Select(z => z.Id).ToList();
        var recent = await db.Events
            .Where(e => zoneIds.Contains(e.ZoneId) && e.Ts >= since && e.Ts <= now && e.TrackId != null)
            .Select(e => new { e.ZoneId, e.TrackId })
            .Distinct().ToListAsync(ct);

        return cameras.Select(c =>
        {
            var own = c.Zones.Select(z => z.Id).ToHashSet();
            var people = recent.Where(r => own.Contains(r.ZoneId)).Select(r => r.TrackId).Distinct().Count();
            return new CameraDto(c.Id, c.Name, c.Brand, c.Protocol, owner ? c.RtspUrl : "", c.Resolution, c.Status, c.ReferencePhotoAt,
                c.Status == CameraStatus.Online ? people : 0,
                c.Zones.OrderBy(z => z.Id).Select(z => z.ToDto()).ToList());
        }).ToList();
    }

    [Authorize(Roles = Roles.Titulaire)]
    [HttpPost]
    public async Task<ActionResult<int>> Create(CameraInput input, CancellationToken ct)
    {
        if (!User.CanAccess(input.PharmacyId)) return Forbid();
        var device = await db.Devices.FirstOrDefaultAsync(d => d.PharmacyId == input.PharmacyId, ct);
        if (device is null) return NotFound("Aucun boîtier pour cette pharmacie");
        var camera = new Camera
        {
            DeviceId = device.Id, Name = input.Name, Brand = input.Brand, RtspUrl = input.RtspUrl,
            Resolution = input.Resolution ?? "2560×1440", Status = input.Status,
        };
        db.Cameras.Add(camera);
        await db.SaveChangesAsync(ct);
        return camera.Id;
    }

    [Authorize(Roles = Roles.Titulaire)]
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, CameraInput input, CancellationToken ct)
    {
        var camera = await Scoped().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (camera is null) return NotFound();
        (camera.Name, camera.Brand, camera.RtspUrl, camera.Status) = (input.Name, input.Brand, input.RtspUrl, input.Status);
        camera.Resolution = input.Resolution ?? camera.Resolution;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [Authorize(Roles = Roles.Titulaire)]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct) =>
        await Scoped().Where(c => c.Id == id).ExecuteDeleteAsync(ct) == 0 ? NotFound() : NoContent();

    /// <summary>Découverte ONVIF simulée : propose des caméras non encore enregistrées.</summary>
    [Authorize(Roles = Roles.Titulaire)]
    [HttpPost("discover")]
    public async Task<List<DiscoveredCamera>> Discover(int pharmacyId, CancellationToken ct)
    {
        var known = await db.Cameras.Where(c => c.Device!.PharmacyId == pharmacyId).Select(c => c.RtspUrl).ToListAsync(ct);
        DiscoveredCamera[] found =
        [
            new("Hikvision DS-2CD2143G2", "Hikvision", $"rtsp://192.168.{pharmacyId}.120:554/Streaming/Channels/101", "2688×1520"),
            new("Dahua IPC-HDW2431T", "Dahua", $"rtsp://192.168.{pharmacyId}.121:554/cam/realmonitor", "2560×1440"),
            new("Axis M3085-V", "Axis", $"rtsp://192.168.{pharmacyId}.122:554/axis-media/media.amp", "1920×1080"),
        ];
        return found.Where(f => !known.Contains(f.RtspUrl)).ToList();
    }

    [HttpGet("{id:int}/snapshot")]
    public async Task<IActionResult> Snapshot(int id, CancellationToken ct)
    {
        var camera = await Scoped().AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        return camera is null ? NotFound() : Content(SceneRenderer.Snapshot(camera), "image/svg+xml; charset=utf-8");
    }

    [Authorize(Roles = Roles.Titulaire)]
    [HttpPost("{id:int}/reference-photo")]
    public async Task<IActionResult> ReferencePhoto(int id, CancellationToken ct)
    {
        var camera = await Scoped().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (camera is null) return NotFound();
        camera.ReferencePhotoAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}

[ApiController]
[Authorize(Roles = Roles.Titulaire)]
[Route("api/zones")]
public class ZonesController(PharmaDbContext db, LiveNotifier notifier) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<ZoneDto>> Create(ZoneDto input, CancellationToken ct)
    {
        if (Validate(input) is { } error) return BadRequest(error);
        var allowed = User.PharmacyIds();
        if (!await db.Cameras.AnyAsync(c => c.Id == input.CameraId && allowed.Contains(c.Device!.PharmacyId), ct))
            return NotFound("Caméra inconnue");
        var zone = new Zone { CameraId = input.CameraId, Name = input.Name };
        Apply(zone, input);
        if (input.Rules is null) zone.Rules = ReferenceData.DefaultRules(zone.Type, zone.Category);
        db.Zones.Add(zone);
        await db.SaveChangesAsync(ct);
        return zone.ToDto();
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<ZoneDto>> Update(int id, ZoneDto input, CancellationToken ct)
    {
        if (Validate(input) is { } error) return BadRequest(error);
        var zone = await Scoped().Include(z => z.Rules).FirstOrDefaultAsync(z => z.Id == id, ct);
        if (zone is null) return NotFound();
        Apply(zone, input);
        await db.SaveChangesAsync(ct);
        return zone.ToDto();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var zone = await Scoped().Include(z => z.Camera!.Device).FirstOrDefaultAsync(z => z.Id == id, ct);
        if (zone is null) return NotFound();
        var pharmacyId = zone.Camera!.Device!.PharmacyId;
        // Les alertes actives d'une zone supprimée n'ont plus de sens.
        await db.Alerts.Where(a => a.ZoneId == id && a.Status != AlertStatus.Resolved).ExecuteDeleteAsync(ct);
        db.Zones.Remove(zone);
        await db.SaveChangesAsync(ct);
        await notifier.AlertsChangedAsync(pharmacyId, ct);
        return NoContent();
    }

    private IQueryable<Zone> Scoped()
    {
        var allowed = User.PharmacyIds();
        return db.Zones.Where(z => allowed.Contains(z.Camera!.Device!.PharmacyId));
    }

    private static string? Validate(ZoneDto z)
    {
        var minimum = z.Type == ZoneType.CountingLine ? 2 : 3;
        if (z.Polygon.Count < minimum || z.Polygon.Count > 50) return $"La zone doit avoir entre {minimum} et 50 points";
        if (z.Polygon.Any(p => p.X is < 0 or > 1 || p.Y is < 0 or > 1)) return "Les points doivent rester dans l'image";
        if (z.Rules is not null && z.Rules.GroupBy(r => r.Kind).Any(g => g.Count() > 1)) return "Une seule règle par indicateur";
        return null;
    }

    private void Apply(Zone zone, ZoneDto input)
    {
        zone.Name = input.Name.Trim();
        zone.Type = input.Type;
        zone.Polygon = [.. input.Polygon];
        zone.Category = input.Category;
        zone.Brands = input.Brands;
        zone.PlanogramRef = input.PlanogramRef;
        zone.FacingsExpected = input.FacingsExpected;
        if (input.Rules is null) return;

        // Mise à jour par indicateur pour conserver l'identifiant (et l'historique d'alertes).
        foreach (var old in zone.Rules.Where(r => input.Rules.All(n => n.Kind != r.Kind)).ToList())
        {
            zone.Rules.Remove(old);
            db.AlertRules.Remove(old);
        }
        foreach (var rule in input.Rules)
        {
            var existing = zone.Rules.FirstOrDefault(r => r.Kind == rule.Kind);
            if (existing is null) zone.Rules.Add(existing = new AlertRule { Kind = rule.Kind });
            (existing.Threshold, existing.DurationSec, existing.Channel, existing.Enabled) =
                (rule.Threshold, rule.DurationSec, rule.Channel, rule.Enabled);
        }
    }
}
