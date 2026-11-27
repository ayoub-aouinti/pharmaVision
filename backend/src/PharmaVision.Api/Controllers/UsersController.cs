using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmaVision.Api.Security;
using PharmaVision.Domain;
using PharmaVision.Infrastructure;

namespace PharmaVision.Api.Controllers;

public record UserDto(
    int Id, string Email, string DisplayName, UserRole Role, List<int> PharmacyIds, bool IsActive, bool Locked);

public record NewUser(
    [Required, EmailAddress, StringLength(120)] string Email,
    [Required, StringLength(80)] string DisplayName,
    [EnumDataType(typeof(UserRole))] UserRole Role,
    [Required, MinLength(1)] List<int> PharmacyIds,
    [Required, StringLength(200, MinimumLength = AccountPolicy.MinPasswordLength,
        ErrorMessage = "Le mot de passe doit compter au moins 10 caractères")] string Password);

public record UserChanges(
    [Required, StringLength(80)] string DisplayName,
    [EnumDataType(typeof(UserRole))] UserRole Role,
    [Required, MinLength(1)] List<int> PharmacyIds,
    bool IsActive);

public record PasswordReset(
    [Required, StringLength(200, MinimumLength = AccountPolicy.MinPasswordLength,
        ErrorMessage = "Le mot de passe doit compter au moins 10 caractères")] string Password);

/// <summary>
/// Gestion des comptes par le titulaire, limitée aux comptes dont toutes les officines sont les siennes.
/// Son propre compte n'est pas modifiable ici : il reste donc toujours au moins un titulaire actif.
/// </summary>
[ApiController]
[Authorize(Roles = Roles.Titulaire)]
[Route("api/users")]
public class UsersController(PharmaDbContext db, TimeProvider clock) : ControllerBase
{
    private static readonly PasswordHasher<User> Hasher = new();

    private int Me => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private bool Manageable(IEnumerable<int> pharmacyIds) => pharmacyIds.All(User.CanAccess);

    private UserDto ToDto(User u) => new(
        u.Id, u.Email, u.DisplayName, u.Role, u.PharmacyIds, u.IsActive, u.LockedUntil > clock.GetUtcNow().UtcDateTime);

    [HttpGet]
    public async Task<List<UserDto>> List(CancellationToken ct)
    {
        // Quelques dizaines de comptes au plus : le filtre sur les officines se fait en mémoire.
        var users = await db.Users.AsNoTracking().OrderBy(u => u.Email).ToListAsync(ct);
        return users.Where(u => Manageable(u.PharmacyIds)).Select(ToDto).ToList();
    }

    [HttpPost]
    public async Task<ActionResult<UserDto>> Create(NewUser input, CancellationToken ct)
    {
        if (!Manageable(input.PharmacyIds)) return Forbid();
        var email = input.Email.Trim().ToLowerInvariant();
        if (await db.Users.AnyAsync(u => u.Email == email, ct)) return Conflict("Un compte utilise déjà cette adresse");

        var user = new User
        {
            Email = email, DisplayName = input.DisplayName.Trim(), Role = input.Role,
            PharmacyIds = input.PharmacyIds.Distinct().Order().ToList(),
        };
        user.PasswordHash = Hasher.HashPassword(user, input.Password);
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return ToDto(user);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<UserDto>> Update(int id, UserChanges input, CancellationToken ct)
    {
        if (await FindAsync(id, ct) is not { } user) return NotFound();
        if (id == Me) return BadRequest("Vous ne pouvez pas modifier les droits de votre propre compte");
        if (!Manageable(input.PharmacyIds)) return Forbid();

        user.DisplayName = input.DisplayName.Trim();
        user.Role = input.Role;
        user.PharmacyIds = input.PharmacyIds.Distinct().Order().ToList();
        user.IsActive = input.IsActive;
        user.SecurityVersion++; // les sessions ouvertes sont fermées
        await db.SaveChangesAsync(ct);
        return ToDto(user);
    }

    /// <summary>Nouveau mot de passe : lève aussi un blocage et ferme les sessions du compte.</summary>
    [HttpPost("{id:int}/password")]
    public async Task<IActionResult> ResetPassword(int id, PasswordReset input, CancellationToken ct)
    {
        if (await FindAsync(id, ct) is not { } user) return NotFound();
        if (id == Me) return BadRequest("Changez votre mot de passe depuis « Mon compte »");

        user.PasswordHash = Hasher.HashPassword(user, input.Password);
        user.FailedLogins = 0;
        user.LockedUntil = null;
        user.SecurityVersion++;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        if (await FindAsync(id, ct) is not { } user) return NotFound();
        if (id == Me) return BadRequest("Vous ne pouvez pas supprimer votre propre compte");

        db.Users.Remove(user);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Null aussi pour un compte hors du périmètre : son existence n'est pas révélée.</summary>
    private async Task<User?> FindAsync(int id, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        return user is not null && Manageable(user.PharmacyIds) ? user : null;
    }
}
