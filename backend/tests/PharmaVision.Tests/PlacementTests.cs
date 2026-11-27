using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using PharmaVision.Api.Services;
using PharmaVision.Domain;
using PharmaVision.Domain.Placement;
using PharmaVision.Infrastructure;

namespace PharmaVision.Tests;

public class PlacementRulesTests
{
    //                                      id  nom       passages arrêts prises unités  sans contact
    private static readonly ShelfStats Hot = new(1, "Dermo", 1000, 400, 200, 120, 30);
    private static readonly ShelfStats Wasted = new(2, "Solaires", 900, 300, 30, 12, 60);
    private static readonly ShelfStats Hidden = new(3, "Ortho", 200, 60, 40, 15, 200);
    private static readonly ShelfStats Normal = new(4, "Bébé", 700, 270, 120, 70, 40);

    private static PlacementResult Analyze(params ShelfStats[] shelves) => PlacementRules.Analyze(shelves, 120);

    [Fact]
    public void Shelves_are_ranked_from_hot_to_cold()
    {
        var result = Analyze(Hot, Wasted, Hidden, Normal);

        Assert.Equal([1, 4, 3, 2], result.Shelves.Select(s => s.Stats.ZoneId));
        Assert.Equal([Temperature.Hot, Temperature.Warm, Temperature.Cold, Temperature.Cold],
            result.Shelves.Select(s => s.Temperature));
    }

    [Fact]
    public void Findings_explain_why_a_shelf_is_cold()
    {
        var result = Analyze(Hot, Wasted, Hidden, Normal);
        IEnumerable<FindingKind> Of(int id) => result.Shelves.Single(s => s.Stats.ZoneId == id).Findings.Select(f => f.Kind);

        Assert.Equal([FindingKind.LittleTraffic, FindingKind.Untouched], Of(3));
        Assert.Contains(FindingKind.LookedNotTouched, Of(2));
        Assert.Empty(Of(1));
    }

    [Fact]
    public void A_hidden_shelf_that_interests_is_swapped_with_a_busy_one_that_does_not()
    {
        var advice = Analyze(Hot, Wasted, Hidden, Normal).Advice;

        var swap = advice[0];
        Assert.Equal((AdviceKind.Swap, 3, 2), (swap.Kind, swap.ZoneId, swap.OtherZoneId!.Value));
        Assert.Contains("900 clients", swap.Reason.Replace('\u202f', ' ').Replace('\u00a0', ' '));
        Assert.Contains(advice, a => a.Kind == AdviceKind.UseHotSpot && a.ZoneId == 1);
        // Un rayon déjà déplacé ne reçoit pas d'autre conseil.
        Assert.Single(advice, a => a.ZoneId == 2 || a.OtherZoneId == 2 && a.Kind == AdviceKind.Swap);
    }

    [Fact]
    public void Without_a_candidate_for_a_swap_the_hidden_shelf_moves_closer_to_the_traffic()
    {
        var advice = Analyze(Hot, Normal, Hidden).Advice;

        Assert.Equal((AdviceKind.MoveCloser, 3, 1), (advice[0].Kind, advice[0].ZoneId, advice[0].OtherZoneId!.Value));
    }

    [Fact]
    public void An_empty_shelf_is_refilled_before_being_judged()
    {
        var empty = Wasted with { Fill = 10 };

        var advice = Analyze(Hot, empty, Normal).Advice;

        Assert.Equal(AdviceKind.CheckStock, advice.Single(a => a.ZoneId == 2).Kind);
    }

    [Fact]
    public void Every_cold_shelf_receives_a_proposal()
    {
        var result = Analyze(Hot, Wasted, Hidden, Normal);

        foreach (var cold in result.Shelves.Where(s => s.Temperature == Temperature.Cold))
            Assert.Contains(result.Advice, a => a.ZoneId == cold.Stats.ZoneId || a.OtherZoneId == cold.Stats.ZoneId);
    }

