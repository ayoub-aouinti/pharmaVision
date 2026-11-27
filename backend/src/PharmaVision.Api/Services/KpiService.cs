using Microsoft.EntityFrameworkCore;
using PharmaVision.Domain;
using PharmaVision.Domain.Simulation;
using PharmaVision.Infrastructure;

namespace PharmaVision.Api.Services;

public record Kpi(string Key, string Label, double Value, string Unit, double? Trend, bool HigherIsBetter);

public record KpiSummary(DateTime From, DateTime To, int Occupancy, List<Kpi> Items);

public record ShelfRow(
    int ZoneId, string Name, string? Category, string? Brands, string CameraName, double? Fill, ShelfState State,
    int Stops, double EngagementRate, double PickupRate, double Transformation, double RuptureMinutes);

public record HeatCell(int ZoneId, string Name, ZoneType Type, int? X, int? Y, int? W, int? H, double DwellSeconds, double Intensity);

public record HourPoint(int Hour, double Visitors, double Tickets);

public class KpiService(PharmaDbContext db, TimeProvider clock)
{
    private record Totals(EventType Type, int ZoneId, int Count, int LongCount, double Sum);

    private async Task<(Pharmacy Pharmacy, TimeZoneInfo Tz, List<Zone> Zones)?> LoadAsync(int pharmacyId, CancellationToken ct)
    {
        var pharmacy = await db.Pharmacies.AsNoTracking().FirstOrDefaultAsync(p => p.Id == pharmacyId, ct);
        if (pharmacy is null) return null;
        var zones = await db.Zones.AsNoTracking().Include(z => z.Camera).Include(z => z.Rules)
            .Where(z => z.Camera!.Device!.PharmacyId == pharmacyId && z.Type != ZoneType.PrivacyMask)
            .OrderBy(z => z.Id).ToListAsync(ct);
        return (pharmacy, TimeZoneInfo.FindSystemTimeZoneById(pharmacy.TimeZone), zones);
    }

    private Task<List<Totals>> TotalsAsync(List<int> zoneIds, DateTime from, DateTime to, CancellationToken ct) =>
        db.Events.Where(e => zoneIds.Contains(e.ZoneId) && e.Ts >= from && e.Ts < to)
            .GroupBy(e => new { e.Type, e.ZoneId })
            .Select(g => new Totals(
                g.Key.Type, g.Key.ZoneId, g.Count(),
                g.Count(e => e.Value > KpiFormulas.MinStopSeconds), g.Sum(e => e.Value)))
            .ToListAsync(ct);

    /// <summary>Dernière mesure de remplissage connue par zone à l'instant donné.</summary>
    public async Task<Dictionary<int, double>> FillAtAsync(List<int> zoneIds, DateTime at, CancellationToken ct)
    {
        var since = at.AddDays(-3);
        var rows = await db.Events
            .Where(e => zoneIds.Contains(e.ZoneId) && e.Type == EventType.Fill && e.Ts >= since && e.Ts <= at)
            .GroupBy(e => e.ZoneId)
            .Select(g => new { ZoneId = g.Key, Value = g.OrderByDescending(e => e.Ts).Select(e => e.Value).First() })
            .ToListAsync(ct);
        return rows.ToDictionary(r => r.ZoneId, r => r.Value);
    }

    private static double ThresholdOf(Zone zone) =>
        zone.Rules.FirstOrDefault(r => r.Kind == AlertKind.LowFill)?.Threshold ?? KpiFormulas.RuptureThreshold;

