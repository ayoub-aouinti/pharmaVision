using Microsoft.EntityFrameworkCore;
using PharmaVision.Domain;
using PharmaVision.Domain.Placement;
using PharmaVision.Domain.Simulation;
using PharmaVision.Infrastructure;

namespace PharmaVision.Api.Services;

/// <param name="UntouchedTimes">Nombre d'intervalles sans contact au moins aussi longs que la limite.</param>
/// <param name="SinceLastTouchMinutes">Minutes d'ouverture écoulées aujourd'hui depuis le dernier contact.</param>
public record PlacementShelf(
    int ZoneId, string Name, string? Category, Temperature Temperature, double Score,
    int Passes, int Stops, int Pickups, int Units, double Engagement, double PickupRate,
    double LongestUntouchedMinutes, int UntouchedTimes, double? SinceLastTouchMinutes, List<Finding> Findings);

public record PlacementProduct(string Code, string Name, string? Brand, string Family, int Units, decimal Revenue, int PreviousUnits);

public record PlacementReport(
    DateTime From, DateTime To, int UntouchedLimitMinutes, List<PlacementShelf> Shelves, List<Advice> Advice,
    List<PlacementProduct> HotProducts, List<PlacementProduct> ColdProducts, List<string> NotMeasured);

/// <summary>Écran « Chaud &amp; froid » : ce qui marche, ce qui dort, et quoi déplacer.</summary>
public class PlacementService(PharmaDbContext db, KpiService kpis, TimeProvider clock)
{
    private const int ProductCount = 8;

