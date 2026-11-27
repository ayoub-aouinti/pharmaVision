using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmaVision.Api.Hubs;
using PharmaVision.Api.Security;
using PharmaVision.Api.Services;
using PharmaVision.Domain;
using PharmaVision.Infrastructure;

namespace PharmaVision.Api.Controllers;

public record AlertActionRequest(string Action);

public record AlertPage(List<Alert> Items, int Total, int Page, int PageSize, AlertStats Stats);

[ApiController]
[Route("api/alerts")]
public class AlertsController(PharmaDbContext db, LiveNotifier notifier, TimeProvider clock) : ControllerBase
{
    private static readonly string[] Actions = ["restock", "notify", "dispatch", "diagnose", "resolve"];

    /// <summary>Alertes affichées sur le tableau de bord.</summary>
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

    /// <summary>
    /// Journal filtré et paginé. Les alertes en cours sont toujours incluses, même si elles ont
    /// commencé avant la période. Les statistiques portent sur la période, le type et la priorité,
    /// indépendamment de l'état affiché.
    /// </summary>
    /// <param name="state">all | active | resolved</param>
    [HttpGet("history")]
    public async Task<ActionResult<AlertPage>> History(
        int pharmacyId, string? period, string? state, AlertKind? kind, AlertSeverity? severity,
        int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        var pharmacy = await db.Pharmacies.AsNoTracking().FirstOrDefaultAsync(p => p.Id == pharmacyId, ct);
        if (pharmacy is null) return NotFound();
        var range = PeriodRange.Resolve(PeriodRange.Parse(period), clock.GetUtcNow().UtcDateTime,
            TimeZoneInfo.FindSystemTimeZoneById(pharmacy.TimeZone));

        var query = db.Alerts.AsNoTracking().Where(a => a.PharmacyId == pharmacyId
            && (a.Status != AlertStatus.Resolved || (a.StartedAt >= range.From && a.StartedAt <= range.To)));
        if (kind is not null) query = query.Where(a => a.Kind == kind);
        if (severity is not null) query = query.Where(a => a.Severity == severity);

        // Au plus quelques milliers de lignes par officine et par mois : calcul en mémoire.
        var matching = await query.OrderByDescending(a => a.StartedAt).ThenByDescending(a => a.Id).ToListAsync(ct);
        var shown = state switch
        {
            "active" => matching.Where(a => a.Status != AlertStatus.Resolved).ToList(),
            "resolved" => matching.Where(a => a.Status == AlertStatus.Resolved).ToList(),
            _ => matching,
        };

        pageSize = Math.Clamp(pageSize, 1, 1000);
        page = Math.Clamp(page, 1, Math.Max(1, (shown.Count + pageSize - 1) / pageSize));
        return new AlertPage(
            shown.Skip((page - 1) * pageSize).Take(pageSize).ToList(),
            shown.Count, page, pageSize, AlertStats.Of(matching));
    }

    [HttpPost("{id:int}/action")]
    public async Task<ActionResult<Alert>> Act(int id, AlertActionRequest request, CancellationToken ct)
    {
        if (!Actions.Contains(request.Action)) return BadRequest($"Action inconnue : {request.Action}");
        var alert = await db.Alerts.FindAsync([id], ct);
        if (alert is null || !User.CanAccess(alert.PharmacyId)) return NotFound();
        if (alert.Status == AlertStatus.Resolved) return Conflict("Alerte déjà résolue");

        var now = clock.GetUtcNow().UtcDateTime;
        if (request.Action == "resolve")
        {
            alert.Status = AlertStatus.Resolved;
            alert.EndedAt = now;
            alert.ResolvedBy = User.Identity!.Name;
        }
        else
        {
            alert.Status = AlertStatus.Acknowledged;
            alert.ActionTaken = request.Action;
            alert.AcknowledgedAt ??= now;
            alert.HandledBy = User.Identity!.Name;
        }
        await db.SaveChangesAsync(ct);
        await notifier.AlertsChangedAsync(alert.PharmacyId, ct);
        return alert;
    }

    [HttpGet("{id:int}/thumbnail")]
    public async Task<IActionResult> Thumbnail(int id, CancellationToken ct)
    {
        var alert = await db.Alerts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
        if (alert is null || !User.CanAccess(alert.PharmacyId) || alert.CameraId is not { } cameraId) return NotFound();
        var camera = await db.Cameras.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cameraId, ct);
        if (camera is null) return NotFound();
        var zone = alert.ZoneId is { } zoneId
            ? await db.Zones.AsNoTracking().FirstOrDefaultAsync(z => z.Id == zoneId, ct)
            : null;
        return Content(SceneRenderer.Thumbnail(camera, zone, alert.Id, alert.StartedAt), "image/svg+xml; charset=utf-8");
    }
}
