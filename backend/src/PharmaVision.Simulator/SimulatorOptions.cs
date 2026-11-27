namespace PharmaVision.Simulator;

public class SimulatorOptions
{
    public string ApiBaseUrl { get; set; } = "http://localhost:5080";
    /// <summary>Clés API des boîtiers simulés (valeurs de développement du seed).</summary>
    public string[] ApiKeys { get; set; } = [];
    /// <summary>Multiplie le nombre d'arrivées (démonstration).</summary>
    public double Speed { get; set; } = 1;
    /// <summary>Garde une activité minimale hors horaires d'ouverture pour la démonstration.</summary>
    public bool AlwaysOpen { get; set; } = true;
    public int TickSeconds { get; set; } = 2;
    public int FillIntervalSeconds { get; set; } = 30;
}
