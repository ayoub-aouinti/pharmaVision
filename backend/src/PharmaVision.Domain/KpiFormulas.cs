namespace PharmaVision.Domain;

public enum ShelfState { Conforme, StockBas, Rupture, Inconnu }

/// <summary>Formules du cahier de conception (section « Formules des indicateurs »).</summary>
public static class KpiFormulas
{
    public const double RuptureThreshold = 30;
    public const double LowStockThreshold = 60;
    public const double MinStopSeconds = 5;

    private static double Ratio(double num, double den) => den <= 0 ? 0 : num / den;

    /// <summary>Tickets de caisse ÷ visiteurs entrés.</summary>
    public static double ConversionRate(int tickets, int visitors) => Ratio(tickets, visitors);

    /// <summary>Personnes arrêtées plus de 5 s ÷ personnes passées devant (plafonné à 100 %).</summary>
    public static double EngagementRate(int stops, int passes) => Math.Min(1, Ratio(stops, passes));

    /// <summary>Interactions main-produit ÷ arrêts.</summary>
    public static double PickupRate(int pickups, int stops) => Ratio(pickups, stops);

    /// <summary>Ventes de la famille ÷ prises en main de la zone.</summary>
    public static double ShelfTransformation(int unitsSold, int pickups) => Ratio(unitsSold, pickups);

    public static double AverageWaitSeconds(IReadOnlyCollection<double> waits) => waits.Count == 0 ? 0 : waits.Average();

    public static ShelfState StateOf(double? fill, double ruptureThreshold = RuptureThreshold) => fill switch
    {
        null => ShelfState.Inconnu,
        var f when f < ruptureThreshold => ShelfState.Rupture,
        var f when f < LowStockThreshold => ShelfState.StockBas,
        _ => ShelfState.Conforme,
    };

    /// <summary>
    /// Temps cumulé où le remplissage reste sous le seuil. Chaque mesure vaut jusqu'à la suivante,
    /// dans la limite de <paramref name="maxGap"/> (évite de compter la nuit ou une coupure).
    /// </summary>
    public static TimeSpan RuptureDuration(
        IEnumerable<(DateTime Ts, double Fill)> samples, double threshold, DateTime until, TimeSpan maxGap)
    {
        var ordered = samples.OrderBy(s => s.Ts).ToList();
        var total = TimeSpan.Zero;
        for (var i = 0; i < ordered.Count; i++)
        {
            if (ordered[i].Fill >= threshold) continue;
            var next = i + 1 < ordered.Count ? ordered[i + 1].Ts : until;
            var span = next - ordered[i].Ts;
            if (span > maxGap) span = maxGap;
            if (span > TimeSpan.Zero) total += span;
        }
        return total;
    }

    /// <summary>Variation relative vs période précédente ; null si la référence est vide.</summary>
    public static double? Trend(double current, double previous) =>
        previous == 0 ? null : (current - previous) / previous;
}
