using PharmaVision.Domain;

namespace PharmaVision.Api.Services;

public record AlertStats(
    int Total, int Active, int Resolved, int AutoResolved,
    double? AvgAcknowledgeSeconds, double? AvgResolutionSeconds)
{
    public static AlertStats Of(IReadOnlyCollection<Alert> alerts)
    {
        static double? Average(IEnumerable<double> values)
        {
            var list = values.ToList();
            return list.Count == 0 ? null : list.Average();
        }

        var resolved = alerts.Where(a => a.Status == AlertStatus.Resolved).ToList();
        return new AlertStats(
            alerts.Count,
            alerts.Count - resolved.Count,
            resolved.Count,
            // Close sans aucune intervention : ni prise en charge, ni clôture manuelle.
            resolved.Count(a => a.AcknowledgedAt is null && a.ResolvedBy is null),
            Average(alerts.Where(a => a.AcknowledgedAt is not null)
                .Select(a => (a.AcknowledgedAt!.Value - a.StartedAt).TotalSeconds)),
            Average(resolved.Where(a => a.EndedAt is not null)
                .Select(a => (a.EndedAt!.Value - a.StartedAt).TotalSeconds)));
    }
}
