using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmaVision.Api.Hubs;
using PharmaVision.Api.Security;
using PharmaVision.Api.Services;
using PharmaVision.Infrastructure;

namespace PharmaVision.Api.Controllers;

public record ImportDto(int Id, string FileName, DateTime ImportedAt, string ImportedBy, int Rows, DateOnly FirstDay, DateOnly LastDay);

/// <summary>Import CSV de la caisse. dryRun = true analyse le fichier sans rien écrire.</summary>
[ApiController]
[Authorize(Roles = Roles.Titulaire)]
public class ImportsController(ImportService imports, PharmaDbContext db, LiveNotifier notifier) : ControllerBase
{
    private const int MaxFileBytes = 10_000_000;

    [HttpPost("api/sales/import")]
    [RequestSizeLimit(MaxFileBytes + 100_000)]
    public async Task<ActionResult<SalesImportReport>> Sales(
        int pharmacyId, IFormFile file, bool replace = false, bool dryRun = true, CancellationToken ct = default)
    {
        if (file.Length > MaxFileBytes) return BadRequest("Fichier limité à 10 Mo");
        var name = Path.GetFileName(file.FileName);
        var report = await imports.SalesAsync(
            pharmacyId, await ReadAsync(file, ct), name.Length > 120 ? name[..120] : name,
            User.Identity!.Name!, replace, dryRun, ct);
        return report is null ? NotFound() : report;
    }

    [HttpGet("api/sales/imports")]
    public Task<List<ImportDto>> List(int pharmacyId, CancellationToken ct) =>
        db.SalesImports.Where(i => i.PharmacyId == pharmacyId).OrderByDescending(i => i.ImportedAt)
            .Select(i => new ImportDto(i.Id, i.FileName, i.ImportedAt, i.ImportedBy, i.Rows, i.FirstDay, i.LastDay))
            .ToListAsync(ct);

    /// <summary>Annule un import : ses ventes sont effacées. Les ventes remplacées ne reviennent pas.</summary>
    [HttpDelete("api/sales/imports/{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var import = await db.SalesImports.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (import is null || !User.CanAccess(import.PharmacyId)) return NotFound();
        // PostgreSQL efface les ventes en cascade ; la base en mémoire des tests a besoin qu'on les charge.
        if (!db.Database.IsRelational()) await db.Sales.Where(s => s.ImportId == id).LoadAsync(ct);
        db.SalesImports.Remove(import);
        await db.SaveChangesAsync(ct);
        await notifier.EventsReceivedAsync(import.PharmacyId, 0, [], ct);
        return NoContent();
    }

    /// <summary>Le catalogue est commun à toutes les officines : seul un titulaire de toutes peut le modifier.</summary>
    [HttpPost("api/catalog/import")]
    [RequestSizeLimit(MaxFileBytes + 100_000)]
    public async Task<ActionResult<CatalogImportReport>> Catalog(IFormFile file, bool dryRun = true, CancellationToken ct = default)
    {
        var mine = User.PharmacyIds();
        if (await db.Pharmacies.AnyAsync(p => !mine.Contains(p.Id), ct))
            return StatusCode(StatusCodes.Status403Forbidden,
                "Le catalogue est commun à toutes les officines : il faut avoir accès à chacune pour le modifier");
        if (file.Length > MaxFileBytes) return BadRequest("Fichier limité à 10 Mo");
        return await imports.CatalogAsync(await ReadAsync(file, ct), dryRun, ct);
    }

    private static async Task<byte[]> ReadAsync(IFormFile file, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, ct);
        return buffer.ToArray();
    }
}
