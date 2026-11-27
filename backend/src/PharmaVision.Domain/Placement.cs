namespace PharmaVision.Domain.Placement;

public enum Temperature { Hot, Warm, Cold, Unknown }

public enum FindingKind { LittleTraffic, PassedBy, LookedNotTouched, TouchedNotBought, Untouched }

public enum AdviceKind { Swap, MoveCloser, Visibility, Access, Advise, CheckStock, UseHotSpot }

/// <summary>Mesures d'un rayon sur la période. Les prises en main viennent des caméras, les ventes de la caisse.</summary>
/// <param name="LongestUntouchedMinutes">Plus long intervalle d'ouverture sans aucun produit touché.</param>
/// <param name="FamilyShelves">Nombre de rayons de la même famille : leurs ventes sont partagées.</param>
public record ShelfStats(
    int ZoneId, string Name, int Passes, int Stops, int Pickups, int Units, double LongestUntouchedMinutes,
    double? Fill = null, int FamilyShelves = 1)
{
    public double Engagement => Passes == 0 ? 0 : Math.Min(1, (double)Stops / Passes);
    public double PickupRate => Stops == 0 ? 0 : (double)Pickups / Stops;
    public double Transformation => Pickups == 0 ? 0 : (double)Units / FamilyShelves / Pickups;
}

public record Finding(FindingKind Kind, string Text);

/// <param name="OtherZoneId">Second rayon concerné par un échange ou un rapprochement.</param>
public record Advice(AdviceKind Kind, int ZoneId, int? OtherZoneId, string Title, string Reason, string Action);

public record ShelfDiagnosis(ShelfStats Stats, Temperature Temperature, double Score, List<Finding> Findings);

public record PlacementResult(List<ShelfDiagnosis> Shelves, List<Advice> Advice);

/// <summary>
/// Règles de placement : chaque rayon est comparé à la médiane des rayons de l'officine. Elles proposent
/// des essais à mener, sans promettre de gain.
/// </summary>
public static class PlacementRules
{
    /// <summary>En dessous, les mesures sont trop peu nombreuses pour juger un rayon.</summary>
    public const int MinPasses = 30;
    private const double Low = 0.75;
    private const double LittleTrafficShare = 0.65;
    private const double HotScore = 0.66;
    private const double ColdScore = 0.35;
    private const double LowFillPercent = 30;

    public static PlacementResult Analyze(IReadOnlyList<ShelfStats> shelves, int untouchedLimitMinutes)
    {
        var measured = shelves.Where(s => s.Passes >= MinPasses).ToList();
        var maxPickups = measured.Select(s => (double)s.Pickups).DefaultIfEmpty(0).Max();
        var maxUnits = measured.Select(s => (double)s.Units / s.FamilyShelves).DefaultIfEmpty(0).Max();
        var passes = Median(measured.Select(s => (double)s.Passes));
        var engagement = Median(measured.Select(s => s.Engagement));
        var pickupRate = Median(measured.Where(s => s.Stops > 0).Select(s => s.PickupRate));
        var transformation = Median(measured.Where(s => s.Pickups > 0).Select(s => s.Transformation));

        var diagnoses = shelves.Select(s =>
        {
            var enough = s.Passes >= MinPasses;
            // Chaud ou froid : moitié ce que les clients touchent, moitié ce qui se vend.
            var score = !enough ? 0
                : (Share(s.Pickups, maxPickups) + Share((double)s.Units / s.FamilyShelves, maxUnits)) / 2;
            var temperature = !enough ? Temperature.Unknown
                : score >= HotScore ? Temperature.Hot
                : score < ColdScore ? Temperature.Cold
                : Temperature.Warm;

            var findings = new List<Finding>();
            if (enough)
            {
                if (s.Passes < passes * LittleTrafficShare)
                    findings.Add(new(FindingKind.LittleTraffic, "Peu de clients passent devant"));
                else if (s.Engagement < engagement * Low)
                    findings.Add(new(FindingKind.PassedBy, "On passe sans s'arrêter"));
                if (s.Stops > 0 && s.PickupRate < pickupRate * Low)
                    findings.Add(new(FindingKind.LookedNotTouched, "On regarde sans toucher"));
                if (s.Pickups > 0 && s.Transformation < transformation * Low)
                    findings.Add(new(FindingKind.TouchedNotBought, "On touche sans acheter"));
            }
            if (s.LongestUntouchedMinutes >= untouchedLimitMinutes)
                findings.Add(new(FindingKind.Untouched, $"Aucun produit touché pendant {Span(s.LongestUntouchedMinutes)}"));
            return new ShelfDiagnosis(s, temperature, Math.Round(score, 2), findings);
        }).OrderByDescending(d => d.Score).ThenBy(d => d.Stats.ZoneId).ToList();

        return new(diagnoses, Recommend(diagnoses, pickupRate, maxPickups));
    }

