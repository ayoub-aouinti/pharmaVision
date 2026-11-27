namespace PharmaVision.Domain;

// Contrat boîtier ↔ serveur : le futur boîtier Python enverra exactement ce format.

public record IngestEvent(DateTime Ts, int ZoneId, EventType Type, double Value, string? TrackId);

public record IngestSale(DateTime Ts, string TicketNo, string ProductCode, string Family, int Quantity, decimal Amount);

public record IngestBatch(List<IngestEvent> Events, List<IngestSale>? Sales);

public record DeviceZone(int Id, string Name, ZoneType Type, string? Category, double? CurrentFill);

public record DeviceConfig(int DeviceId, int PharmacyId, string TimeZone, List<DeviceZone> Zones, List<int> RestockZoneIds);
