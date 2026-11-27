using Microsoft.EntityFrameworkCore;
using PharmaVision.Domain;
using PharmaVision.Infrastructure;

namespace PharmaVision.Api.Services;

public record ZoneRow(
    int ZoneId, string Name, ZoneType Type, int CameraId, string CameraName, CameraStatus CameraStatus,
    string? Category, string? Brands, string? PlanogramRef, int? FacingsExpected, List<AlertKind> Rules,
    int Visits, int Stops, int Pickups, double? Fill, ShelfState? State, double? AvgWaitSeconds, int ActiveAlerts);

/// <summary>
/// Mesures d’une zone sur un intervalle ; remplissage et attente nuls quand rien n’a été mesuré.
/// Pas de propriété « Fill » : le graphique la prendrait pour la couleur des barres.
/// </summary>
public record ZonePointStats(
    DateTime Start, int Visits, int Stops, int Pickups, double? Level, double? MinLevel, double? WaitSeconds);

public record ZoneDetail(
    int ZoneId, string Name, ZoneType Type, int BucketMinutes, double? FillThreshold, double RuptureMinutes,
    double? Availability, double AvgStopSeconds, List<ZonePointStats> Points, int AlertCount, List<Alert> Alerts);

public record FamilyRow(
    string Family, int Units, decimal Revenue, int Tickets, double? RevenueTrend,
    bool HasShelf, int Stops, int Pickups, double? Transformation);

public record BrandRow(
    string Brand, List<string> Families, int Units, decimal Revenue, double? RevenueTrend, double RevenueShare,
    List<string> Shelves, int ShelfStops);

public record ProductRow(string Code, string Name, string Brand, string Family, int Units, decimal Revenue);

public record ProductReport(
    DateTime From, DateTime To, decimal Revenue, int Units, int Tickets, decimal UnreferencedRevenue,
    List<FamilyRow> Families, List<BrandRow> Brands, List<ProductRow> TopProducts);

/// <summary>Écrans « Zones &amp; linéaires » et « Produits &amp; marques ».</summary>
public class AnalyticsService(PharmaDbContext db, KpiService kpis, TimeProvider clock)
{
    private const int TopProductCount = 15;
    private const int ZoneAlertCount = 20;
    private static readonly TimeSpan MaxFillGap = TimeSpan.FromMinutes(20);

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private async Task<TimeZoneInfo?> TimeZoneAsync(int pharmacyId, CancellationToken ct)
    {
        var id = await db.Pharmacies.Where(p => p.Id == pharmacyId).Select(p => p.TimeZone).FirstOrDefaultAsync(ct);
        return id is null ? null : TimeZoneInfo.FindSystemTimeZoneById(id);
    }

    private IQueryable<Zone> ZonesOf(int pharmacyId) => db.Zones.AsNoTracking()
        .Where(z => z.Camera!.Device!.PharmacyId == pharmacyId && z.Type != ZoneType.PrivacyMask);

    private static double ThresholdOf(Zone zone) =>
        zone.Rules.FirstOrDefault(r => r.Kind == AlertKind.LowFill)?.Threshold ?? KpiFormulas.RuptureThreshold;

    /// <summary>Ce qui compte comme un passage : entrée, attente au comptoir, présence ou passage.</summary>
    private static EventType VisitEvent(ZoneType type) => type switch
    {
        ZoneType.CountingLine => EventType.Entry,
        ZoneType.Queue => EventType.QueueWait,
        ZoneType.Restricted => EventType.Stop,
        _ => EventType.Pass,
    };

    public async Task<List<ZoneRow>?> ZonesAsync(int pharmacyId, Period period, CancellationToken ct)
    {
        if (await TimeZoneAsync(pharmacyId, ct) is not { } tz) return null;
        var range = PeriodRange.Resolve(period, Now, tz);
        var zones = await ZonesOf(pharmacyId).Include(z => z.Camera).Include(z => z.Rules)
            .OrderBy(z => z.CameraId).ThenBy(z => z.Id).ToListAsync(ct);
        var ids = zones.Select(z => z.Id).ToList();

        var totals = await db.Events.Where(e => ids.Contains(e.ZoneId) && e.Ts >= range.From && e.Ts < range.To)
            .GroupBy(e => new { e.ZoneId, e.Type })
            .Select(g => new
            {
                g.Key.ZoneId, g.Key.Type, Count = g.Count(),
                LongCount = g.Count(e => e.Value > KpiFormulas.MinStopSeconds), Sum = g.Sum(e => e.Value),
            })
            .ToListAsync(ct);
        var fills = await kpis.FillAtAsync(zones.Where(z => z.Type == ZoneType.Shelf).Select(z => z.Id).ToList(), range.To, ct);
        var alerts = await db.Alerts
            .Where(a => a.PharmacyId == pharmacyId && a.Status != AlertStatus.Resolved && a.ZoneId != null)
            .GroupBy(a => a.ZoneId!.Value)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, ct);

