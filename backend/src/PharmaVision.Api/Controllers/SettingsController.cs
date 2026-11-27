using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmaVision.Api.Security;
using PharmaVision.Domain;
using PharmaVision.Infrastructure;

namespace PharmaVision.Api.Controllers;

public record PharmacySettings(
    [Required, StringLength(80)] string Name,
    [Required, StringLength(60)] string City,
    [Range(1, 365, ErrorMessage = "Conservation des images : entre 1 et 365 jours")] int ThumbnailDays,
    // Au moins 3 mois : les tendances sur 30 jours ont besoin de la période précédente.
    [Range(3, 120, ErrorMessage = "Conservation des statistiques : entre 3 et 120 mois")] int StatisticsMonths);

public record DeviceDto(int Id, string Name, string Version, DateTime? LastSeen, int Cameras);

public record NewDevice(int PharmacyId, [Required, StringLength(80)] string Name);

/// <summary>La clé n'est lisible que dans cette réponse.</summary>
public record DeviceKey(int DeviceId, string Key);

[ApiController]
[Authorize(Roles = Roles.Titulaire)]
public class SettingsController(PharmaDbContext db) : ControllerBase
{
    [HttpPut("api/pharmacies/{id:int}")]
    public async Task<IActionResult> Update(int id, PharmacySettings input, CancellationToken ct)
    {
        if (!User.CanAccess(id)) return NotFound();
        var pharmacy = await db.Pharmacies.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (pharmacy is null) return NotFound();
        (pharmacy.Name, pharmacy.City) = (input.Name.Trim(), input.City.Trim());
        (pharmacy.ThumbnailDays, pharmacy.StatisticsMonths) = (input.ThumbnailDays, input.StatisticsMonths);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("api/devices")]
    public Task<List<DeviceDto>> Devices(int pharmacyId, CancellationToken ct) =>
        db.Devices.Where(d => d.PharmacyId == pharmacyId).OrderBy(d => d.Id)
            .Select(d => new DeviceDto(d.Id, d.Name, d.Version, d.LastSeen, d.Cameras.Count))
            .ToListAsync(ct);

    [HttpPost("api/devices")]
    public async Task<ActionResult<DeviceKey>> AddDevice(NewDevice input, CancellationToken ct)
    {
        if (!User.CanAccess(input.PharmacyId)) return Forbid();
        var key = DeviceKeys.Create();
        var device = new EdgeDevice
        {
            PharmacyId = input.PharmacyId, Name = input.Name.Trim(), Version = "—", ApiKeyHash = DeviceKeys.Hash(key),
        };
        db.Devices.Add(device);
        await db.SaveChangesAsync(ct);
        return new DeviceKey(device.Id, key);
    }

    /// <summary>Remplace la clé : le boîtier qui utilise l'ancienne est aussitôt refusé.</summary>
    [HttpPost("api/devices/{id:int}/key")]
    public async Task<ActionResult<DeviceKey>> RenewKey(int id, CancellationToken ct)
    {
        if (await FindAsync(id, ct) is not { } device) return NotFound();
        var key = DeviceKeys.Create();
        device.ApiKeyHash = DeviceKeys.Hash(key);
        await db.SaveChangesAsync(ct);
        return new DeviceKey(device.Id, key);
    }

    [HttpDelete("api/devices/{id:int}")]
    public async Task<IActionResult> DeleteDevice(int id, CancellationToken ct)
    {
        if (await FindAsync(id, ct) is not { } device) return NotFound();
        if (await db.Cameras.AnyAsync(c => c.DeviceId == id, ct))
            return Conflict("Ce boîtier a encore des caméras : déplacez-les ou supprimez-les d'abord");
        db.Devices.Remove(device);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<EdgeDevice?> FindAsync(int id, CancellationToken ct)
    {
        var device = await db.Devices.FirstOrDefaultAsync(d => d.Id == id, ct);
        return device is not null && User.CanAccess(device.PharmacyId) ? device : null;
    }
}