    private static List<Advice> Recommend(List<ShelfDiagnosis> diagnoses, double medianPickupRate, double maxPickups)
    {
        var advice = new List<Advice>();
        bool Has(ShelfDiagnosis d, FindingKind kind) => d.Findings.Any(f => f.Kind == kind);
        var measured = diagnoses.Where(d => d.Temperature != Temperature.Unknown).ToList();
        var moved = new HashSet<int>();

        // 1. Un rayon qui intéresse mais mal placé échange sa place avec un rayon très fréquenté qui retient peu.
        var hidden = measured.Where(d => Has(d, FindingKind.LittleTraffic))
            .OrderByDescending(d => d.Stats.PickupRate).FirstOrDefault();
        if (hidden is not null)
        {
            var wasted = measured
                .Where(d => d.Stats.ZoneId != hidden.Stats.ZoneId && d.Stats.Passes > hidden.Stats.Passes
                            && (Has(d, FindingKind.PassedBy) || Has(d, FindingKind.LookedNotTouched))
                            && d.Stats.PickupRate < hidden.Stats.PickupRate)
                .OrderByDescending(d => d.Stats.Passes).FirstOrDefault();
            var busiest = measured.OrderByDescending(d => d.Stats.Passes).First();

            if (wasted is not null)
            {
                advice.Add(new(AdviceKind.Swap, hidden.Stats.ZoneId, wasted.Stats.ZoneId,
                    $"Échangez les places de « {hidden.Stats.Name} » et « {wasted.Stats.Name} »",
                    $"« {wasted.Stats.Name} » voit passer {Count(wasted.Stats.Passes)} clients mais seuls {Percent(wasted.Stats.PickupRate)} "
                    + $"de ceux qui s'arrêtent touchent un produit. « {hidden.Stats.Name} » n'en voit que {Count(hidden.Stats.Passes)}, "
                    + $"et {Percent(hidden.Stats.PickupRate)} y touchent un produit.",
                    "Faites l'échange pendant deux semaines, puis comparez les ventes des deux rayons."));
                moved.Add(wasted.Stats.ZoneId);
            }
            else if (busiest.Stats.ZoneId != hidden.Stats.ZoneId)
            {
                advice.Add(new(AdviceKind.MoveCloser, hidden.Stats.ZoneId, busiest.Stats.ZoneId,
                    $"Rapprochez « {hidden.Stats.Name} » du passage",
                    $"Seuls {Count(hidden.Stats.Passes)} clients passent devant, contre {Count(busiest.Stats.Passes)} devant « {busiest.Stats.Name} ».",
                    $"Placez une partie de ses produits près de « {busiest.Stats.Name} » et suivez les ventes."));
            }
            moved.Add(hidden.Stats.ZoneId);
        }

        // 2. Conseils sur place, pour les rayons qui ne sont pas déjà déplacés.
        foreach (var d in measured.OrderByDescending(d => d.Stats.Passes).Where(d => !moved.Contains(d.Stats.ZoneId)))
        {
            var s = d.Stats;
            if (s.Fill < LowFillPercent)
                advice.Add(d.Temperature == Temperature.Hot
                    ? new(AdviceKind.CheckStock, s.ZoneId, null, $"Remplissez « {s.Name} » sans attendre",
                        $"C'est un rayon chaud et il n'est rempli qu'à {s.Fill:0} % : des ventes risquent d'être perdues.",
                        "Réassortissez maintenant et prévoyez plus de stock aux heures chargées.")
                    : new(AdviceKind.CheckStock, s.ZoneId, null, $"Remplissez « {s.Name} » avant de le juger",
                        $"Le rayon n'est rempli qu'à {s.Fill:0} % : un rayon vide ne peut pas vendre.",
                        "Réassortissez, puis observez une semaine."));
            else if (Has(d, FindingKind.PassedBy))
                advice.Add(new(AdviceKind.Visibility, s.ZoneId, null, $"Rendez « {s.Name} » plus visible",
                    $"{Count(s.Passes)} clients passent devant, mais seuls {Percent(s.Engagement)} s'arrêtent.",
                    "Mettez les meilleures ventes à hauteur des yeux, ajoutez un éclairage ou une affiche."));
            else if (Has(d, FindingKind.LookedNotTouched))
                advice.Add(new(AdviceKind.Access, s.ZoneId, null, $"Donnez envie de prendre en main à « {s.Name} »",
                    $"{Count(s.Stops)} arrêts, mais un produit n'est touché que dans {Percent(s.PickupRate)} des cas "
                    + $"(les autres rayons : {Percent(medianPickupRate)}).",
                    "Affichez les prix, laissez les produits à portée de main, proposez un testeur."));
            else if (Has(d, FindingKind.TouchedNotBought))
                advice.Add(new(AdviceKind.Advise, s.ZoneId, null, $"Accompagnez les clients devant « {s.Name} »",
                    $"{Count(s.Pickups)} prises en main pour {Count(s.Units / s.FamilyShelves)} unités vendues.",
                    "Proposez un conseil au bon moment et vérifiez le prix par rapport au marché."));
            else if (d.Temperature == Temperature.Cold)
                advice.Add(new(AdviceKind.Access, s.ZoneId, null, $"Essayez une autre présentation pour « {s.Name} »",
                    $"Rayon froid : {Count(s.Pickups)} prises en main et {Count(s.Units / s.FamilyShelves)} unités vendues, "
                    + $"contre {Count((int)maxPickups)} prises en main pour le rayon le plus touché.",
                    "Changez les produits placés à hauteur des yeux pendant deux semaines, puis comparez."));
        }

        // 3. Exploiter le meilleur emplacement.
        var hot = measured.FirstOrDefault(d => d.Temperature == Temperature.Hot && !moved.Contains(d.Stats.ZoneId)
            && !(d.Stats.Fill < LowFillPercent));
        var cold = measured.LastOrDefault(d => d.Temperature == Temperature.Cold);
        if (hot is not null)
            advice.Add(new(AdviceKind.UseHotSpot, hot.Stats.ZoneId, cold?.Stats.ZoneId,
                $"Profitez de « {hot.Stats.Name} », votre rayon le plus chaud",
                $"{Count(hot.Stats.Pickups)} prises en main et {Count(hot.Stats.Units / hot.Stats.FamilyShelves)} unités vendues sur la période.",
                cold is null ? "Placez-y les nouveautés et les promotions."
                    : $"Placez juste à côté une sélection de « {cold.Stats.Name} », aujourd'hui froid."));

        return advice;
    }