    public async Task<KpiSummary?> SummaryAsync(int pharmacyId, Period period, CancellationToken ct)
    {
        if (await LoadAsync(pharmacyId, ct) is not var (_, tz, zones)) return null;
        var now = clock.GetUtcNow().UtcDateTime;
        var range = PeriodRange.Resolve(period, now, tz);
        var ids = zones.Select(z => z.Id).ToList();
        var shelves = zones.Where(z => z.Type == ZoneType.Shelf).ToList();
        var shelfIds = shelves.Select(z => z.Id).ToHashSet();

        async Task<(int Visitors, double Conversion, double Wait, double Ruptures, double Engagement)> Measure(DateTime from, DateTime to)
        {
            var totals = await TotalsAsync(ids, from, to, ct);
            int Count(EventType type) => totals.Where(t => t.Type == type).Sum(t => t.Count);
            var visitors = Count(EventType.Entry);
            var tickets = await db.Sales.Where(s => s.PharmacyId == pharmacyId && s.Ts >= from && s.Ts < to)
                .Select(s => s.TicketNo).Distinct().CountAsync(ct);
            var waitCount = Count(EventType.QueueWait);
            var wait = waitCount == 0 ? 0 : totals.Where(t => t.Type == EventType.QueueWait).Sum(t => t.Sum) / waitCount;
            var fills = await FillAtAsync([.. shelfIds], to, ct);
            var ruptures = shelves.Count(z => fills.TryGetValue(z.Id, out var f) && f < ThresholdOf(z));
            var stops = totals.Where(t => t.Type == EventType.Stop && shelfIds.Contains(t.ZoneId)).Sum(t => t.LongCount);
            var passes = totals.Where(t => t.Type == EventType.Pass && shelfIds.Contains(t.ZoneId)).Sum(t => t.Count);
            return (visitors, KpiFormulas.ConversionRate(tickets, visitors), wait, ruptures,
                KpiFormulas.EngagementRate(stops, passes));
        }

        var current = await Measure(range.From, range.To);
        var previous = await Measure(range.PreviousFrom, range.PreviousTo);

        Kpi Item(string key, string label, double value, double before, string unit, bool higherIsBetter) =>
            new(key, label, value, unit, KpiFormulas.Trend(value, before), higherIsBetter);

        // Occupation : entrées moins sorties sur la dernière heure.
        var recent = await TotalsAsync(ids, now.AddHours(-1), now, ct);
        var occupancy = Math.Max(0, recent.Where(t => t.Type == EventType.Entry).Sum(t => t.Count)
                                    - recent.Where(t => t.Type == EventType.Exit).Sum(t => t.Count));

        var visitorsLabel = period switch
        {
            Period.Live => "Visiteurs (dernière heure)",
            Period.Today => "Visiteurs du jour",
            Period.Week => "Visiteurs sur 7 jours",
            _ => "Visiteurs sur 30 jours",
        };

        return new KpiSummary(range.From, range.To, occupancy,
        [
            Item("visitors", visitorsLabel, current.Visitors, previous.Visitors, "count", true),
            Item("conversion", "Taux de conversion", current.Conversion, previous.Conversion, "percent", true),
            Item("wait", "Attente moyenne au comptoir", current.Wait, previous.Wait, "seconds", false),
            Item("ruptures", "Linéaires en rupture", current.Ruptures, previous.Ruptures, "count", false),
            Item("engagement", "Engagement linéaire", current.Engagement, previous.Engagement, "percent", true),
        ]);
    }

    public async Task<List<ShelfRow>?> ShelvesAsync(int pharmacyId, Period period, CancellationToken ct)
    {
        if (await LoadAsync(pharmacyId, ct) is not var (_, tz, zones)) return null;
        var now = clock.GetUtcNow().UtcDateTime;
        var range = PeriodRange.Resolve(period, now, tz);
        var shelves = zones.Where(z => z.Type == ZoneType.Shelf).ToList();
        var ids = shelves.Select(z => z.Id).ToList();

        var totals = await TotalsAsync(ids, range.From, range.To, ct);
        var fills = await FillAtAsync(ids, now, ct);
        var samples = await db.Events
            .Where(e => ids.Contains(e.ZoneId) && e.Type == EventType.Fill && e.Ts >= range.From && e.Ts < range.To)
            .Select(e => new { e.ZoneId, e.Ts, e.Value })
            .ToListAsync(ct);
        var units = await db.Sales
            .Where(s => s.PharmacyId == pharmacyId && s.Ts >= range.From && s.Ts < range.To)
            .GroupBy(s => s.Family)
            .Select(g => new { Family = g.Key, Units = g.Sum(s => s.Quantity) })
            .ToDictionaryAsync(g => g.Family, g => g.Units, ct);
        var byZone = samples.ToLookup(s => s.ZoneId);

        return shelves.Select(zone =>
        {
            Totals? Of(EventType type) => totals.FirstOrDefault(t => t.ZoneId == zone.Id && t.Type == type);
            var stops = Of(EventType.Stop)?.LongCount ?? 0;
            var passes = Of(EventType.Pass)?.Count ?? 0;
            var pickups = Of(EventType.Pickup)?.Count ?? 0;
            double? fill = fills.TryGetValue(zone.Id, out var f) ? f : null;
            var threshold = ThresholdOf(zone);
            var rupture = KpiFormulas.RuptureDuration(
                byZone[zone.Id].Select(s => (s.Ts, s.Value)), threshold, range.To, TimeSpan.FromMinutes(20));
            return new ShelfRow(
                zone.Id, zone.Name, zone.Category, zone.Brands, zone.Camera!.Name, fill,
                KpiFormulas.StateOf(fill, threshold), stops,
                KpiFormulas.EngagementRate(stops, passes),
                KpiFormulas.PickupRate(pickups, stops),
                KpiFormulas.ShelfTransformation(units.GetValueOrDefault(zone.Category ?? ""), pickups),
                Math.Round(rupture.TotalMinutes));
        }).ToList();
    }

