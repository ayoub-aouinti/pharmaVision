using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using PharmaVision.Api.Security;
using PharmaVision.Domain;
using PharmaVision.Infrastructure;

namespace PharmaVision.Api.Controllers;

public record LoginRequest([Required, StringLength(120)] string Email, [Required, StringLength(200)] string Password);

public record CurrentUser(int Id, string Name, string Email, UserRole Role);

public record PasswordChange(
    [Required, StringLength(200)] string CurrentPassword,
    [Required, StringLength(200, MinimumLength = AccountPolicy.MinPasswordLength,
        ErrorMessage = "Le mot de passe doit compter au moins 10 caractères")] string NewPassword);

[ApiController]
[Route("api/auth")]
public class AuthController(PharmaDbContext db, TimeProvider clock) : ControllerBase
{
    private static readonly PasswordHasher<User> Hasher = new();

    // Vérifié quand l'adresse est inconnue, pour que la durée de réponse ne révèle pas les comptes existants.
    private static readonly string DummyHash = Hasher.HashPassword(null!, Guid.NewGuid().ToString());

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<CurrentUser>> Login(LoginRequest request, CancellationToken ct)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
        var result = Hasher.VerifyHashedPassword(user!, user?.PasswordHash ?? DummyHash, request.Password);
        var now = clock.GetUtcNow().UtcDateTime;
        var locked = user?.LockedUntil > now;

        if (user is not null && !locked && result == PasswordVerificationResult.Failed)
        {
            if (++user.FailedLogins >= AccountPolicy.MaxFailedLogins)
            {
                user.LockedUntil = now + AccountPolicy.LockDuration;
                user.FailedLogins = 0;
            }
            await db.SaveChangesAsync(ct);
        }
        // Un seul message : il ne dit pas si le compte existe, est désactivé ou bloqué.
        if (user is null || locked || !user.IsActive || result == PasswordVerificationResult.Failed)
            return Unauthorized("Adresse ou mot de passe incorrect, ou compte temporairement bloqué après plusieurs échecs");

        user.FailedLogins = 0;
        user.LockedUntil = null;
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
            user.PasswordHash = Hasher.HashPassword(user, request.Password);
        await db.SaveChangesAsync(ct);

        await SignInAsync(user);
        return ToCurrent(user);
    }

    /// <summary>Change son mot de passe ; les autres sessions du compte sont fermées.</summary>
    [HttpPost("password")]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> ChangePassword(PasswordChange request, CancellationToken ct)
    {
        if (await SessionUserAsync(ct) is not { } user) return Unauthorized();
        if (Hasher.VerifyHashedPassword(user, user.PasswordHash, request.CurrentPassword) == PasswordVerificationResult.Failed)
            return BadRequest("Mot de passe actuel incorrect");

        user.PasswordHash = Hasher.HashPassword(user, request.NewPassword);
        user.SecurityVersion++;
        await db.SaveChangesAsync(ct);
        await SignInAsync(user);
        return NoContent();
    }

    private Task SignInAsync(User user)
    {
        const string scheme = CookieAuthenticationDefaults.AuthenticationScheme;
        return HttpContext.SignInAsync(scheme, Access.ToPrincipal(user, scheme));
    }

    private Task<User?> SessionUserAsync(CancellationToken ct)
    {
        var id = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        return db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
    }

    private static CurrentUser ToCurrent(User user) => new(user.Id, user.DisplayName, user.Email, user.Role);

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
    }

    [HttpGet("me")]
    public async Task<ActionResult<CurrentUser>> Me(CancellationToken ct) =>
        await SessionUserAsync(ct) is { } user ? ToCurrent(user) : Unauthorized();
}
