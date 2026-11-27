namespace PharmaVision.Domain;

public enum ZoneType { Shelf, Passage, Queue, CountingLine, Restricted, PrivacyMask }
public enum CameraStatus { Online, Offline, Unconfigured }
public enum EventType { Entry, Exit, Pass, Stop, Pickup, Fill, QueueWait, Unattended }
public enum AlertKind { LowFill, QueueWait, Unattended, RestrictedPresence, CameraOffline }
public enum AlertStatus { Open, Acknowledged, Resolved }
public enum AlertSeverity { Medium, High }
public enum NotificationChannel { Dashboard, Mobile, WhatsApp }

public enum UserRole { Titulaire, Equipe }

public class User
{
    public int Id { get; set; }
    /// <summary>Toujours en minuscules.</summary>
    public required string Email { get; set; }
    public required string DisplayName { get; set; }
    public string PasswordHash { get; set; } = "";
    public UserRole Role { get; set; }
    /// <summary>Officines auxquelles le compte donne accès.</summary>
    public List<int> PharmacyIds { get; set; } = [];
    public bool IsActive { get; set; } = true;
    /// <summary>Augmentée à chaque changement de droits ou de mot de passe : les sessions antérieures tombent.</summary>
    public int SecurityVersion { get; set; }
    public int FailedLogins { get; set; }
    public DateTime? LockedUntil { get; set; }
}

public class Pharmacy
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string City { get; set; }
    public string? Group { get; set; }
    public string TimeZone { get; set; } = "Africa/Tunis";
    /// <summary>Durée pendant laquelle les images d'alerte restent consultables.</summary>
    public int ThumbnailDays { get; set; } = 30;
    /// <summary>Durée de conservation des mesures, ventes et alertes.</summary>
    public int StatisticsMonths { get; set; } = 24;
    public List<EdgeDevice> Devices { get; set; } = [];
}

public class EdgeDevice
{
    public int Id { get; set; }
    public int PharmacyId { get; set; }
    public Pharmacy? Pharmacy { get; set; }
    public required string Name { get; set; }
    public required string Version { get; set; }
    public required string ApiKeyHash { get; set; }
    public DateTime? LastSeen { get; set; }
    public List<Camera> Cameras { get; set; } = [];
}

public class Camera
{
    public int Id { get; set; }
    public int DeviceId { get; set; }
    public EdgeDevice? Device { get; set; }
    public required string Name { get; set; }
    public required string Brand { get; set; }
    public string Protocol { get; set; } = "RTSP / ONVIF";
    public string RtspUrl { get; set; } = "";
    public string Resolution { get; set; } = "2560×1440";
    public CameraStatus Status { get; set; }
    /// <summary>Décor fictif utilisé pour l'image de référence.</summary>
    public string Scene { get; set; } = "generic";
    public DateTime? ReferencePhotoAt { get; set; }
    public List<Zone> Zones { get; set; } = [];
}

/// <summary>Image de référence envoyée par un boîtier, personnes retirées ou floutées.</summary>
public class CameraImage
{
    public int CameraId { get; set; }
    public required byte[] Jpeg { get; set; }
}

/// <summary>Point normalisé (0..1) dans l'image de la caméra.</summary>
public record ZonePoint(double X, double Y);

public class Zone
{
    public int Id { get; set; }
    public int CameraId { get; set; }
    public Camera? Camera { get; set; }
    public required string Name { get; set; }
    public ZoneType Type { get; set; }
    public List<ZonePoint> Polygon { get; set; } = [];
    public string? Category { get; set; }
    public string? Brands { get; set; }
    public string? PlanogramRef { get; set; }
    public int? FacingsExpected { get; set; }
    /// <summary>Position sur le plan de la pharmacie (grille 12 × 8).</summary>
    public int? PlanX { get; set; }
    public int? PlanY { get; set; }
    public int? PlanW { get; set; }
    public int? PlanH { get; set; }
    public List<AlertRule> Rules { get; set; } = [];
}

public class AlertRule
{
    public int Id { get; set; }
    public int ZoneId { get; set; }
    public Zone? Zone { get; set; }
    public AlertKind Kind { get; set; }
    /// <summary>% pour LowFill, secondes pour QueueWait et Unattended.</summary>
    public double Threshold { get; set; }
    public int DurationSec { get; set; }
    public NotificationChannel Channel { get; set; }
    public bool Enabled { get; set; } = true;
}

/// <summary>Événement anonyme horodaté envoyé par le boîtier (hypertable TimescaleDB).</summary>
public class ZoneEvent
{
    public long Id { get; set; }
    public DateTime Ts { get; set; }
    public int ZoneId { get; set; }
    public EventType Type { get; set; }
    public double Value { get; set; }
    public string? TrackId { get; set; }
}

public class Alert
{
    public int Id { get; set; }
    public int PharmacyId { get; set; }
    public int? RuleId { get; set; }
    public int? ZoneId { get; set; }
    public int? CameraId { get; set; }
    public AlertKind Kind { get; set; }
    public AlertSeverity Severity { get; set; }
    public required string Title { get; set; }
    public required string Detail { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public AlertStatus Status { get; set; }
    public string? HandledBy { get; set; }
    public string? ActionTaken { get; set; }
    public DateTime? AcknowledgedAt { get; set; }
    /// <summary>Null pour une clôture automatique (condition disparue).</summary>
    public string? ResolvedBy { get; set; }
}

/// <summary>Référence du catalogue : rattache une ligne de caisse à une marque.</summary>
public class Product
{
    public required string Code { get; set; }
    public required string Name { get; set; }
    /// <summary>Null pour les lignes sans marque (ordonnances).</summary>
    public string? Brand { get; set; }
    public required string Family { get; set; }
    public decimal Price { get; set; }
}

public class Sale
{
    public long Id { get; set; }
    public int PharmacyId { get; set; }
    public DateTime Ts { get; set; }
    public required string TicketNo { get; set; }
    public required string ProductCode { get; set; }
    public required string Family { get; set; }
    public int Quantity { get; set; }
    public decimal Amount { get; set; }
}
