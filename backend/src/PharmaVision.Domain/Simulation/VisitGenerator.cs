namespace PharmaVision.Domain.Simulation;

public record SimZone(int Id, ZoneType Type, string? Category);

public record Visit(List<IngestEvent> Events, List<IngestSale> Sales);

/// <summary>
/// Génère le parcours anonyme d'un visiteur fictif. Déterministe pour une graine donnée :
/// utilisé à la fois par le seed historique et par le boîtier simulé.
/// </summary>
public class VisitGenerator(Random rng)
{
    private const double ConversionProbability = 0.62;

    private readonly Dictionary<int, double> _fill = [];
    private readonly Dictionary<int, DateTime> _restockAt = [];
    private long _sequence;

    private record Appeal(double Pass, double Stop, double Pickup, decimal Price);

    private static Appeal AppealOf(string? category) => category switch
    {
        "Dermo-cosmétique" => new(0.55, 0.42, 0.48, 38m),
        "Bébé" => new(0.42, 0.38, 0.45, 27m),
        "Solaires" => new(0.35, 0.24, 0.40, 45m),
        "Médicaments conseil" => new(0.60, 0.20, 0.30, 12m),
        "Orthopédie" => new(0.20, 0.14, 0.25, 55m),
        _ => new(0.30, 0.18, 0.30, 20m),
    };

    /// <summary>Fixe le niveau d'un linéaire (état initial ou réassort par l'équipe).</summary>
    public void SetFill(int zoneId, double value)
    {
        _fill[zoneId] = Math.Clamp(value, 0, 100);
        _restockAt.Remove(zoneId);
    }

    public double Fill(int zoneId)
    {
        if (!_fill.TryGetValue(zoneId, out var v)) _fill[zoneId] = v = Between(65, 98);
        return v;
    }

    /// <summary>Mesure de remplissage d'un linéaire, avec réassort différé quand le niveau baisse.</summary>
    public IngestEvent SampleFill(int zoneId, DateTime ts)
    {
        var level = Fill(zoneId);
        if (_restockAt.TryGetValue(zoneId, out var at) && ts >= at)
        {
            SetFill(zoneId, Between(88, 100));
            level = _fill[zoneId];
        }
        else if (level < 45 && !_restockAt.ContainsKey(zoneId))
        {
            _restockAt[zoneId] = ts.AddMinutes(Between(45, 300));
        }
        var measured = Math.Clamp(level + Between(-1.5, 1.5), 0, 100);
        return new IngestEvent(ts, zoneId, EventType.Fill, Math.Round(measured, 1), null);
    }

    /// <param name="load">Facteur d'affluence appliqué au temps d'attente.</param>
    public Visit Generate(DateTime arrivalUtc, IReadOnlyList<SimZone> zones, double load = 1)
    {
        var events = new List<IngestEvent>();
        var sales = new List<IngestSale>();
        var track = $"t{arrivalUtc:HHmmss}-{++_sequence:x}-{rng.Next(0x1000, 0xffff):x}";
        var t = arrivalUtc;

        void Add(int zone, EventType type, double value) =>
            events.Add(new IngestEvent(t, zone, type, Math.Round(value, 1), track));
        void Advance(double seconds) => t = t.AddSeconds(seconds);

        var door = zones.FirstOrDefault(z => z.Type == ZoneType.CountingLine);
        if (door is not null) Add(door.Id, EventType.Entry, 1);

        foreach (var passage in zones.Where(z => z.Type == ZoneType.Passage))
        {
            if (rng.NextDouble() > 0.7) continue;
            Advance(Between(3, 15));
            Add(passage.Id, EventType.Pass, 1);
            if (rng.NextDouble() < 0.25) Add(passage.Id, EventType.Stop, Between(6, 30));
        }

        var buys = rng.NextDouble() < ConversionProbability;
        var interested = new List<(string Family, decimal Price)>();

        foreach (var shelf in zones.Where(z => z.Type == ZoneType.Shelf).OrderBy(_ => rng.Next()).ToList())
        {
            var appeal = AppealOf(shelf.Category);
            if (rng.NextDouble() > appeal.Pass) continue;
            Advance(Between(4, 25));
            Add(shelf.Id, EventType.Pass, 1);
            if (rng.NextDouble() > appeal.Stop) continue;

            var dwell = Between(6, 75);
            Add(shelf.Id, EventType.Stop, dwell);
            Advance(dwell);

            // Client longtemps seul devant un rayon conseil (dermo, bébé).
            if (shelf.Category is "Dermo-cosmétique" or "Bébé" && rng.NextDouble() < 0.035)
            {
                var alone = Between(95, 200);
                Advance(alone - dwell);
                Add(shelf.Id, EventType.Unattended, alone);
            }

            if (rng.NextDouble() >= appeal.Pickup || Fill(shelf.Id) <= 3) continue;

            Add(shelf.Id, EventType.Pickup, 1);
            var taken = buys && rng.NextDouble() < 0.7;
            if (taken) interested.Add((shelf.Category ?? "Divers", appeal.Price));
            _fill[shelf.Id] = Math.Max(0, Fill(shelf.Id) - (taken ? Between(1.2, 4.5) : Between(0, 0.6)));
        }

        foreach (var restricted in zones.Where(z => z.Type == ZoneType.Restricted))
        {
            if (rng.NextDouble() < 0.0015) Add(restricted.Id, EventType.Stop, Between(10, 40));
        }

        if (buys)
        {
            var counters = zones.Where(z => z.Type == ZoneType.Queue).ToList();
            if (counters.Count > 0)
            {
                var counter = counters[rng.Next(counters.Count)];
                var wait = Between(25, 170) * load;
                if (rng.NextDouble() < 0.04) wait += Between(120, 240);
                Advance(wait);
                Add(counter.Id, EventType.QueueWait, wait);
            }

            Advance(Between(40, 120));
            var ticket = $"T{arrivalUtc:yyMMdd}-{track[1..]}";
            // Ordonnance ou achat au comptoir, en plus des produits pris en rayon.
            if (interested.Count == 0 || rng.NextDouble() < 0.6) interested.Add(("Ordonnance", 22m));
            foreach (var (family, price) in interested)
            {
                var qty = rng.NextDouble() < 0.15 ? 2 : 1;
                var amount = Math.Round(price * qty * (decimal)Between(0.6, 1.5), 3);
                sales.Add(new IngestSale(t, ticket, $"P{rng.Next(1000, 9999)}", family, qty, amount));
            }
        }

        if (door is not null)
        {
            Advance(Between(20, 90));
            Add(door.Id, EventType.Exit, 1);
        }

        return new Visit(events, sales);
    }

    /// <summary>Tirage de Poisson (nombre d'arrivées).</summary>
    public int Poisson(double mean)
    {
        if (mean <= 0) return 0;
        if (mean > 30)
        {
            var gaussian = Math.Sqrt(-2 * Math.Log(1 - rng.NextDouble())) * Math.Cos(2 * Math.PI * rng.NextDouble());
            return Math.Max(0, (int)Math.Round(mean + Math.Sqrt(mean) * gaussian));
        }
        var limit = Math.Exp(-mean);
        var product = rng.NextDouble();
        var count = 0;
        while (product > limit) { product *= rng.NextDouble(); count++; }
        return count;
    }

    public double Between(double min, double max) => min + rng.NextDouble() * (max - min);
}
