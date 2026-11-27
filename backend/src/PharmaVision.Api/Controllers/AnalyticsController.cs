using Microsoft.AspNetCore.Mvc;
using PharmaVision.Api.Services;

namespace PharmaVision.Api.Controllers;

/// <summary>Analyse détaillée par zone et par produit ; period = live | today | 7d | 30d.</summary>
[ApiController]
[Route("api/analytics")]
public class AnalyticsController(AnalyticsService analytics) : ControllerBase
{
    [HttpGet("zones")]
    public async Task<ActionResult<List<ZoneRow>>> Zones(int pharmacyId, string? period, CancellationToken ct) =>
        await analytics.ZonesAsync(pharmacyId, PeriodRange.Parse(period), ct) is { } result ? result : NotFound();

    /// <summary>La zone doit appartenir à l'officine demandée, dont l'accès est vérifié par le filtre.</summary>
    [HttpGet("zones/{id:int}")]
    public async Task<ActionResult<ZoneDetail>> Zone(int id, int pharmacyId, string? period, CancellationToken ct) =>
        await analytics.ZoneAsync(pharmacyId, id, PeriodRange.Parse(period), ct) is { } result ? result : NotFound();

    [HttpGet("products")]
    public async Task<ActionResult<ProductReport>> Products(int pharmacyId, string? period, CancellationToken ct) =>
        await analytics.ProductsAsync(pharmacyId, PeriodRange.Parse(period), ct) is { } result ? result : NotFound();
}
