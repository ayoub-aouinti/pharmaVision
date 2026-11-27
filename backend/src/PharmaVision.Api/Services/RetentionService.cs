using Microsoft.EntityFrameworkCore;
using PharmaVision.Domain;
using PharmaVision.Infrastructure;

namespace PharmaVision.Api.Services;

/// <summary>
/// Efface, une fois par jour, les mesures détaillées, les ventes et les alertes closes plus anciennes
/// que la durée de conservation choisie par chaque officine.
/// </summary>
public class RetentionService(IServiceScopeFactory scopes, TimeProvider clock, ILogger<RetentionService> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await PurgeAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Purge des données impossible");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task PurgeAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PharmaDbContext>();
        db.Database.SetCommandTimeout(TimeSpan.FromMinutes(5));
        var now = clock.GetUtcNow().UtcDateTime;

        foreach (var pharmacy in await db.Pharmacies.AsNoTracking().ToListAsync(ct))
        {
            var id = pharmacy.Id;
            var limit = now.AddMonths(-pharmacy.StatisticsMonths);
            var zones = db.Zones.Where(z => z.Camera!.Device!.PharmacyId == id).Select(z => z.Id);

            var events = await db.Events.Where(e => e.Ts < limit && zones.Contains(e.ZoneId)).ExecuteDeleteAsync(ct);
            var sales = await db.Sales.Where(s => s.PharmacyId == id && s.Ts < limit).ExecuteDeleteAsync(ct);
            var alerts = await db.Alerts
                .Where(a => a.PharmacyId == id && a.Status == AlertStatus.Resolved && a.StartedAt < limit)
                .ExecuteDeleteAsync(ct);
            if (events + sales + alerts > 0)
                logger.LogInformation("Conservation dépassée, officine {Id} : {Events} mesures, {Sales} ventes, {Alerts} alertes effacées",
                    id, events, sales, alerts);
        }
    }
}
