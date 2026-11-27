using Microsoft.AspNetCore.SignalR;
using PharmaVision.Api.Security;
using PharmaVision.Domain;

namespace PharmaVision.Api.Hubs;

public class LiveHub : Hub
{
    public static string Group(int pharmacyId) => $"pharmacy-{pharmacyId}";

    public Task Join(int pharmacyId) => Context.User?.CanAccess(pharmacyId) == true
        ? Groups.AddToGroupAsync(Context.ConnectionId, Group(pharmacyId))
        : throw new HubException("Accès refusé à cette pharmacie");

    public Task Leave(int pharmacyId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, Group(pharmacyId));
}

public record ZoneFill(int ZoneId, double Fill);

public class LiveNotifier(IHubContext<LiveHub> hub)
{
    private IClientProxy To(int pharmacyId) => hub.Clients.Group(LiveHub.Group(pharmacyId));

    public Task EventsReceivedAsync(int pharmacyId, int count, List<ZoneFill> fills, CancellationToken ct) =>
        Task.WhenAll(
            To(pharmacyId).SendAsync("kpiUpdate", new { count }, ct),
            fills.Count == 0 ? Task.CompletedTask : To(pharmacyId).SendAsync("zoneUpdate", fills, ct));

    public Task AlertRaisedAsync(Alert alert, CancellationToken ct) =>
        To(alert.PharmacyId).SendAsync("alertRaised", new { alert.Id, alert.Title, alert.Detail, alert.Severity }, ct);

    public Task AlertsChangedAsync(int pharmacyId, CancellationToken ct = default) =>
        To(pharmacyId).SendAsync("alertsChanged", ct);
}
