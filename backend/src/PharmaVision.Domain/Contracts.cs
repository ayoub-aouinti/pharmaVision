namespace PharmaVision.Domain;

// Contrat boîtier ↔ serveur : le futur boîtier Python enverra exactement ce format.

public record IngestEvent(DateTime Ts, int ZoneId, EventType Type, double Value, string? TrackId);

public record IngestSale(DateTime Ts, string TicketNo, string ProductCode, string Family, int Quantity, decimal Amount);

public record IngestBatch(List<IngestEvent> Events, List<IngestSale>? Sales);

/// <param name="Polygon">Points normalisés (0..1) dans l'image de la caméra.</param>
public record DeviceZone(
    int Id, string Name, ZoneType Type, string? Category, double? CurrentFill,
    int CameraId = 0, List<ZonePoint>? Polygon = null);

/// <param name="Masks">Parties de l'image à ne jamais analyser ni transmettre.</param>
/// <param name="PhotoAt">Date de l'image de référence demandée ; un changement en réclame une nouvelle.</param>
public record DeviceCamera(int Id, string Name, string RtspUrl, List<List<ZonePoint>> Masks, DateTime? PhotoAt);

public record DeviceConfig(
    int DeviceId, int PharmacyId, string TimeZone, List<DeviceZone> Zones, List<int> RestockZoneIds,
    List<DeviceCamera>? Cameras = null);

public record ImageReceived(DateTime PhotoAt);
