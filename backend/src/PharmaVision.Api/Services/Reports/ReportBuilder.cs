using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PharmaVision.Domain;
using PharmaVision.Infrastructure;
using static PharmaVision.Api.Services.Reports.ReportCell;

namespace PharmaVision.Api.Services.Reports;

public enum ReportKind { Synthese, Zones, Produits, Laboratoire }

/// <summary>Compose les rapports à partir des mêmes calculs que les écrans.</summary>
public class ReportBuilder(PharmaDbContext db, KpiService kpis, AnalyticsService analytics, TimeProvider clock)
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    private static readonly Dictionary<ShelfState, string> States = new()
    {
        [ShelfState.Conforme] = "Conforme",
        [ShelfState.StockBas] = "Stock bas",
        [ShelfState.Rupture] = "Rupture",
        [ShelfState.Inconnu] = "Sans mesure",
    };

    private static readonly Dictionary<ZoneType, string> ZoneTypes = new()
    {
        [ZoneType.Shelf] = "Linéaire",
        [ZoneType.Passage] = "Passage",
        [ZoneType.Queue] = "File d'attente",
        [ZoneType.CountingLine] = "Ligne de comptage",
        [ZoneType.Restricted] = "Zone réglementée",
        [ZoneType.PrivacyMask] = "Masque",
    };

    private record Context(Pharmacy Pharmacy, TimeZoneInfo Tz, Period Period, PeriodRange Range)
    {
        public string Local(DateTime utc, string format) =>
            TimeZoneInfo.ConvertTimeFromUtc(utc, Tz).ToString(format, French);
    }

    /// <returns>Null si l'officine, ou la marque demandée, est inconnue.</returns>
    public async Task<Report?> BuildAsync(
        ReportKind kind, int pharmacyId, Period period, string? brand, CancellationToken ct)
    {
        var pharmacy = await db.Pharmacies.AsNoTracking().FirstOrDefaultAsync(p => p.Id == pharmacyId, ct);
        if (pharmacy is null) return null;
        var tz = TimeZoneInfo.FindSystemTimeZoneById(pharmacy.TimeZone);
        var context = new Context(pharmacy, tz, period, PeriodRange.Resolve(period, clock.GetUtcNow().UtcDateTime, tz));

        return kind switch
        {
            ReportKind.Synthese => await SummaryAsync(context, ct),
            ReportKind.Zones => await ZonesAsync(context, ct),
            ReportKind.Produits => await ProductsAsync(context, ct),
            _ => await LaboratoryAsync(context, brand, ct),
        };
    }

    private static Report Compose(Context c, string title, IReadOnlyList<ReportSection> sections, params string[] notes) =>
        new(title,
            $"{c.Pharmacy.Name} · {c.Pharmacy.City} — du {c.Local(c.Range.From, "dd/MM/yyyy HH:mm")} "
                + $"au {c.Local(c.Range.To, "dd/MM/yyyy HH:mm")} (heure locale)",
            [.. notes, "Données fictives : aucune caméra ni caisse réelle n'est branchée."],
            sections);

    private async Task<Report> SummaryAsync(Context c, CancellationToken ct)
    {
        var id = c.Pharmacy.Id;
        var summary = (await kpis.SummaryAsync(id, c.Period, ct))!;
        var shelves = (await kpis.ShelvesAsync(id, c.Period, ct))!;
        var hourly = (await kpis.HourlyAsync(id, c.Period, ct))!;
        // Comme le journal : la période, plus tout ce qui est encore en cours.
        var alerts = await db.Alerts.AsNoTracking()
            .Where(a => a.PharmacyId == id && (a.Status != AlertStatus.Resolved || a.StartedAt >= c.Range.From))
            .ToListAsync(ct);
        var stats = AlertStats.Of(alerts);

        ReportCell Value(Kpi kpi) => kpi.Unit switch
        {
            "percent" => Percent(kpi.Value),
            "seconds" => Duration(kpi.Value),
            _ => Integer(kpi.Value),
        };

        return Compose(c, "Synthèse d'activité",
        [
            new("Indicateurs clés", "Indicateurs", ["Indicateur", "Valeur", "Évolution"],
                summary.Items.Select(k => new[] { Text(k.Label), Value(k), Change(k.Trend) }).ToList(),
                "Évolution par rapport à la période précédente comparable."),
            ShelfSection(shelves),
            new("Alertes et interventions", "Alertes", ["Mesure", "Valeur"],
            [
                [Text("Alertes"), Integer(stats.Total)],
                [Text("Encore en cours"), Integer(stats.Active)],
                [Text("Résolues"), Integer(stats.Resolved)],
                [Text("Closes sans intervention"), Integer(stats.AutoResolved)],
                [Text("Délai moyen de prise en charge"), Duration(stats.AvgAcknowledgeSeconds)],
                [Text("Durée moyenne"), Duration(stats.AvgResolutionSeconds)],
            ]),
            new("Affluence par heure", "Affluence", ["Heure", "Visiteurs", "Tickets de caisse"],
                hourly.Select(h => new[] { Text($"{h.Hour} h"), Integer(h.Visitors), Integer(h.Tickets) }).ToList(),
                c.Period is Period.Week or Period.Month ? "Moyenne par jour."
                    : c.Period == Period.Live ? "Journée en cours." : null),
        ]);
    }

    private static ReportSection ShelfSection(List<ShelfRow> shelves) => new(
        "État des linéaires", "Linéaires",
        ["Linéaire", "Remplissage", "État", "Arrêts", "Engagement", "Prise en main", "Transformation", "Sous le seuil"],
        shelves.Select(s => new[]
        {
            Text(s.Name), Percent(s.Fill / 100), Text(States[s.State]), Integer(s.Stops), Percent(s.EngagementRate),
            Percent(s.PickupRate), Percent(s.Transformation), Duration(s.RuptureMinutes * 60),
        }).ToList(),
        "Remplissage et état au dernier relevé ; le reste porte sur la période.");

    private async Task<Report> ZonesAsync(Context c, CancellationToken ct)
    {
        var zones = (await analytics.ZonesAsync(c.Pharmacy.Id, c.Period, ct))!;
        var shelves = (await kpis.ShelvesAsync(c.Pharmacy.Id, c.Period, ct))!;

        return Compose(c, "Rapport par zone",
        [
            new("Toutes les zones", "Zones",
                ["Zone", "Type", "Caméra", "Passages", "Arrêts", "Prises en main", "Attente moyenne", "Alertes en cours"],
                zones.Select(z => new[]
                {
                    Text(z.Name), Text(ZoneTypes[z.Type]), Text(z.CameraName), Integer(z.Visits),
                    Integer(z.Type is ZoneType.Shelf or ZoneType.Passage ? z.Stops : null),
                    Integer(z.Type == ZoneType.Shelf ? z.Pickups : null),
                    Duration(z.AvgWaitSeconds), Integer(z.ActiveAlerts),
                }).ToList(),
                "Passages : entrées, passages, clients servis ou présences selon le type de zone."),
            ShelfSection(shelves),
        ]);
    }

    private async Task<Report> ProductsAsync(Context c, CancellationToken ct)
    {
        var data = (await analytics.ProductsAsync(c.Pharmacy.Id, c.Period, ct))!;
        var notes = new List<string>
        {
            "Les arrêts sont mesurés par linéaire : les marques d'un même rayon les partagent.",
        };
        if (data.UnreferencedRevenue > 0)
            notes.Add($"{data.UnreferencedRevenue.ToString("N3", French)} TND de ventes portent un code absent du catalogue "
                + "et ne sont rattachés à aucune marque.");

        return Compose(c, "Rapport produits et marques",
        [
            new("Familles", "Familles",
                ["Famille", "Chiffre d'affaires", "Évolution", "Unités", "Tickets", "Arrêts", "Prises en main", "Transformation"],
                data.Families.Select(f => new[]
                {
                    Text(f.Family), Money(f.Revenue), Change(f.RevenueTrend), Integer(f.Units), Integer(f.Tickets),
                    Integer(f.HasShelf ? f.Stops : null), Integer(f.HasShelf ? f.Pickups : null), Percent(f.Transformation),
                }).ToList()),
            new("Marques", "Marques",
                ["Marque", "Chiffre d'affaires", "Part", "Évolution", "Unités", "Linéaires", "Arrêts des linéaires"],
                data.Brands.Select(b => new[]
                {
                    Text(b.Brand), Money(b.Revenue), Percent(b.RevenueShare), Change(b.RevenueTrend), Integer(b.Units),
                    Text(string.Join(", ", b.Shelves)), Integer(b.Shelves.Count > 0 ? b.ShelfStops : null),
                }).ToList()),
            ProductSection("Meilleurs produits", data.TopProducts),
        ], [.. notes]);
    }

    private static ReportSection ProductSection(string title, IEnumerable<ProductRow> products) => new(
        title, "Produits", ["Produit", "Code", "Marque", "Famille", "Unités", "Chiffre d'affaires"],
        products.Select(p => new[]
        {
            Text(p.Name), Text(p.Code), Text(p.Brand), Text(p.Family), Integer(p.Units), Money(p.Revenue),
        }).ToList());

    /// <summary>Preuve de visibilité d'une marque : ses ventes et la tenue des linéaires où elle est présente.</summary>
    private async Task<Report?> LaboratoryAsync(Context c, string? brand, CancellationToken ct)
    {
        var name = await db.Products.Where(p => p.Brand != null && p.Brand == brand).Select(p => p.Brand)
            .FirstOrDefaultAsync(ct);
        if (name is null) return null;

        var id = c.Pharmacy.Id;
        var data = (await analytics.ProductsAsync(id, c.Period, ct, top: int.MaxValue))!;
        var sales = data.Brands.FirstOrDefault(b => b.Brand == name);
        var shelves = await analytics.ShelvesListingAsync(id, name, ct);
        var rows = (await kpis.ShelvesAsync(id, c.Period, ct))!.Where(s => shelves.Contains(s.ZoneId)).ToList();
        var details = new List<ZoneDetail>();
        foreach (var shelf in rows)
            details.Add((await analytics.ZoneAsync(id, shelf.ZoneId, c.Period, ct))!);

        var bucket = c.Period switch { Period.Live => "HH:mm", Period.Today => "HH:mm", _ => "dd/MM/yyyy" };
        var history = details.SelectMany(d => d.Points.Where(p => p.Level != null).Select(p => new[]
        {
            Text(d.Name), Text(c.Local(p.Start, bucket)), Percent(p.Level / 100), Percent(p.MinLevel / 100),
            Integer(p.Stops), Integer(p.Pickups),
        })).ToList();

        var notes = new List<string>
        {
            "Les mesures de visibilité portent sur le linéaire entier : la caméra ne reconnaît pas les produits, "
                + "et les marques d'un même rayon partagent ses arrêts et ses prises en main.",
            "Le respect du planogramme n'est pas mesuré à ce stade.",
        };
        if (rows.Count == 0) notes.Add("Aucun linéaire ne mentionne cette marque : seules les ventes sont disponibles.");

        return Compose(c, $"Rapport laboratoire · {name}",
        [
            new("Ventes de la marque", "Ventes", ["Mesure", "Valeur"],
            [
                [Text("Chiffre d'affaires"), Money(sales?.Revenue ?? 0)],
                [Text("Unités vendues"), Integer(sales?.Units ?? 0)],
                [Text("Part des ventes de marques"), Percent(sales?.RevenueShare ?? 0)],
                [Text("Évolution du chiffre d'affaires"), Change(sales?.RevenueTrend)],
            ]),
            ProductSection("Produits vendus", data.TopProducts.Where(p => p.Brand == name)),
            new("Visibilité en rayon", "Visibilité",
                ["Linéaire", "Passages devant", "Arrêts", "Engagement", "Prises en main", "Disponibilité", "Sous le seuil"],
                rows.Zip(details, (shelf, detail) => new[]
                {
                    Text(shelf.Name), Integer(detail.Points.Sum(p => p.Visits)), Integer(shelf.Stops),
                    Percent(shelf.EngagementRate), Integer(detail.Points.Sum(p => p.Pickups)),
                    Percent(detail.Availability), Duration(detail.RuptureMinutes * 60),
                }).ToList(),
                "Disponibilité : part des relevés de remplissage au-dessus du seuil de rupture du linéaire."),
            new("Évolution des linéaires", "Évolution",
                ["Linéaire", "Début", "Remplissage moyen", "Remplissage minimal", "Arrêts", "Prises en main"], history),
        ], [.. notes]);
    }
}
