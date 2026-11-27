using PharmaVision.Domain;

namespace PharmaVision.Infrastructure.Seeding;

/// <summary>Configuration fictive : 3 officines d'un même groupement.</summary>
public static class ReferenceData
{
    /// <summary>Clé API de développement du boîtier de la n-ième officine (n à partir de 1).</summary>
    public static string DevApiKey(int n) => $"dev-key-{n}";

    public static readonly (string Name, string City, double DailyVisitors)[] Sites =
    [
        ("Pharmacie El Amal", "Tunis", 390),
        ("Pharmacie Ennour", "Sfax", 320),
        ("Pharmacie du Port", "Sousse", 270),
    ];

    public static List<Pharmacy> Build() => Sites.Select((s, i) => new Pharmacy
    {
        Name = s.Name,
        City = s.City,
        Group = "Groupement Santé Plus (fictif)",
        Devices =
        [
            new EdgeDevice
            {
                Name = $"Boîtier {s.City}",
                Version = "0.1.0-sim",
                ApiKey = DevApiKey(i + 1),
                Cameras = Cameras(i + 1),
            },
        ],
    }).ToList();

    private static List<Camera> Cameras(int site) =>
    [
        Cam(site, 1, "Entrée", "Hikvision", "entrance", CameraStatus.Online,
        [
            NewZone("Entrée", ZoneType.CountingLine, null, null, Line(0.22, 0.80, 0.78, 0.80), (0, 6, 4, 2)),
            NewZone("Allée centrale", ZoneType.Passage, null, null, Rect(0.30, 0.42, 0.40, 0.30), (4, 3, 4, 5)),
        ]),
        Cam(site, 2, "Comptoir", "Dahua", "counter", CameraStatus.Online,
        [
            NewZone("Comptoir 1", ZoneType.Queue, null, null, Rect(0.08, 0.50, 0.38, 0.40), (8, 6, 2, 2)),
            NewZone("Comptoir 2", ZoneType.Queue, null, null, Rect(0.54, 0.50, 0.38, 0.40), (10, 6, 2, 2)),
            NewZone("Masque ordonnances", ZoneType.PrivacyMask, null, null, Rect(0.30, 0.30, 0.40, 0.14), null),
        ]),
        Cam(site, 3, "Dermo & Bébé", "Axis", "shelves", CameraStatus.Online,
        [
            NewZone("Dermo-cosmétique", ZoneType.Shelf, "Dermo-cosmétique", "Avène, La Roche-Posay, Bioderma", Rect(0.05, 0.18, 0.42, 0.60), (0, 0, 4, 3)),
            NewZone("Bébé", ZoneType.Shelf, "Bébé", "Mustela, Chicco, Uriage", Rect(0.53, 0.18, 0.42, 0.60), (4, 0, 4, 3)),
        ]),
        Cam(site, 4, "Rayons latéraux", "Hikvision", "aisle", CameraStatus.Online,
        [
            NewZone("Solaires", ZoneType.Shelf, "Solaires", "SVR, Avène, Vichy", Rect(0.03, 0.22, 0.29, 0.56), (8, 0, 4, 3)),
            NewZone("Orthopédie", ZoneType.Shelf, "Orthopédie", "Orliman, Thuasne", Rect(0.36, 0.22, 0.28, 0.56), (0, 3, 4, 3)),
            NewZone("Médicaments conseil", ZoneType.Shelf, "Médicaments conseil", "Doliprane, Humex, Gaviscon", Rect(0.68, 0.22, 0.29, 0.56), (8, 3, 2, 3)),
        ]),
        // La caméra de réserve de la première officine est volontairement hors ligne.
        Cam(site, 5, "Réserve", "Dahua", "storage", site == 1 ? CameraStatus.Offline : CameraStatus.Online,
        [
            NewZone("Stupéfiants", ZoneType.Restricted, null, null, Rect(0.62, 0.30, 0.28, 0.45), (10, 3, 2, 3)),
        ]),
        Cam(site, 6, "Parking", "Axis", "generic", CameraStatus.Unconfigured, []),
    ];

    private static Camera Cam(int site, int n, string name, string brand, string scene, CameraStatus status, List<Zone> zones) => new()
    {
        Name = $"Cam {n} · {name}",
        Brand = brand,
        Scene = scene,
        Status = status,
        RtspUrl = $"rtsp://192.168.{site}.{100 + n}:554/stream1",
        ReferencePhotoAt = status == CameraStatus.Unconfigured ? null : DateTime.UtcNow.AddDays(-30),
        Zones = zones,
    };

    private static Zone NewZone(string name, ZoneType type, string? category, string? brands, List<ZonePoint> polygon,
        (int X, int Y, int W, int H)? plan) => new()
    {
        Name = name,
        Type = type,
        Category = category,
        Brands = brands,
        Polygon = polygon,
        PlanX = plan?.X, PlanY = plan?.Y, PlanW = plan?.W, PlanH = plan?.H,
        PlanogramRef = type == ZoneType.Shelf ? $"PLN-{name[..3].ToUpperInvariant()}-2026" : null,
        FacingsExpected = type == ZoneType.Shelf ? 48 : null,
        Rules = DefaultRules(type, category),
    };

    public static List<AlertRule> DefaultRules(ZoneType type, string? category)
    {
        var rules = new List<AlertRule>();
        if (type == ZoneType.Shelf)
        {
            rules.Add(new AlertRule { Kind = AlertKind.LowFill, Threshold = KpiFormulas.RuptureThreshold, Channel = NotificationChannel.Mobile });
            if (category is "Dermo-cosmétique" or "Bébé")
                rules.Add(new AlertRule { Kind = AlertKind.Unattended, Threshold = 90, DurationSec = 180, Channel = NotificationChannel.Mobile });
        }
        if (type == ZoneType.Queue)
            rules.Add(new AlertRule { Kind = AlertKind.QueueWait, Threshold = 240, DurationSec = 300, Channel = NotificationChannel.Dashboard });
        if (type == ZoneType.Restricted)
            rules.Add(new AlertRule { Kind = AlertKind.RestrictedPresence, DurationSec = 120, Channel = NotificationChannel.Mobile });
        return rules;
    }

    private static List<ZonePoint> Rect(double x, double y, double w, double h) =>
        [new(x, y), new(x + w, y), new(x + w, y + h), new(x, y + h)];

    private static List<ZonePoint> Line(double x1, double y1, double x2, double y2) => [new(x1, y1), new(x2, y2)];
}
