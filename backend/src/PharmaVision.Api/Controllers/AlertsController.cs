using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmaVision.Api.Hubs;
using PharmaVision.Api.Services;
using PharmaVision.Domain;
using PharmaVision.Infrastructure;

namespace PharmaVision.Api.Controllers;

public record AlertActionRequest(string Action);

[ApiController]
[Route("api/alerts")]
public class AlertsController(PharmaDbContext db, LiveNotifier notifier, TimeProvider clock) : ControllerBase
{
    private static readonly string[] Actions = ["restock", "notify", "dispatch", "diagnose", "resolve"];
    private const string DemoUser = "Titulaire (démo)"; // authentification hors itération 1

    [HttpGet]
    public async Task<List<Alert>> List(int pharmacyId, bool activeOnly = true, int limit = 50, CancellationToken ct = default)
    {
        var query = db.Alerts.AsNoTracking().Where(a => a.PharmacyId == pharmacyId);
        if (activeOnly) query = query.Where(a => a.Status != AlertStatus.Resolved);
        return await query
            .OrderBy(a => a.Status).ThenByDescending(a => a.Severity).ThenByDescending(a => a.StartedAt)
            .Take(Math.Clamp(limit, 1, 500))
            .ToListAsync(ct);
    }

    [HttpPost("{id:int}/action")]
    public async Task<ActionResult<Alert>> Act(int id, AlertActionRequest request, CancellationToken ct)
    {
        if (!Actions.Contains(request.Action)) return BadRequest($"Action inconnue : {request.Action}");
        var alert = await db.Alerts.FindAsync([id], ct);
        if (alert is null) return NotFound();
        if (alert.Status == AlertStatus.Resolved) return Conflict("Alerte déjà résolue");

        alert.HandledBy = DemoUser;
        if (request.Action == "resolve")
        {
            alert.Status = AlertStatus.Resolved;
            alert.EndedAt = clock.GetUtcNow().UtcDateTime;
        }
        else
        {
            alert.Status = AlertStatus.Acknowledged;
            alert.ActionTaken = request.Action;
        }
        await db.SaveChangesAsync(ct);
        await notifier.AlertsChangedAsync(alert.PharmacyId, ct);
        return alert;
    }

    [HttpGet("{id:int}/thumbnail")]
    public async Task<IActionResult> Thumbnail(int id, CancellationToken ct)
    {
        var alert = await db.Alerts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
        if (alert?.CameraId is not { } cameraId) return NotFound();
        var camera = await db.Cameras.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cameraId, ct);
        if (camera is null) return NotFound();
        var zone = alert.ZoneId is { } zoneId
            ? await db.Zones.AsNoTracking().FirstOrDefaultAsync(z => z.Id == zoneId, ct)
            : null;
        return Content(SceneRenderer.Thumbnail(camera, zone, alert.Id, alert.StartedAt), "image/svg+xml; charset=utf-8");
    }
}
