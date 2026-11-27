using Microsoft.EntityFrameworkCore;
using PharmaVision.Domain;
using PharmaVision.Domain.Import;
using PharmaVision.Infrastructure;

namespace PharmaVision.Api.Services;

/// <param name="ImportId">Renseigné quand les ventes ont été enregistrées.</param>
/// <param name="Replaced">Ventes non importées effacées (ou à effacer) sur les jours couverts.</param>
public record SalesImportReport(
    int? ImportId, int Rows, int NewRows, int Duplicates, int Tickets, decimal Revenue, DateOnly? From, DateOnly? To,
    int Replaced, int UnknownCodes, List<string> UnknownExamples, List<string> FamiliesWithoutShelf,
    int ErrorCount, List<ImportError> Errors);

public record CatalogImportReport(bool Applied, int Rows, int Added, int Updated, int ErrorCount, List<ImportError> Errors);

/// <summary>Import CSV de la caisse. Tout ou rien : un fichier comportant une erreur n'écrit rien.</summary>
public class ImportService(PharmaDbContext db, TimeProvider clock)
{
    private const int ShownErrors = 20;
    private const string NoFamily = "Non classé";

    private static SalesImportReport Refused(int rows, List<ImportError> errors) => new(
        null, rows, 0, 0, 0, 0, null, null, 0, 0, [], [], errors.Count, errors.Take(ShownErrors).ToList());

    public async Task<SalesImportReport?> SalesAsync(
        int pharmacyId, byte[] content, string fileName, string user, bool replace, bool dryRun, CancellationToken ct)
    {
        var pharmacy = await db.Pharmacies.AsNoTracking().FirstOrDefaultAsync(p => p.Id == pharmacyId, ct);
        if (pharmacy is null) return null;
        var tz = TimeZoneInfo.FindSystemTimeZoneById(pharmacy.TimeZone);
        var now = clock.GetUtcNow().UtcDateTime;
        var oldest = now.AddMonths(-pharmacy.StatisticsMonths);

        var (lines, errors) = SalesFile.Parse(content);
        var rows = lines.Count + errors.Count;
        var catalog = await db.Products.AsNoTracking().ToDictionaryAsync(p => p.Code, p => p.Family, ct);
        var sales = new List<Sale>(lines.Count);
        foreach (var line in lines)
        {
            if (tz.IsInvalidTime(line.Ts))
            {
                errors.Add(new(line.Line, "Cette heure n'existe pas (passage à l'heure d'été)"));
                continue;
            }
            var ts = TimeZoneInfo.ConvertTimeToUtc(line.Ts, tz);
            if (ts > now.AddMinutes(5)) errors.Add(new(line.Line, "Vente datée dans le futur"));
            else if (ts < oldest)
                errors.Add(new(line.Line, $"Vente plus ancienne que la durée de conservation ({pharmacy.StatisticsMonths} mois)"));
            else
                sales.Add(new Sale
                {
                    PharmacyId = pharmacyId, Ts = ts, ProductCode = line.Code,
                    // Beaucoup de caisses recommencent la numérotation chaque jour.
                    TicketNo = $"{line.Ts:yyyyMMdd}-{line.Ticket}",
                    Family = line.Family ?? catalog.GetValueOrDefault(line.Code) ?? NoFamily,
                    Quantity = line.Quantity, Amount = line.Amount,
                });
        }
        if (errors.Count > 0) return Refused(rows, errors.OrderBy(e => e.Line).ToList());

        // Jours entiers (heure de l'officine) couverts par le fichier.
        var first = DateOnly.FromDateTime(lines.Min(l => l.Ts));
        var last = DateOnly.FromDateTime(lines.Max(l => l.Ts));
        DateTime Midnight(DateOnly d) => TimeZoneInfo.ConvertTimeToUtc(d.ToDateTime(TimeOnly.MinValue), tz);
        var (from, to) = (Midnight(first), Midnight(last.AddDays(1)));
        var existing = db.Sales.Where(s => s.PharmacyId == pharmacyId && s.Ts >= from && s.Ts < to);

        // Réimporter un fichier ne double rien : chaque ligne déjà importée en absorbe une identique.
        var known = (await existing.Where(s => s.ImportId != null)
                .Select(s => new { s.Ts, s.TicketNo, s.ProductCode, s.Quantity, s.Amount }).ToListAsync(ct))
            .GroupBy(s => (s.Ts, s.TicketNo, s.ProductCode, s.Quantity, s.Amount))
            .ToDictionary(g => g.Key, g => g.Count());
        var fresh = sales.Where(s =>
        {
            var key = (s.Ts, s.TicketNo, s.ProductCode, s.Quantity, s.Amount);
            if (known.GetValueOrDefault(key) == 0) return true;
            known[key]--;
            return false;
        }).ToList();

        var others = existing.Where(s => s.ImportId == null);
        var replaced = replace ? await others.CountAsync(ct) : 0;
        var unknown = fresh.Select(s => s.ProductCode).Where(c => !catalog.ContainsKey(c)).Distinct().Order().ToList();
        var shelves = await db.Zones
            .Where(z => z.Camera!.Device!.PharmacyId == pharmacyId && z.Type == ZoneType.Shelf && z.Category != null)
            .Select(z => z.Category!).Distinct().ToListAsync(ct);

        var importId = dryRun || (fresh.Count == 0 && replaced == 0)
            ? null
            : await SaveAsync(fresh.Count == 0 ? null : new SalesImport
            {
                PharmacyId = pharmacyId, FileName = fileName, ImportedAt = now, ImportedBy = user,
                Rows = fresh.Count, FirstDay = first, LastDay = last,
            }, fresh, replaced == 0 ? null : others, ct);

        return new SalesImportReport(
            importId, rows, fresh.Count, sales.Count - fresh.Count,
            fresh.Select(s => s.TicketNo).Distinct().Count(), fresh.Sum(s => s.Amount), first, last,
            replaced, unknown.Count, unknown.Take(10).ToList(),
            fresh.Select(s => s.Family).Distinct().Except(shelves, StringComparer.CurrentCultureIgnoreCase).Order().ToList(),
            0, []);
    }

