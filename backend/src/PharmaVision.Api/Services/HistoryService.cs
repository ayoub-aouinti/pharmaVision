using Microsoft.EntityFrameworkCore;
using PharmaVision.Domain;
using PharmaVision.Domain.Simulation;
using PharmaVision.Infrastructure;

namespace PharmaVision.Api.Services;

public record ReplayZone(int Id, string Name, ZoneType Type, int? X, int? Y, int? W, int? H, double? Threshold);

/// <param name="Level">Dernier remplissage connu à la fin de la tranche (linéaires).</param>
/// <param name="Presence">Secondes de présence cumulées sur la tranche.</param>
public record ReplayZoneState(int ZoneId, double? Level, double Presence);

public record ReplayFrame(
    DateTime Start, int Entries, int Exits, int Occupancy, int Tickets, double? WaitSeconds, List<ReplayZoneState> Zones);

public record DayReplay(
    DateOnly Date, DateOnly FirstDay, DateOnly Today, int StepMinutes,
    List<ReplayZone> Zones, List<ReplayFrame> Frames, List<Alert> Alerts);

public record ContextPoint(DateTime Start, double? Value);

public record AlertContext(string Metric, string Unit, double? Threshold, int BucketMinutes, List<ContextPoint> Points);

/// <summary>Écran « Historique &amp; replay » et courbe d'une alerte.</summary>
public class HistoryService(PharmaDbContext db, TimeProvider clock)
{
    private static readonly TimeSpan Step = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan ContextBucket = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ContextBefore = TimeSpan.FromHours(1);
    private static readonly TimeSpan ContextAfter = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan ContextMax = TimeSpan.FromHours(6);

