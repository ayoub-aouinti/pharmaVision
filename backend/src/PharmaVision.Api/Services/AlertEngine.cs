using Microsoft.EntityFrameworkCore;
using PharmaVision.Api.Hubs;
using PharmaVision.Domain;
using PharmaVision.Infrastructure;

namespace PharmaVision.Api.Services;

public record AlertCandidate(AlertSeverity Severity, string Title, string Detail);

/// <summary>Décision pure : la condition d'une règle est-elle remplie par les mesures récentes ?</summary>
public static class AlertEvaluator
{
    /// <param name="values">
    /// LowFill : dernier remplissage ; QueueWait et Unattended : valeurs sur la fenêtre de la règle ;
    /// RestrictedPresence : durées de présence sur la fenêtre.
    /// </param>
    public static AlertCandidate? Evaluate(AlertRule rule, string zoneName, IReadOnlyList<double> values)
    {
        if (values.Count == 0) return null;
        switch (rule.Kind)
        {
            case AlertKind.LowFill:
                var fill = values[^1];
                return fill >= rule.Threshold ? null : new(
                    fill < rule.Threshold / 2 ? AlertSeverity.High : AlertSeverity.Medium,
                    $"Rupture · {zoneName}", $"Remplissage à {fill:0} % (seuil {rule.Threshold:0} %)");
            case AlertKind.QueueWait:
                var wait = values.Average();
                return wait <= rule.Threshold ? null : new(
                    wait > rule.Threshold * 1.4 ? AlertSeverity.High : AlertSeverity.Medium,
                    $"Attente élevée · {zoneName}", $"Attente moyenne de {Duration(wait)}");
            case AlertKind.Unattended:
                var alone = values.Max();
                return alone <= rule.Threshold ? null : new(
                    AlertSeverity.Medium,
                    $"Client sans conseil · {zoneName}", $"En attente depuis {Duration(alone)}");
            case AlertKind.RestrictedPresence:
                return new(AlertSeverity.High, $"Zone réglementée · {zoneName}", $"Présence détectée ({Duration(values.Max())})");
            default:
                return null;
        }
    }

    private static string Duration(double seconds) =>
        seconds < 60 ? $"{seconds:0} s" : $"{(int)seconds / 60} min {(int)seconds % 60:00} s";
}

public class AlertEngine(IServiceScopeFactory scopes, LiveNotifier notifier, TimeProvider clock, ILogger<AlertEngine> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await EvaluateAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Évaluation des alertes impossible");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task EvaluateAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PharmaDbContext>();
        var now = clock.GetUtcNow().UtcDateTime;
        var recentlyEnded = now - Cooldown;

        var rules = await db.AlertRules.AsNoTracking().Where(r => r.Enabled)
            .Include(r => r.Zone).ThenInclude(z => z!.Camera).ThenInclude(c => c!.Device)
            .ToListAsync(ct);
        var alerts = await db.Alerts
            .Where(a => a.Status != AlertStatus.Resolved || a.EndedAt > recentlyEnded)
            .ToListAsync(ct);

        var raised = new List<Alert>();
        var changed = new HashSet<int>();

        foreach (var rule in rules)
        {
            var zone = rule.Zone!;
            if (zone.Camera!.Status != CameraStatus.Online) continue;

            var type = rule.Kind switch
            {
                AlertKind.LowFill => EventType.Fill,
                AlertKind.QueueWait => EventType.QueueWait,
                AlertKind.Unattended => EventType.Unattended,
                _ => EventType.Stop,
            };
            var window = rule.Kind == AlertKind.LowFill
                ? TimeSpan.FromHours(1)
                : TimeSpan.FromSeconds(Math.Max(rule.DurationSec, 60));
            var since = now - window;
            var values = await db.Events
                .Where(e => e.ZoneId == zone.Id && e.Type == type && e.Ts >= since && e.Ts <= now)
                .OrderBy(e => e.Ts).Select(e => e.Value).ToListAsync(ct);

            var candidate = AlertEvaluator.Evaluate(rule, zone.Name, values);
            var active = alerts.FirstOrDefault(a => a.RuleId == rule.Id && a.Status != AlertStatus.Resolved);
            var pharmacyId = zone.Camera.Device!.PharmacyId;

            if (candidate is null)
            {
                // Sans mesure de remplissage on ne sait rien : l'alerte reste ouverte.
                if (active is null || (rule.Kind == AlertKind.LowFill && values.Count == 0)) continue;
                active.Status = AlertStatus.Resolved;
                active.EndedAt = now;
                changed.Add(pharmacyId);
            }
            else if (active is not null)
            {
                if (active.Detail == candidate.Detail) continue;
                active.Detail = candidate.Detail;
                active.Severity = candidate.Severity;
                changed.Add(pharmacyId);
            }
            else if (!alerts.Any(a => a.RuleId == rule.Id))
            {
                raised.Add(new Alert
                {
                    PharmacyId = pharmacyId, RuleId = rule.Id, ZoneId = zone.Id, CameraId = zone.CameraId,
                    Kind = rule.Kind, Severity = candidate.Severity, Title = candidate.Title,
                    Detail = candidate.Detail, StartedAt = now, Status = AlertStatus.Open,
                });
            }
        }

        var cameras = await db.Cameras.AsNoTracking().Include(c => c.Device).ToListAsync(ct);
        foreach (var camera in cameras)
        {
            var existing = alerts.Where(a => a.Kind == AlertKind.CameraOffline && a.CameraId == camera.Id).ToList();
            if (camera.Status == CameraStatus.Offline && existing.Count == 0)
            {
                raised.Add(new Alert
                {
                    PharmacyId = camera.Device!.PharmacyId, CameraId = camera.Id, Kind = AlertKind.CameraOffline,
                    Severity = AlertSeverity.Medium, Title = $"Caméra hors ligne · {camera.Name}",
                    Detail = "Aucun flux reçu du boîtier", StartedAt = now, Status = AlertStatus.Open,
                });
            }
            else if (camera.Status != CameraStatus.Offline)
            {
                foreach (var alert in existing.Where(a => a.Status != AlertStatus.Resolved))
                {
                    alert.Status = AlertStatus.Resolved;
                    alert.EndedAt = now;
                    changed.Add(alert.PharmacyId);
                }
            }
        }

        db.Alerts.AddRange(raised);
        await db.SaveChangesAsync(ct);

        foreach (var alert in raised) await notifier.AlertRaisedAsync(alert, ct);
        foreach (var pharmacyId in changed) await notifier.AlertsChangedAsync(pharmacyId, ct);
    }
}
