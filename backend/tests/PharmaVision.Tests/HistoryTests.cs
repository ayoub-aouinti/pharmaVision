using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using PharmaVision.Api.Services;
using PharmaVision.Domain;
using PharmaVision.Infrastructure;

namespace PharmaVision.Tests;

public class HistoryTests : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ApiFactory _api = new();

    public void Dispose() => _api.Dispose();

    // Hier à midi, heure de Tunis (UTC+1) : une journée complète et passée.
    private static readonly DateTime Noon = DateTime.UtcNow.Date.AddDays(-1).AddHours(11);
    private static readonly string Yesterday = Noon.ToString("yyyy-MM-dd");

    private async Task<HttpClient> SignedInWithMeasures()
    {
        var client = await _api.SignedInAs("equipe@test");
        using var scope = _api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PharmaDbContext>();
        void Event(int minutes, EventType type, double value) =>
            db.Events.Add(new ZoneEvent { Ts = Noon.AddMinutes(minutes), ZoneId = 1, Type = type, Value = value });
        Event(0, EventType.Entry, 1);
        Event(1, EventType.Entry, 1);
        Event(2, EventType.Fill, 80);
        Event(16, EventType.Exit, 1);
        Event(17, EventType.Stop, 40);
        Event(31, EventType.Fill, 12);
        db.Sales.Add(new Sale { PharmacyId = 1, Ts = Noon.AddMinutes(18), TicketNo = "T1", ProductCode = "A", Family = "Bébé", Quantity = 1, Amount = 1 });
        var alert = db.Alerts.Single(a => a.Id == 1);
        alert.StartedAt = Noon.AddMinutes(31);
        await db.SaveChangesAsync();
        return client;
    }

    [Fact]
    public async Task Day_is_replayed_by_quarter_hour()
    {
        var client = await SignedInWithMeasures();

        var day = await client.GetFromJsonAsync<DayReplay>($"/api/history/day?pharmacyId=1&date={Yesterday}", Json);

        Assert.Equal(15, day!.StepMinutes);
        ReplayFrame At(int minutes) => day.Frames.Single(f => f.Start == Noon.AddMinutes(minutes));
        Assert.Equal((2, 0, 2), (At(0).Entries, At(0).Exits, At(0).Occupancy));
        Assert.Equal((1, 1, 1), (At(15).Exits, At(15).Occupancy, At(15).Tickets));
        // Le dernier remplissage connu reste affiché tant qu'aucune nouvelle mesure n'arrive.
        Assert.Equal(80, At(15).Zones[0].Level);
        Assert.Equal(40, At(15).Zones[0].Presence);
        Assert.Equal(12, At(30).Zones[0].Level);
        Assert.Null(day.Frames[0].Zones[0].Level);
        Assert.Equal(1, Assert.Single(day.Alerts).Id);
        Assert.Equal(DateOnly.FromDateTime(Noon), day.FirstDay);
    }

    [Fact]
    public async Task Day_without_measures_has_no_frames()
    {
        var client = await SignedInWithMeasures();

        var day = await client.GetFromJsonAsync<DayReplay>("/api/history/day?pharmacyId=1&date=2001-01-01", Json);

        Assert.Empty(day!.Frames);
    }

    [Fact]
    public async Task Alert_context_follows_the_zone_around_the_alert()
    {
        var client = await SignedInWithMeasures();

        var context = await client.GetFromJsonAsync<AlertContext>("/api/alerts/1/context", Json);

        Assert.Equal(("Remplissage", "percent", 5), (context!.Metric, context.Unit, context.BucketMinutes));
        Assert.Equal([80, 12], context.Points.Where(p => p.Value != null).Select(p => p.Value!.Value));
    }

    [Theory]
    [InlineData("/api/history/day?pharmacyId=2", HttpStatusCode.Forbidden)]
    [InlineData("/api/history/day?pharmacyId=1&date=9999-12-31", HttpStatusCode.BadRequest)]
    [InlineData("/api/history/day?pharmacyId=1&date=hier", HttpStatusCode.BadRequest)]
    [InlineData("/api/alerts/2/context", HttpStatusCode.NotFound)]
    public async Task Invalid_or_foreign_requests_are_refused(string url, HttpStatusCode expected)
    {
        var client = await _api.SignedInAs("equipe@test");

        Assert.Equal(expected, (await client.GetAsync(url)).StatusCode);
    }
}