    [Fact]
    public void A_hot_empty_shelf_must_be_refilled_at_once()
    {
        var advice = Analyze(Hot with { Fill = 12 }, Normal, Wasted).Advice;

        Assert.Contains("sans attendre", advice.Single(a => a.ZoneId == 1).Title);
    }

    [Fact]
    public void Too_few_measures_give_no_verdict()
    {
        var result = Analyze(new ShelfStats(1, "Neuf", 10, 2, 0, 0, 15));

        Assert.Equal(Temperature.Unknown, result.Shelves[0].Temperature);
        Assert.Empty(result.Advice);
        Assert.Empty(result.Shelves[0].Findings);
    }

    [Fact]
    public void Untouched_time_counts_opening_and_closing_but_not_the_night()
    {
        DateTime At(int day, int hour, int minute = 0) => new(2026, 3, day, hour, minute, 0, DateTimeKind.Utc);
        var openings = new[] { (At(1, 8), At(1, 20)), (At(2, 8), At(2, 20)) };

        var (longest, over) = PlacementRules.Untouched(
            openings, [At(1, 9), At(1, 19), At(2, 14), At(2, 7)], TimeSpan.FromHours(5));

        Assert.Equal(TimeSpan.FromHours(10), longest);
        // 9 h → 19 h le premier jour, 8 h → 14 h puis 14 h → 20 h le second.
        Assert.Equal(3, over);
    }

    [Fact]
    public void A_shelf_never_touched_is_untouched_all_day()
    {
        var open = new DateTime(2026, 3, 1, 8, 0, 0, DateTimeKind.Utc);

        var (longest, over) = PlacementRules.Untouched([(open, open.AddHours(12))], [], TimeSpan.FromHours(2));

        Assert.Equal((TimeSpan.FromHours(12), 1), (longest, over));
    }
}

public class PlacementApiTests : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ApiFactory _api = new();

    public void Dispose() => _api.Dispose();

    [Fact]
    public async Task Report_lists_shelves_and_products_including_those_never_sold()
    {
        var client = await _api.SignedInAs("equipe@test");
        using (var scope = _api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PharmaDbContext>();
            db.Zones.Single(z => z.Id == 1).Category = "Bébé";
            db.Products.AddRange(
                new Product { Code = "A", Name = "Gel", Family = "Bébé" },
                new Product { Code = "B", Name = "Biberon", Family = "Bébé" },
                new Product { Code = "C", Name = "Hors rayon", Family = "Solaires" });
            var ts = DateTime.UtcNow.AddMinutes(-5);
            db.Events.Add(new ZoneEvent { Ts = ts, ZoneId = 1, Type = EventType.Pickup, Value = 1 });
            db.Sales.Add(new Sale { PharmacyId = 1, Ts = ts, TicketNo = "1", ProductCode = "A", Family = "Bébé", Quantity = 3, Amount = 9 });
            await db.SaveChangesAsync();
        }

        var report = await client.GetFromJsonAsync<PlacementReport>("/api/analytics/placement?pharmacyId=1&period=live", Json);

        var shelf = Assert.Single(report!.Shelves);
        Assert.Equal((Temperature.Unknown, 1, 3), (shelf.Temperature, shelf.Pickups, shelf.Units));
        Assert.Equal("A", Assert.Single(report.HotProducts).Code);
        Assert.Equal(("B", 0), (Assert.Single(report.ColdProducts).Code, report.ColdProducts[0].Units));
    }

    [Theory]
    [InlineData("/api/analytics/placement?pharmacyId=2", HttpStatusCode.Forbidden)]
    [InlineData("/api/analytics/placement?pharmacyId=1&untouched=1", HttpStatusCode.BadRequest)]
    public async Task Invalid_or_foreign_requests_are_refused(string url, HttpStatusCode expected)
    {
        var client = await _api.SignedInAs("equipe@test");

        Assert.Equal(expected, (await client.GetAsync(url)).StatusCode);
    }
}