    public async Task<DayReplay?> DayAsync(int pharmacyId, DateOnly? date, CancellationToken ct)
    {
        var pharmacy = await db.Pharmacies.AsNoTracking().FirstOrDefaultAsync(p => p.Id == pharmacyId, ct);
        if (pharmacy is null) return null;
        var tz = TimeZoneInfo.FindSystemTimeZoneById(pharmacy.TimeZone);
        var now = clock.GetUtcNow().UtcDateTime;
        DateOnly LocalDay(DateTime utc) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, tz));
        var today = LocalDay(now);
        var day = date ?? today;

        DateTime At(DateOnly d, int hour) => TimeZoneInfo.ConvertTimeToUtc(d.ToDateTime(new TimeOnly(hour, 0)), tz);
        var from = At(day, 0);
        var to = At(day.AddDays(1), 0);

        var zones = await db.Zones.AsNoTracking().Include(z => z.Rules)
            .Where(z => z.Camera!.Device!.PharmacyId == pharmacyId && z.Type != ZoneType.PrivacyMask)
            .OrderBy(z => z.Id).ToListAsync(ct);
        var ids = zones.Select(z => z.Id).ToList();
        var first = await db.Events.Where(e => ids.Contains(e.ZoneId)).MinAsync(e => (DateTime?)e.Ts, ct);

        var events = await db.Events.AsNoTracking()
            .Where(e => ids.Contains(e.ZoneId) && e.Ts >= from && e.Ts < to && e.Ts <= now)
            .Select(e => new { e.Ts, e.ZoneId, e.Type, e.Value })
            .ToListAsync(ct);
        var sales = await db.Sales
            .Where(s => s.PharmacyId == pharmacyId && s.Ts >= from && s.Ts < to && s.Ts <= now)
            .Select(s => new { s.Ts, s.TicketNo })
            .ToListAsync(ct);
        var alerts = await db.Alerts.AsNoTracking()
            .Where(a => a.PharmacyId == pharmacyId && a.StartedAt < to && a.StartedAt <= now
                        && (a.EndedAt == null || a.EndedAt >= from))
            .OrderBy(a => a.StartedAt).ToListAsync(ct);

        var frames = new List<ReplayFrame>();
        if (events.Count > 0)
        {
            // Au moins les horaires d'ouverture, élargis à toute activité mesurée.
            var open = At(day, TrafficProfile.OpeningHour);
            var close = At(day, TrafficProfile.ClosingHour);
            var start = Buckets.Floor(Earliest(events.Min(e => e.Ts), open), Step, tz);
            var end = Latest(events.Max(e => e.Ts).AddTicks(1), close);
            if (end > now) end = now;

            var byFrame = events.ToLookup(e => Buckets.Floor(e.Ts, Step, tz));
            var tickets = sales.DistinctBy(s => s.TicketNo).ToLookup(s => Buckets.Floor(s.Ts, Step, tz));
            var levels = new Dictionary<int, double>();
            var occupancy = 0;

            foreach (var slot in Buckets.Between(start, end, Step, tz))
            {
                var items = byFrame[slot].OrderBy(e => e.Ts).ToList();
                var entries = items.Count(e => e.Type == EventType.Entry);
                var exits = items.Count(e => e.Type == EventType.Exit);
                occupancy = Math.Max(0, occupancy + entries - exits);
                foreach (var fill in items.Where(e => e.Type == EventType.Fill)) levels[fill.ZoneId] = fill.Value;
                var waits = items.Where(e => e.Type == EventType.QueueWait).Select(e => e.Value).ToList();
                var presence = items.GroupBy(e => e.ZoneId).ToDictionary(g => g.Key, g => g.Sum(e => e.Type switch
                {
                    EventType.Stop or EventType.QueueWait => e.Value,
                    EventType.Pass or EventType.Entry => 3.0,
                    _ => 0,
                }));

                frames.Add(new ReplayFrame(
                    slot, entries, exits, occupancy, tickets[slot].Count(),
                    waits.Count == 0 ? null : Math.Round(waits.Average()),
                    zones.Select(z => new ReplayZoneState(
                        z.Id,
                        z.Type == ZoneType.Shelf && levels.TryGetValue(z.Id, out var level) ? level : null,
                        Math.Round(presence.GetValueOrDefault(z.Id)))).ToList()));
            }
        }

        return new DayReplay(
            day, first is { } oldest ? LocalDay(oldest) : today, today, (int)Step.TotalMinutes,
            zones.Select(z => new ReplayZone(
                z.Id, z.Name, z.Type, z.PlanX, z.PlanY, z.PlanW, z.PlanH,
                z.Type == ZoneType.Shelf
                    ? z.Rules.FirstOrDefault(r => r.Kind == AlertKind.LowFill)?.Threshold ?? KpiFormulas.RuptureThreshold
                    : null)).ToList(),
            frames, alerts);
    }

    /// <summary>Courbe de la zone autour de l'alerte ; null si elle ne porte sur aucune zone.</summary>
    public async Task<AlertContext?> ContextAsync(Alert alert, CancellationToken ct)
    {
        if (alert.ZoneId is not { } zoneId) return null;
        var timeZone = await db.Pharmacies.Where(p => p.Id == alert.PharmacyId).Select(p => p.TimeZone).FirstAsync(ct);
        var tz = TimeZoneInfo.FindSystemTimeZoneById(timeZone);
        var now = clock.GetUtcNow().UtcDateTime;
        var from = alert.StartedAt - ContextBefore;
        var to = Earliest(Earliest((alert.EndedAt ?? now) + ContextAfter, from + ContextMax), now);
        var threshold = await db.AlertRules.Where(r => r.Id == alert.RuleId).Select(r => (double?)r.Threshold)
            .FirstOrDefaultAsync(ct);

        var (type, metric, unit) = alert.Kind switch
        {
            AlertKind.LowFill => (EventType.Fill, "Remplissage", "percent"),
            AlertKind.QueueWait => (EventType.QueueWait, "Attente moyenne", "seconds"),
            AlertKind.Unattended => (EventType.Unattended, "Attente sans conseil", "seconds"),
            _ => (EventType.Stop, "Durée de présence", "seconds"),
        };
        var events = await db.Events.AsNoTracking()
            .Where(e => e.ZoneId == zoneId && e.Type == type && e.Ts >= from && e.Ts < to)
            .Select(e => new { e.Ts, e.Value })
            .ToListAsync(ct);
        var byBucket = events.ToLookup(e => Buckets.Floor(e.Ts, ContextBucket, tz), e => e.Value);
        var average = alert.Kind is AlertKind.LowFill or AlertKind.QueueWait;

        var points = Buckets.Between(from, to, ContextBucket, tz).Select(start =>
        {
            var values = byBucket[start].ToList();
            return new ContextPoint(start, values.Count == 0 ? null
                : Math.Round(average ? values.Average() : values.Max(), 1));
        }).ToList();

        // Le seuil d'une règle de présence réglementée n'a pas de sens sur la courbe.
        return new AlertContext(metric, unit, alert.Kind == AlertKind.RestrictedPresence ? null : threshold,
            (int)ContextBucket.TotalMinutes, points);
    }

    private static DateTime Earliest(DateTime a, DateTime b) => a < b ? a : b;
    private static DateTime Latest(DateTime a, DateTime b) => a > b ? a : b;
}