    /// <summary>Efface les ventes remplacées et écrit les nouvelles : tout réussit ou rien.</summary>
    private async Task<int?> SaveAsync(SalesImport? import, List<Sale> sales, IQueryable<Sale>? replaced, CancellationToken ct)
    {
        // La base en mémoire des tests ne sait ni copier en bloc ni effacer par requête.
        if (!db.Database.IsRelational())
        {
            if (replaced is not null) db.Sales.RemoveRange(await replaced.ToListAsync(ct));
            if (import is not null)
            {
                import.Sales = sales;
                db.SalesImports.Add(import);
            }
            await db.SaveChangesAsync(ct);
            return import?.Id;
        }

        db.Database.SetCommandTimeout(TimeSpan.FromMinutes(2));
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (replaced is not null) await replaced.ExecuteDeleteAsync(ct);
        if (import is not null)
        {
            db.SalesImports.Add(import);
            await db.SaveChangesAsync(ct);
            foreach (var sale in sales) sale.ImportId = import.Id;
            await db.CopySalesAsync(sales, ct);
        }
        await transaction.CommitAsync(ct);
        return import?.Id;
    }

    /// <summary>Ajoute les produits nouveaux et met à jour les codes connus ; rien n'est supprimé.</summary>
    public async Task<CatalogImportReport> CatalogAsync(byte[] content, bool dryRun, CancellationToken ct)
    {
        var (products, errors) = CatalogFile.Parse(content);
        if (errors.Count > 0)
            return new(false, products.Count + errors.Count, 0, 0, errors.Count, errors.Take(ShownErrors).ToList());

        var existing = await db.Products.ToDictionaryAsync(p => p.Code, ct);
        int added = 0, updated = 0;
        foreach (var product in products)
        {
            if (!existing.TryGetValue(product.Code, out var current))
            {
                db.Products.Add(product);
                added++;
            }
            else if ((current.Name, current.Brand, current.Family, current.Price)
                     != (product.Name, product.Brand, product.Family, product.Price))
            {
                (current.Name, current.Brand, current.Family, current.Price) =
                    (product.Name, product.Brand, product.Family, product.Price);
                updated++;
            }
        }
        if (!dryRun) await db.SaveChangesAsync(ct);
        return new(!dryRun, products.Count, added, updated, 0, []);
    }
}