    public async Task<PlacementReport?> AnalyzeAsync(int pharmacyId, Period period, int untouchedLimit, CancellationToken ct)
    {
        var timeZone = await db.Pharmacies.Where(p => p.Id == pharmacyId).Select(p => p.TimeZone).FirstOrDefaultAsync(ct);
        if (timeZone is null) return null;
        var tz = TimeZoneInfo.FindSystemTimeZoneById(timeZone);
        var now = clock.GetUtcNow().UtcDateTime;
        var range = PeriodRange.Resolve(period, now, tz);
        var all = await db.Zones.AsNoTracking().Include(z => z.Camera)
            .Where(z => z.Camera!.Device!.PharmacyId == pharmacyId && z.Type == ZoneType.Shelf
                        && z.Camera.Status == CameraStatus.Online)
            .OrderBy(z => z.Id).ToListAsync(ct);
        // Un boîtier qui n'a signalé aucun contact depuis 30 jours ne les mesure pas : juger ses rayons
        // « jamais touchés » serait faux.
        var allIds = all.Select(z => z.Id).ToList();
        var since30 = now.AddDays(-30);
        var touching = await db.Events
            .Where(e => allIds.Contains(e.ZoneId) && e.Type == EventType.Pickup && e.Ts >= since30)
            .Select(e => e.ZoneId).Distinct().ToListAsync(ct);
        var devices = all.Where(z => touching.Contains(z.Id)).Select(z => z.Camera!.DeviceId).ToHashSet();
        var shelves = all.Where(z => devices.Contains(z.Camera!.DeviceId)).ToList();
        var ids = shelves.Select(z => z.Id).ToList();

        var totals = await db.Events
            .Where(e => ids.Contains(e.ZoneId) && e.Ts >= range.From && e.Ts < range.To
                        && (e.Type == EventType.Pass || e.Type == EventType.Pickup
                            || (e.Type == EventType.Stop && e.Value > KpiFormulas.MinStopSeconds)))
            .GroupBy(e => new { e.ZoneId, e.Type })
            .Select(g => new { g.Key.ZoneId, g.Key.Type, Count = g.Count() })
            .ToListAsync(ct);
        var touches = (await db.Events
                .Where(e => ids.Contains(e.ZoneId) && e.Type == EventType.Pickup && e.Ts >= range.From && e.Ts < range.To)
                .Select(e => new { e.ZoneId, e.Ts }).ToListAsync(ct))
            .ToLookup(e => e.ZoneId, e => e.Ts);
        var fills = await kpis.FillAtAsync(ids, range.To, ct);

        var sales = await db.Sales.Where(s => s.PharmacyId == pharmacyId && s.Ts >= range.From && s.Ts < range.To)
            .GroupBy(s => new { s.ProductCode, s.Family })
            .Select(g => new { g.Key.ProductCode, g.Key.Family, Units = g.Sum(s => s.Quantity), Revenue = g.Sum(s => s.Amount) })
            .ToListAsync(ct);
        var previous = await db.Sales
            .Where(s => s.PharmacyId == pharmacyId && s.Ts >= range.PreviousFrom && s.Ts < range.PreviousTo)
            .GroupBy(s => s.ProductCode)
            .Select(g => new { g.Key, Units = g.Sum(s => s.Quantity) })
            .ToDictionaryAsync(g => g.Key, g => g.Units, ct);

        // Seules les heures où des mesures existent comptent : ni la nuit, ni un jour de fermeture ou de coupure.
        var openings = await OpeningsAsync(ids, range, now, tz, ct);
        var limit = TimeSpan.FromMinutes(untouchedLimit);
        var familyShelves = shelves.Where(z => z.Category != null).GroupBy(z => z.Category!).ToDictionary(g => g.Key, g => g.Count());
        var untouched = new Dictionary<int, (TimeSpan Longest, int Over, double? Since)>();

        var stats = shelves.Select(zone =>
        {
            int Count(EventType type) => totals.FirstOrDefault(t => t.ZoneId == zone.Id && t.Type == type)?.Count ?? 0;
            var times = touches[zone.Id].ToList();
            var (longest, over) = PlacementRules.Untouched(openings, times, limit);
            var today = openings.Count > 0 && openings[^1].To >= now.AddMinutes(-20) ? openings[^1] : default;
            double? since = today == default ? null
                : Math.Round((today.To - times.Where(t => t >= today.From).DefaultIfEmpty(today.From).Max()).TotalMinutes);
            untouched[zone.Id] = (longest, over, since);
            return new ShelfStats(
                zone.Id, zone.Name, Count(EventType.Pass), Count(EventType.Stop), Count(EventType.Pickup),
                sales.Where(s => s.Family == zone.Category).Sum(s => s.Units), Math.Round(longest.TotalMinutes),
                fills.TryGetValue(zone.Id, out var fill) ? fill : null,
                familyShelves.GetValueOrDefault(zone.Category ?? "", 1));
        }).ToList();

        var result = PlacementRules.Analyze(stats, untouchedLimit);
        var categories = shelves.ToDictionary(z => z.Id, z => z.Category);

        // Les médicaments des familles présentes en rayon, y compris ceux qui ne se vendent pas.
        var families = familyShelves.Keys.ToList();
        var catalog = await db.Products.AsNoTracking().Where(p => families.Contains(p.Family)).ToListAsync(ct);
        var products = catalog.Select(p =>
        {
            var sold = sales.Where(s => s.ProductCode == p.Code).ToList();
            return new PlacementProduct(p.Code, p.Name, p.Brand, p.Family, sold.Sum(s => s.Units), sold.Sum(s => s.Revenue),
                previous.GetValueOrDefault(p.Code));
        }).ToList();
        var hot = products.Where(p => p.Units > 0).OrderByDescending(p => p.Units).ThenBy(p => p.Code).Take(ProductCount).ToList();
        var cold = products.Except(hot).OrderBy(p => p.Units).ThenByDescending(p => p.PreviousUnits).ThenBy(p => p.Code)
            .Take(ProductCount).ToList();

        return new PlacementReport(
            range.From, range.To, untouchedLimit,
            result.Shelves.Select(d =>
            {
                var s = d.Stats;
                var u = untouched[s.ZoneId];
                return new PlacementShelf(
                    s.ZoneId, s.Name, categories[s.ZoneId], d.Temperature, d.Score, s.Passes, s.Stops, s.Pickups, s.Units,
                    s.Engagement, s.PickupRate, s.LongestUntouchedMinutes, u.Over, u.Since, d.Findings);
            }).ToList(),
            result.Advice, hot, cold, all.Except(shelves).Select(z => z.Name).ToList());
    }

    /// <summary>Plages observées : pour chaque jour, des heures d'ouverture couvertes par des mesures.</summary>
    private async Task<List<(DateTime From, DateTime To)>> OpeningsAsync(
        List<int> zoneIds, PeriodRange range, DateTime now, TimeZoneInfo tz, CancellationToken ct)
    {
        var seen = await db.Events
            .Where(e => zoneIds.Contains(e.ZoneId) && (e.Type == EventType.Fill || e.Type == EventType.Pass)
                        && e.Ts >= range.From && e.Ts < range.To)
            .Select(e => e.Ts).ToListAsync(ct);
        return seen.Select(t => (Utc: t, Local: TimeZoneInfo.ConvertTimeFromUtc(t, tz)))
            .Where(t => t.Local.Hour >= TrafficProfile.OpeningHour && t.Local.Hour < TrafficProfile.ClosingHour)
            .GroupBy(t => t.Local.Date)
            .Select(g => (From: g.Min(t => t.Utc), To: g.Max(t => t.Utc)))
            // La journée en cours s'étend jusqu'à maintenant si le boîtier mesure toujours.
            .Select(o => (o.From, To: o.To >= now.AddMinutes(-20) ? now : o.To))
            .Where(o => o.To > o.From)
            .OrderBy(o => o.From)
            .ToList();
    }
}