    public async Task<List<HeatCell>?> HeatmapAsync(int pharmacyId, Period period, CancellationToken ct)
    {
        if (await LoadAsync(pharmacyId, ct) is not var (_, tz, zones)) return null;
        var range = PeriodRange.Resolve(period, clock.GetUtcNow().UtcDateTime, tz);
        var totals = await TotalsAsync(zones.Select(z => z.Id).ToList(), range.From, range.To, ct);

        // Temps de présence : arrêts + attente ; un passage ou une entrée compte pour quelques secondes.
        var dwell = zones.ToDictionary(z => z.Id, z => totals.Where(t => t.ZoneId == z.Id).Sum(t => t.Type switch
        {
            EventType.Stop or EventType.QueueWait => t.Sum,
            EventType.Pass or EventType.Entry => t.Count * 3.0,
            _ => 0,
        }));
        var max = dwell.Values.DefaultIfEmpty(0).Max();

        return zones.Select(z => new HeatCell(
            z.Id, z.Name, z.Type, z.PlanX, z.PlanY, z.PlanW, z.PlanH, Math.Round(dwell[z.Id]),
            max <= 0 ? 0 : Math.Sqrt(dwell[z.Id] / max))).ToList();
    }

    /// <summary>Visiteurs et tickets par heure locale (moyenne par jour sur 7 j / 30 j).</summary>
    public async Task<List<HourPoint>?> HourlyAsync(int pharmacyId, Period period, CancellationToken ct)
    {
        if (await LoadAsync(pharmacyId, ct) is not var (_, tz, zones)) return null;
        var effective = period == Period.Live ? Period.Today : period;
        var range = PeriodRange.Resolve(effective, clock.GetUtcNow().UtcDateTime, tz);
        var ids = zones.Select(z => z.Id).ToList();

        var entries = await db.Hourly
            .Where(h => ids.Contains(h.ZoneId) && h.Type == EventType.Entry && h.Bucket >= range.From && h.Bucket < range.To)
            .ToListAsync(ct);
        var sales = await db.Sales
            .Where(s => s.PharmacyId == pharmacyId && s.Ts >= range.From && s.Ts < range.To)
            .Select(s => new { s.TicketNo, s.Ts })
            .ToListAsync(ct);

        int LocalHour(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(utc, tz).Hour;
        var days = effective switch { Period.Week => 7.0, Period.Month => 30.0, _ => 1.0 };
        var visitors = entries.GroupBy(e => LocalHour(e.Bucket)).ToDictionary(g => g.Key, g => g.Sum(e => e.Count));
        var tickets = sales.DistinctBy(s => s.TicketNo).GroupBy(s => LocalHour(s.Ts)).ToDictionary(g => g.Key, g => g.Count());

        // Le simulateur peut produire une activité hors horaires : on l'affiche si elle existe.
        var hours = Enumerable.Range(0, 24).Where(h =>
            h is >= TrafficProfile.OpeningHour and < TrafficProfile.ClosingHour
            || visitors.ContainsKey(h) || tickets.ContainsKey(h));

        return hours.Select(h => new HourPoint(
            h,
            Math.Round(visitors.GetValueOrDefault(h) / days, 1),
            Math.Round(tickets.GetValueOrDefault(h) / days, 1))).ToList();
    }
}
