using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using PharmaVision.Domain;
using PharmaVision.Domain.Simulation;

namespace PharmaVision.Infrastructure.Seeding;

/// <summary>
/// Crée la configuration fictive et 30 jours d'historique ; au redémarrage, comble le trou
/// entre le dernier événement et maintenant.
/// </summary>
public class DataSeeder(PharmaDbContext db, ILogger<DataSeeder> logger)
{
    public const int HistoryDays = 60; // 30 j affichés + 30 j de référence pour les tendances

    public async Task SeedAsync(int seed, CancellationToken ct = default)
    {
        var firstRun = !await db.Pharmacies.AnyAsync(ct);
        if (firstRun)
        {
            db.Pharmacies.AddRange(ReferenceData.Build());
            await db.SaveChangesAsync(ct);
        }

        var now = DateTime.UtcNow;
        var pharmacies = await db.Pharmacies.OrderBy(p => p.Id)
            .Include(p => p.Devices).ThenInclude(d => d.Cameras).ThenInclude(c => c.Zones).ThenInclude(z => z.Rules)
            .AsSplitQuery().ToListAsync(ct);

        var written = 0;
        for (var i = 0; i < pharmacies.Count; i++)
        {
            var pharmacy = pharmacies[i];
            var zones = pharmacy.Devices.SelectMany(d => d.Cameras)
                .Where(c => c.Status == CameraStatus.Online)
                .SelectMany(c => c.Zones)
                .Where(z => z.Type != ZoneType.PrivacyMask)
                .OrderBy(z => z.Id)
                .Select(z => new SimZone(z.Id, z.Type, z.Category))
                .ToList();
            if (zones.Count == 0) continue;

            var zoneIds = zones.Select(z => z.Id).ToList();
            var last = await db.Events.Where(e => zoneIds.Contains(e.ZoneId)).MaxAsync(e => (DateTime?)e.Ts, ct);
            var from = last ?? now.Date.AddDays(-HistoryDays);
            if (now - from < TimeSpan.FromMinutes(15)) continue;

            var daily = i < ReferenceData.Sites.Length ? ReferenceData.Sites[i].DailyVisitors : 300;
            var tz = TimeZoneInfo.FindSystemTimeZoneById(pharmacy.TimeZone);
            var (events, sales) = Generate(new Random(seed + i + (int)(from.Ticks % 100_003)), zones, tz, daily, from, now);
            await CopyAsync(pharmacy.Id, events, sales, ct);
            written += events.Count;

            if (firstRun) db.Alerts.AddRange(PastAlerts(new Random(seed + 100 + i), pharmacy, now));
            foreach (var device in pharmacy.Devices) device.LastSeen ??= now;
        }

        await db.SaveChangesAsync(ct);
        if (written == 0) return;
        await db.Database.ExecuteSqlRawAsync("CALL refresh_continuous_aggregate('events_hourly', NULL, NULL);", ct);
        logger.LogInformation("Données fictives générées : {Count} événements", written);
    }

    public static (List<IngestEvent> Events, List<IngestSale> Sales) Generate(
        Random rng, IReadOnlyList<SimZone> zones, TimeZoneInfo tz, double dailyVisitors, DateTime fromUtc, DateTime toUtc)
    {
        var generator = new VisitGenerator(rng);
        var events = new List<IngestEvent>();
        var sales = new List<IngestSale>();
        var shelves = zones.Where(z => z.Type == ZoneType.Shelf).ToList();

        // Pas de 15 minutes : une mesure de remplissage + les arrivées de la tranche.
        var step = TimeSpan.FromMinutes(15);
        for (var t = fromUtc; t < toUtc; t += step)
        {
            var local = TimeZoneInfo.ConvertTimeFromUtc(t, tz);
            if (!TrafficProfile.IsOpen(local)) continue;

            events.AddRange(shelves.Select(s => generator.SampleFill(s.Id, t)));

            var expected = TrafficProfile.ExpectedVisitors(local, dailyVisitors);
            var load = Math.Clamp(expected / (dailyVisitors / 12), 0.6, 1.4);
            var arrivals = generator.Poisson(expected / 4);
            for (var n = 0; n < arrivals; n++)
            {
                var visit = generator.Generate(t.AddSeconds(generator.Between(0, step.TotalSeconds)), zones, load);
                events.AddRange(visit.Events.Where(e => e.Ts <= toUtc));
                sales.AddRange(visit.Sales.Where(s => s.Ts <= toUtc));
            }
        }
        return (events, sales);
    }

