using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmaVision.Api.Security;
using PharmaVision.Api.Services;
using PharmaVision.Infrastructure;

namespace PharmaVision.Api.Controllers;

[ApiController]
public class HistoryController(HistoryService history, PharmaDbContext db) : ControllerBase
{
    /// <summary>Une journée rejouable ; date = aaaa-mm-jj (aujourd'hui par défaut).</summary>
    [HttpGet("api/history/day")]
    public async Task<ActionResult<DayReplay>> Day(int pharmacyId, DateOnly? date, CancellationToken ct)
    {
        // Bornes qui évitent les dépassements de calcul sur des dates absurdes.
        if (date is { Year: < 2000 or > 2100 }) return BadRequest("Date hors limites");
        return await history.DayAsync(pharmacyId, date, ct) is { } result ? result : NotFound();
    }

    [HttpGet("api/alerts/{id:int}/context")]
    public async Task<ActionResult<AlertContext>> Context(int id, CancellationToken ct)
    {
        var alert = await db.Alerts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
        if (alert is null || !User.CanAccess(alert.PharmacyId)) return NotFound();
        return await history.ContextAsync(alert, ct) is { } context ? context : NotFound();
    }
}
