using PharmaVision.Domain;
using PharmaVision.Domain.Simulation;

namespace PharmaVision.Simulator;

/// <summary>
/// État d'un boîtier fictif : planifie des parcours de visiteurs et restitue, à chaque pas,
/// les événements arrivés à échéance.
/// </summary>
public class DeviceSimulator(Random rng, double dailyVisitors, SimulatorOptions options)
{
    private readonly VisitGenerator _generator = new(rng);
    private readonly PriorityQueue<IngestEvent, DateTime> _events = new();
    private readonly PriorityQueue<IngestSale, DateTime> _sales = new();
    private readonly HashSet<int> _initialised = [];
    private readonly HashSet<int> _restocked = [];
    private DateTime _nextFill = DateTime.MinValue;

    public IngestBatch Tick(DeviceConfig config, DateTime nowUtc, TimeSpan elapsed)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById(config.TimeZone);
        var local = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, tz);
        var zones = config.Zones.Select(z => new SimZone(z.Id, z.Type, z.Category)).ToList();
        var shelves = config.Zones.Where(z => z.Type == ZoneType.Shelf).ToList();

        foreach (var shelf in shelves)
        {
            if (_initialised.Add(shelf.Id) && shelf.CurrentFill is { } fill) _generator.SetFill(shelf.Id, fill);
        }

        // Réassort demandé depuis le tableau de bord (une seule fois par alerte).
        _restocked.IntersectWith(config.RestockZoneIds);
        foreach (var id in config.RestockZoneIds.Where(id => _restocked.Add(id)))
        {
            _generator.SetFill(id, _generator.Between(90, 100));
            _nextFill = nowUtc;
        }

        var perHour = TrafficProfile.ExpectedVisitors(local, dailyVisitors);
        if (perHour == 0 && options.AlwaysOpen) perHour = dailyVisitors / 20;
        var arrivals = _generator.Poisson(perHour * options.Speed * elapsed.TotalHours);
        var load = Math.Clamp(perHour / (dailyVisitors / 12), 0.6, 1.4);
        for (var i = 0; i < arrivals && zones.Count > 0; i++)
        {
            var visit = _generator.Generate(nowUtc, zones, load);
            visit.Events.ForEach(e => _events.Enqueue(e, e.Ts));
            visit.Sales.ForEach(s => _sales.Enqueue(s, s.Ts));
        }

        var due = new List<IngestEvent>();
        if (nowUtc >= _nextFill)
        {
            due.AddRange(shelves.Select(s => _generator.SampleFill(s.Id, nowUtc)));
            _nextFill = nowUtc.AddSeconds(options.FillIntervalSeconds);
        }
        while (_events.TryPeek(out _, out var at) && at <= nowUtc) due.Add(_events.Dequeue());
        var sales = new List<IngestSale>();
        while (_sales.TryPeek(out _, out var at) && at <= nowUtc) sales.Add(_sales.Dequeue());

        return new IngestBatch(due, sales);
    }
}