    private async Task CopyAsync(int pharmacyId, List<IngestEvent> events, List<IngestSale> sales, CancellationToken ct)
    {
        await db.Database.OpenConnectionAsync(ct);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        await using (var writer = await connection.BeginBinaryImportAsync(
            "COPY zone_events (ts, zone_id, type, value, track_id) FROM STDIN (FORMAT BINARY)", ct))
        {
            foreach (var e in events.OrderBy(e => e.Ts))
            {
                await writer.StartRowAsync(ct);
                await writer.WriteAsync(e.Ts, NpgsqlDbType.TimestampTz, ct);
                await writer.WriteAsync(e.ZoneId, NpgsqlDbType.Integer, ct);
                await writer.WriteAsync((int)e.Type, NpgsqlDbType.Integer, ct);
                await writer.WriteAsync(e.Value, NpgsqlDbType.Double, ct);
                if (e.TrackId is null) await writer.WriteNullAsync(ct);
                else await writer.WriteAsync(e.TrackId, NpgsqlDbType.Text, ct);
            }
            await writer.CompleteAsync(ct);
        }

        await using (var writer = await connection.BeginBinaryImportAsync(
            """COPY "Sales" ("PharmacyId", "Ts", "TicketNo", "ProductCode", "Family", "Quantity", "Amount") FROM STDIN (FORMAT BINARY)""", ct))
        {
            foreach (var s in sales)
            {
                await writer.StartRowAsync(ct);
                await writer.WriteAsync(pharmacyId, NpgsqlDbType.Integer, ct);
                await writer.WriteAsync(s.Ts, NpgsqlDbType.TimestampTz, ct);
                await writer.WriteAsync(s.TicketNo, NpgsqlDbType.Text, ct);
                await writer.WriteAsync(s.ProductCode, NpgsqlDbType.Text, ct);
                await writer.WriteAsync(s.Family, NpgsqlDbType.Text, ct);
                await writer.WriteAsync(s.Quantity, NpgsqlDbType.Integer, ct);
                await writer.WriteAsync(s.Amount, NpgsqlDbType.Numeric, ct);
            }
            await writer.CompleteAsync(ct);
        }
    }

    private static IEnumerable<Alert> PastAlerts(Random rng, Pharmacy pharmacy, DateTime now)
    {
        var zones = pharmacy.Devices.SelectMany(d => d.Cameras).SelectMany(c => c.Zones)
            .Where(z => z.Type is ZoneType.Shelf or ZoneType.Queue).ToList();
        if (zones.Count == 0) yield break;

        for (var day = 1; day <= HistoryDays; day++)
        {
            for (var n = rng.Next(1, 5); n > 0; n--)
            {
                var zone = zones[rng.Next(zones.Count)];
                var kind = zone.Type == ZoneType.Queue ? AlertKind.QueueWait : AlertKind.LowFill;
                var start = now.Date.AddDays(-day).AddHours(7 + rng.NextDouble() * 10);
                yield return new Alert
                {
                    PharmacyId = pharmacy.Id,
                    ZoneId = zone.Id,
                    CameraId = zone.CameraId,
                    RuleId = zone.Rules.FirstOrDefault(r => r.Kind == kind)?.Id,
                    Kind = kind,
                    Severity = rng.NextDouble() < 0.4 ? AlertSeverity.High : AlertSeverity.Medium,
                    Title = kind == AlertKind.QueueWait ? $"Attente élevée · {zone.Name}" : $"Rupture · {zone.Name}",
                    Detail = kind == AlertKind.QueueWait
                        ? $"Attente moyenne de {rng.Next(4, 8)} min"
                        : $"Remplissage à {rng.Next(8, 29)} %",
                    StartedAt = start,
                    EndedAt = start.AddMinutes(rng.Next(6, 150)),
                    Status = AlertStatus.Resolved,
                    HandledBy = rng.NextDouble() < 0.5 ? "Équipe comptoir" : "Préparateur",
                    ActionTaken = kind == AlertKind.QueueWait ? "notify" : "restock",
                };
            }
        }
    }
}