        return zones.Select(zone =>
        {
            var own = totals.Where(t => t.ZoneId == zone.Id).ToDictionary(t => t.Type);
            var waits = own.GetValueOrDefault(EventType.QueueWait);
            var isShelf = zone.Type == ZoneType.Shelf;
            double? fill = isShelf && fills.TryGetValue(zone.Id, out var f) ? f : null;
            return new ZoneRow(
                zone.Id, zone.Name, zone.Type, zone.CameraId, zone.Camera!.Name, zone.Camera.Status,
                zone.Category, zone.Brands, zone.PlanogramRef, zone.FacingsExpected,
                zone.Rules.Where(r => r.Enabled).Select(r => r.Kind).Order().ToList(),
                own.GetValueOrDefault(VisitEvent(zone.Type))?.Count ?? 0,
                own.GetValueOrDefault(EventType.Stop)?.LongCount ?? 0,
                own.GetValueOrDefault(EventType.Pickup)?.Count ?? 0,
                fill,
                isShelf ? KpiFormulas.StateOf(fill, ThresholdOf(zone)) : null,
                waits is null ? null : waits.Sum / waits.Count,
                alerts.GetValueOrDefault(zone.Id));
        }).ToList();
    }

    public async Task<ZoneDetail?> ZoneAsync(int pharmacyId, int zoneId, Period period, CancellationToken ct)
    {
        if (await TimeZoneAsync(pharmacyId, ct) is not { } tz) return null;
        var zone = await ZonesOf(pharmacyId).Include(z => z.Rules).FirstOrDefaultAsync(z => z.Id == zoneId, ct);
        if (zone is null) return null;
        var range = PeriodRange.Resolve(period, Now, tz);
        var events = await db.Events.AsNoTracking()
            .Where(e => e.ZoneId == zoneId && e.Ts >= range.From && e.Ts < range.To)
            .Select(e => new { e.Ts, e.Type, e.Value })
            .ToListAsync(ct);

        var size = Buckets.SizeOf(period);
        var byBucket = events.ToLookup(e => Buckets.Floor(e.Ts, size, tz));
        var visit = VisitEvent(zone.Type);
        var points = Buckets.Between(range.From, range.To, size, tz).Select(start =>
        {
            var items = byBucket[start].ToLookup(e => e.Type, e => e.Value);
            var fill = items[EventType.Fill].ToList();
            var wait = items[EventType.QueueWait].ToList();
            return new ZonePointStats(
                start, items[visit].Count(),
                items[EventType.Stop].Count(v => v > KpiFormulas.MinStopSeconds),
                items[EventType.Pickup].Count(),
                fill.Count == 0 ? null : Math.Round(fill.Average(), 1),
                fill.Count == 0 ? null : fill.Min(),
                wait.Count == 0 ? null : Math.Round(wait.Average()));
        }).ToList();

        double? threshold = zone.Type == ZoneType.Shelf ? ThresholdOf(zone) : null;
        var rupture = threshold is { } limit
            ? KpiFormulas.RuptureDuration(
                events.Where(e => e.Type == EventType.Fill).Select(e => (e.Ts, e.Value)), limit, range.To, MaxFillGap)
            : TimeSpan.Zero;
        var levels = events.Where(e => e.Type == EventType.Fill).Select(e => e.Value).ToList();
        double? availability = threshold is { } minimum && levels.Count > 0
            ? (double)levels.Count(v => v >= minimum) / levels.Count
            : null;
        var stops = events.Where(e => e.Type == EventType.Stop && e.Value > KpiFormulas.MinStopSeconds)
            .Select(e => e.Value).ToList();

        // Comme dans le journal : la période, plus tout ce qui est encore en cours.
        var alerts = db.Alerts.AsNoTracking().Where(a => a.PharmacyId == pharmacyId && a.ZoneId == zoneId
            && (a.Status != AlertStatus.Resolved || a.StartedAt >= range.From));

        return new ZoneDetail(
            zone.Id, zone.Name, zone.Type, (int)size.TotalMinutes, threshold, Math.Round(rupture.TotalMinutes),
            availability, stops.Count == 0 ? 0 : Math.Round(stops.Average()), points,
            await alerts.CountAsync(ct),
            await alerts.OrderByDescending(a => a.StartedAt).Take(ZoneAlertCount).ToListAsync(ct));
    }

    /// <summary>
    /// Ventes par famille, marque et produit. La caméra ne reconnaît pas les produits : arrêts et prises
    /// en main sont mesurés par linéaire, et les marques d'un même rayon les partagent.
    /// </summary>
    public async Task<ProductReport?> ProductsAsync(
        int pharmacyId, Period period, CancellationToken ct, int top = TopProductCount)
    {
        if (await TimeZoneAsync(pharmacyId, ct) is not { } tz) return null;
        var range = PeriodRange.Resolve(period, Now, tz);

        IQueryable<Sale> Between(DateTime from, DateTime to) =>
            db.Sales.Where(s => s.PharmacyId == pharmacyId && s.Ts >= from && s.Ts < to);

        var sales = await Between(range.From, range.To)
            .Select(s => new { s.TicketNo, s.ProductCode, s.Family, s.Quantity, s.Amount })
            .ToListAsync(ct);
        var previous = await Between(range.PreviousFrom, range.PreviousTo)
            .Select(s => new { s.ProductCode, s.Family, s.Amount })
            .ToListAsync(ct);
        var catalog = await db.Products.AsNoTracking().ToDictionaryAsync(p => p.Code, ct);

        var shelves = await ZonesOf(pharmacyId).Where(z => z.Type == ZoneType.Shelf).ToListAsync(ct);
        var shelfIds = shelves.Select(z => z.Id).ToList();
        var activity = await db.Events
            .Where(e => shelfIds.Contains(e.ZoneId) && e.Ts >= range.From && e.Ts < range.To
                && (e.Type == EventType.Pickup || (e.Type == EventType.Stop && e.Value > KpiFormulas.MinStopSeconds)))
            .GroupBy(e => new { e.ZoneId, e.Type })
            .Select(g => new { g.Key.ZoneId, g.Key.Type, Count = g.Count() })
            .ToListAsync(ct);
        int Activity(IEnumerable<Zone> zones, EventType type)
        {
            var set = zones.Select(z => z.Id).ToHashSet();
            return activity.Where(a => a.Type == type && set.Contains(a.ZoneId)).Sum(a => a.Count);
        }

        static double? Trend(decimal current, decimal before) => KpiFormulas.Trend((double)current, (double)before);

        var previousByFamily = previous.GroupBy(s => s.Family).ToDictionary(g => g.Key, g => g.Sum(s => s.Amount));
        var salesByFamily = sales.ToLookup(s => s.Family);
        // Les familles ayant un rayon apparaissent même sans vente : c'est une information.
        var families = sales.Select(s => s.Family)
            .Union(shelves.Where(z => z.Category != null).Select(z => z.Category!))
            .Select(family =>
            {
                var items = salesByFamily[family].ToList();
                var own = shelves.Where(z => z.Category == family).ToList();
                var units = items.Sum(s => s.Quantity);
                var revenue = items.Sum(s => s.Amount);
                var pickups = Activity(own, EventType.Pickup);
                return new FamilyRow(
                    family, units, revenue, items.Select(s => s.TicketNo).Distinct().Count(),
                    Trend(revenue, previousByFamily.GetValueOrDefault(family)),
                    own.Count > 0, Activity(own, EventType.Stop), pickups,
                    pickups == 0 ? null : KpiFormulas.ShelfTransformation(units, pickups));
            })
            .OrderByDescending(f => f.Revenue).ThenBy(f => f.Family)
            .ToList();

        string? BrandOf(string code) => catalog.GetValueOrDefault(code)?.Brand;
        var previousByBrand = previous.Where(s => BrandOf(s.ProductCode) != null)
            .GroupBy(s => BrandOf(s.ProductCode)!).ToDictionary(g => g.Key, g => g.Sum(s => s.Amount));
        var branded = sales.Where(s => BrandOf(s.ProductCode) != null).ToList();
        var brandedRevenue = branded.Sum(s => s.Amount);
        var brands = branded.GroupBy(s => BrandOf(s.ProductCode)!).Select(g =>
        {
            var revenue = g.Sum(s => s.Amount);
            var own = shelves.Where(z => Lists(z.Brands, g.Key)).ToList();
            return new BrandRow(
                g.Key, g.Select(s => s.Family).Distinct().Order().ToList(), g.Sum(s => s.Quantity), revenue,
                Trend(revenue, previousByBrand.GetValueOrDefault(g.Key)),
                brandedRevenue == 0 ? 0 : (double)(revenue / brandedRevenue),
                own.Select(z => z.Name).ToList(), Activity(own, EventType.Stop));
        }).OrderByDescending(b => b.Revenue).ThenBy(b => b.Brand).ToList();

        var products = branded.GroupBy(s => s.ProductCode)
            .Select(g =>
            {
                var p = catalog[g.Key];
                return new ProductRow(p.Code, p.Name, p.Brand!, p.Family, g.Sum(s => s.Quantity), g.Sum(s => s.Amount));
            })
            .OrderByDescending(p => p.Revenue).ThenBy(p => p.Code)
            .Take(top).ToList();

        return new ProductReport(
            range.From, range.To, sales.Sum(s => s.Amount), sales.Sum(s => s.Quantity),
            sales.Select(s => s.TicketNo).Distinct().Count(),
            sales.Where(s => !catalog.ContainsKey(s.ProductCode)).Sum(s => s.Amount),
            families, brands, products);
    }

    /// <summary>Linéaires de l'officine qui mentionnent la marque.</summary>
    public async Task<HashSet<int>> ShelvesListingAsync(int pharmacyId, string brand, CancellationToken ct)
    {
        var shelves = await ZonesOf(pharmacyId).Where(z => z.Type == ZoneType.Shelf).ToListAsync(ct);
        return shelves.Where(z => Lists(z.Brands, brand)).Select(z => z.Id).ToHashSet();
    }

    /// <summary>La saisie des marques d'une zone est libre, séparée par des virgules.</summary>
    private static bool Lists(string? brands, string brand) =>
        brands?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Contains(brand, StringComparer.CurrentCultureIgnoreCase) ?? false;
}
