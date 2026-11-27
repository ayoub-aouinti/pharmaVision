namespace PharmaVision.Domain.Simulation;

/// <summary>Profil de fréquentation fictif d'une officine (heure locale).</summary>
public static class TrafficProfile
{
    public const int OpeningHour = 8;
    public const int ClosingHour = 20;

    // 8h → 19h : pics 10h–12h et 17h–19h.
    private static readonly double[] HourWeights = [5, 8, 12, 12, 9, 6, 6, 7, 9, 12, 11, 6];

    public static bool IsOpen(DateTime local) => local.Hour >= OpeningHour && local.Hour < ClosingHour;

    public static double DayFactor(DayOfWeek day) => day switch
    {
        DayOfWeek.Friday => 0.85,
        DayOfWeek.Saturday => 1.10,
        DayOfWeek.Sunday => 0.70,
        DayOfWeek.Monday => 1.08,
        _ => 1.0,
    };

    /// <summary>Visiteurs attendus pendant l'heure locale donnée.</summary>
    public static double ExpectedVisitors(DateTime local, double baseDaily)
    {
        if (!IsOpen(local)) return 0;
        return baseDaily * DayFactor(local.DayOfWeek) * HourWeights[local.Hour - OpeningHour] / HourWeights.Sum();
    }
}