    /// <summary>
    /// Plus long intervalle sans contact dans des plages d'ouverture : le début et la fin de chaque plage comptent.
    /// </summary>
    public static (TimeSpan Longest, int OverLimit) Untouched(
        IEnumerable<(DateTime From, DateTime To)> openings, IReadOnlyList<DateTime> touches, TimeSpan limit)
    {
        var sorted = touches.Order().ToList();
        var longest = TimeSpan.Zero;
        var over = 0;
        var next = 0;
        foreach (var (from, to) in openings.OrderBy(o => o.From))
        {
            var last = from;
            void Close(DateTime at)
            {
                var gap = at - last;
                if (gap > longest) longest = gap;
                if (gap >= limit) over++;
                last = at;
            }
            while (next < sorted.Count && sorted[next] < from) next++;
            while (next < sorted.Count && sorted[next] < to) Close(sorted[next++]);
            Close(to);
        }
        return (longest, over);
    }

    private static double Share(double value, double max) => max <= 0 ? 0 : value / max;

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToList();
        if (sorted.Count == 0) return 0;
        var middle = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
    }

    private static string Count(int value) => value.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"));

    private static string Percent(double ratio) => $"{Math.Round(ratio * 100)} %";

    private static string Span(double minutes) =>
        minutes < 60 ? $"{minutes:0} min" : $"{(int)minutes / 60} h {(int)minutes % 60:00}";
}
