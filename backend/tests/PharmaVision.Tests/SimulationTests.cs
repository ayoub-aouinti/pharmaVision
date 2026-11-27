using PharmaVision.Api.Services;
using PharmaVision.Domain;
using PharmaVision.Domain.Simulation;
using PharmaVision.Infrastructure.Seeding;
using PharmaVision.Simulator;

namespace PharmaVision.Tests;

public class SimulationTests
{
    private static readonly SimZone[] Zones =
    [
        new(1, ZoneType.CountingLine, null),
        new(2, ZoneType.Passage, null),
        new(3, ZoneType.Queue, null),
        new(4, ZoneType.Shelf, "Dermo-cosmétique"),
        new(5, ZoneType.Shelf, "Bébé"),
    ];

    private static (List<IngestEvent> Events, List<IngestSale> Sales) Week(int seed) =>
        DataSeeder.Generate(new Random(seed), Zones, TimeZoneInfo.Utc, 350,
            new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 9, 0, 0, 0, DateTimeKind.Utc));

    [Fact]
    public void Same_seed_gives_same_data()
    {
        var a = Week(7);
        var b = Week(7);

        Assert.Equal(a.Events, b.Events);
        Assert.Equal(a.Sales, b.Sales);
        Assert.NotEqual(a.Events, Week(8).Events);
    }

    [Fact]
    public void Generated_volumes_are_realistic()
    {
        var (events, sales) = Week(42);
        var visitors = events.Count(e => e.Type == EventType.Entry);
        var tickets = sales.Select(s => s.TicketNo).Distinct().Count();

        Assert.InRange(visitors / 7.0, 250, 450);
        Assert.InRange(KpiFormulas.ConversionRate(tickets, visitors), 0.55, 0.70);
        Assert.All(events.Where(e => e.Type == EventType.Fill), e => Assert.InRange(e.Value, 0, 100));
        Assert.All(events.Where(e => e.Type == EventType.Stop), e => Assert.True(e.Value > KpiFormulas.MinStopSeconds));
        Assert.Contains(events, e => e.Type == EventType.Fill && e.Value < KpiFormulas.LowStockThreshold);
    }

    [Fact]
    public void Nothing_is_generated_when_closed()
    {
        var (events, _) = DataSeeder.Generate(new Random(1), Zones, TimeZoneInfo.Utc, 350,
            new DateTime(2026, 11, 2, 21, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 3, 7, 0, 0, DateTimeKind.Utc));

        Assert.Empty(events);
    }

    [Fact]
    public void Restock_request_refills_the_shelf_once()
    {
        var device = new DeviceSimulator(new Random(3), 300, new SimulatorOptions { AlwaysOpen = false });
        var zones = new List<DeviceZone> { new(4, "Dermo", ZoneType.Shelf, "Dermo-cosmétique", 12) };
        var now = new DateTime(2026, 11, 27, 2, 0, 0, DateTimeKind.Utc); // fermé : aucun visiteur
        DeviceConfig Config(params int[] restock) => new(1, 1, "Africa/Tunis", zones, [.. restock]);
        double Fill(DateTime at, DeviceConfig config) =>
            device.Tick(config, at, TimeSpan.FromSeconds(2)).Events.Single(e => e.Type == EventType.Fill).Value;

        Assert.InRange(Fill(now, Config()), 10, 14);
        Assert.InRange(Fill(now.AddSeconds(2), Config(4)), 88, 100);
    }

    [Theory]
    [InlineData(AlertKind.LowFill, 30, new[] { 80.0, 22.0 }, true)]
    [InlineData(AlertKind.LowFill, 30, new[] { 22.0, 80.0 }, false)]
    [InlineData(AlertKind.QueueWait, 240, new[] { 200.0, 330.0 }, true)]
    [InlineData(AlertKind.QueueWait, 240, new[] { 100.0, 330.0 }, false)]
    [InlineData(AlertKind.Unattended, 90, new[] { 120.0 }, true)]
    [InlineData(AlertKind.RestrictedPresence, 0, new double[0], false)]
    public void Alert_rules_trigger_on_recent_measures(AlertKind kind, double threshold, double[] values, bool expected)
    {
        var rule = new AlertRule { Kind = kind, Threshold = threshold };

        Assert.Equal(expected, AlertEvaluator.Evaluate(rule, "Zone", values) is not null);
    }
}
