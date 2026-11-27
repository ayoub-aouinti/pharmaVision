using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmaVision.Api.Security;
using PharmaVision.Api.Services;
using PharmaVision.Domain;
using PharmaVision.Infrastructure;

namespace PharmaVision.Api.Controllers;

public record DeviceStatus(string Name, string Version, DateTime? LastSeen, bool Online);

public record PharmacySummary(
    int Id, string Name, string City, string? Group, string TimeZone, DeviceStatus? Device, int ActiveAlerts);

[ApiController]
[Route("api/pharmacies")]
public class PharmaciesController(PharmaDbContext db, TimeProvider clock) : ControllerBase
{
    [HttpGet]
    public async Task<List<PharmacySummary>> List(CancellationToken ct)
    {
        var allowed = User.PharmacyIds();
        var pharmacies = await db.Pharmacies.AsNoTracking().Where(p => allowed.Contains(p.Id))
            .Include(p => p.Devices).OrderBy(p => p.Id).ToListAsync(ct);
        var alerts = await db.Alerts.Where(a => a.Status != AlertStatus.Resolved)
            .GroupBy(a => a.PharmacyId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, ct);
        var limit = clock.GetUtcNow().UtcDateTime.AddSeconds(-60);

        return pharmacies.Select(p =>
        {
            var device = p.Devices.OrderByDescending(d => d.LastSeen).FirstOrDefault();
            return new PharmacySummary(p.Id, p.Name, p.City, p.Group, p.TimeZone,
                device is null ? null : new DeviceStatus(device.Name, device.Version, device.LastSeen, device.LastSeen > limit),
                alerts.GetValueOrDefault(p.Id));
        }).ToList();
    }
}

/// <summary>Indicateurs calculés côté serveur ; period = live | today | 7d | 30d.</summary>
[ApiController]
[Route("api/dashboard")]
public class DashboardController(KpiService kpis) : ControllerBase
{
    [HttpGet("kpis")]
    public async Task<ActionResult<KpiSummary>> Kpis(int pharmacyId, string? period, CancellationToken ct) =>
        await kpis.SummaryAsync(pharmacyId, PeriodRange.Parse(period), ct) is { } result ? result : NotFound();

    [HttpGet("shelves")]
    public async Task<ActionResult<List<ShelfRow>>> Shelves(int pharmacyId, string? period, CancellationToken ct) =>
        await kpis.ShelvesAsync(pharmacyId, PeriodRange.Parse(period), ct) is { } result ? result : NotFound();

    [HttpGet("heatmap")]
    public async Task<ActionResult<List<HeatCell>>> Heatmap(int pharmacyId, string? period, CancellationToken ct) =>
        await kpis.HeatmapAsync(pharmacyId, PeriodRange.Parse(period), ct) is { } result ? result : NotFound();

    [HttpGet("hourly")]
    public async Task<ActionResult<List<HourPoint>>> Hourly(int pharmacyId, string? period, CancellationToken ct) =>
        await kpis.HourlyAsync(pharmacyId, PeriodRange.Parse(period), ct) is { } result ? result : NotFound();
}
