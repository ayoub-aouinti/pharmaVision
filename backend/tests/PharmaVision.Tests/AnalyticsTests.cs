using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using PharmaVision.Api.Services;
using PharmaVision.Domain;
using PharmaVision.Infrastructure;

namespace PharmaVision.Tests;

public class AnalyticsTests : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ApiFactory _api = new();

    public void Dispose() => _api.Dispose();

    /// <summary>Quelques mesures de la dernière heure sur le rayon de l'officine 1.</summary>
    private async Task<HttpClient> SignedInWithMeasures()
    {
        var client = await _api.SignedInAs("equipe@test");
        using var scope = _api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PharmaDbContext>();
        var zone = db.Zones.Single(z => z.Id == 1);
        zone.Category = "Bébé";
        zone.Brands = "Mustela, chicco";

        var ts = DateTime.UtcNow.AddMinutes(-10);
        void Event(EventType type, double value, int zoneId = 1) =>
            db.Events.Add(new ZoneEvent { Ts = ts = ts.AddSeconds(1), ZoneId = zoneId, Type = type, Value = value });
        for (var i = 0; i < 4; i++) Event(EventType.Pass, 1);
        Event(EventType.Stop, 20);
        Event(EventType.Stop, 3); // trop court pour compter
        Event(EventType.Pickup, 1);
        Event(EventType.Pickup, 1);
        Event(EventType.Fill, 12);
        Event(EventType.Stop, 50, zoneId: 2); // autre officine

        db.Products.AddRange(
            new Product { Code = "A", Name = "Gel", Brand = "Mustela", Family = "Bébé", Price = 10 },
            new Product { Code = "B", Name = "Biberon", Brand = "Chicco", Family = "Bébé", Price = 30 });
        void Sale(string ticket, string code, string family, int qty, decimal amount, int pharmacy = 1) =>
            db.Sales.Add(new Sale
            {
                PharmacyId = pharmacy, Ts = ts, TicketNo = ticket, ProductCode = code, Family = family,
                Quantity = qty, Amount = amount,
            });
        Sale("T1", "A", "Bébé", 2, 20);
        Sale("T1", "B", "Bébé", 1, 30);
        Sale("T2", "ancien-code", "Bébé", 1, 5);
        Sale("T3", "A", "Bébé", 9, 90, pharmacy: 2);
        await db.SaveChangesAsync();
        return client;
    }

    [Fact]
    public async Task Zone_list_reports_the_activity_of_the_period()
    {
        var client = await SignedInWithMeasures();

        var zones = await client.GetFromJsonAsync<List<ZoneRow>>("/api/analytics/zones?pharmacyId=1&period=live", Json);

        var shelf = Assert.Single(zones!);
        Assert.Equal((4, 1, 2, 12.0, ShelfState.Rupture, 1),
            (shelf.Visits, shelf.Stops, shelf.Pickups, shelf.Fill!.Value, shelf.State!.Value, shelf.ActiveAlerts));
    }

    [Fact]
    public async Task Zone_detail_splits_the_period_into_intervals()
    {
        var client = await SignedInWithMeasures();

        var detail = await client.GetFromJsonAsync<ZoneDetail>("/api/analytics/zones/1?pharmacyId=1&period=live", Json);

        Assert.Equal(5, detail!.BucketMinutes);
        Assert.InRange(detail.Points.Count, 12, 13);
        Assert.Equal((4, 1, 2), (detail.Points.Sum(p => p.Visits), detail.Points.Sum(p => p.Stops), detail.Points.Sum(p => p.Pickups)));
        Assert.Equal(12, Assert.Single(detail.Points, p => p.Level != null).Level);
        Assert.Equal(20, detail.AvgStopSeconds);
        Assert.Equal(1, detail.AlertCount);
    }

    [Fact]
    public async Task Product_report_groups_sales_by_family_and_brand()
    {
        var client = await SignedInWithMeasures();

        var report = await client.GetFromJsonAsync<ProductReport>("/api/analytics/products?pharmacyId=1&period=live", Json);

        Assert.Equal((55m, 4, 2, 5m), (report!.Revenue, report.Units, report.Tickets, report.UnreferencedRevenue));
        var family = Assert.Single(report.Families);
        Assert.Equal((1, 2, 2.0), (family.Stops, family.Pickups, family.Transformation!.Value));
        Assert.Equal(["Chicco", "Mustela"], report.Brands.Select(b => b.Brand));
        Assert.Equal(0.6, report.Brands[0].RevenueShare, 3);
        // La marque saisie en minuscules sur la zone est reconnue, les arrêts du rayon sont partagés.
        Assert.All(report.Brands, b => Assert.Equal((1, "Rayon"), (b.ShelfStops, Assert.Single(b.Shelves))));
        Assert.Equal("B", report.TopProducts[0].Code);
    }

    [Theory]
    [InlineData("/api/analytics/zones?pharmacyId=2", HttpStatusCode.Forbidden)]
    [InlineData("/api/analytics/products?pharmacyId=2", HttpStatusCode.Forbidden)]
    [InlineData("/api/analytics/zones/2?pharmacyId=2", HttpStatusCode.Forbidden)]
    [InlineData("/api/analytics/zones/2?pharmacyId=1", HttpStatusCode.NotFound)]
    public async Task Another_pharmacy_stays_invisible(string url, HttpStatusCode expected)
    {
        var client = await _api.SignedInAs("titulaire@test");

        Assert.Equal(expected, (await client.GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task Anonymous_requests_are_rejected() =>
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await _api.Anonymous().GetAsync("/api/analytics/products?pharmacyId=1")).StatusCode);

    [Fact]
    public void Daily_intervals_follow_the_local_midnight()
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById("Africa/Tunis"); // UTC+1
        var from = new DateTime(2026, 11, 24, 23, 0, 0, DateTimeKind.Utc);

        var days = Buckets.Between(from, from.AddDays(2).AddHours(10), TimeSpan.FromDays(1), tz).ToList();

        Assert.Equal([from, from.AddDays(1), from.AddDays(2)], days);
        Assert.Equal(from, Buckets.Floor(from.AddHours(23.9), TimeSpan.FromDays(1), tz));
    }

    [Fact]
    public void Catalog_references_every_simulated_family()
    {
        Assert.Equal(ProductCatalog.All.Count, ProductCatalog.All.Select(p => p.Code).Distinct().Count());
        foreach (var family in new[] { "Dermo-cosmétique", "Bébé", "Solaires", "Orthopédie", "Médicaments conseil", "Hygiène", "Compléments", "Ordonnance" })
            Assert.NotEmpty(ProductCatalog.Of(family));
    }
}
