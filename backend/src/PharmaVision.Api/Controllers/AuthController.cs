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

public record CurrentUser(string Name, string Email, UserRole Role);

[ApiController]
[Route("api/auth")]
public class AuthController(PharmaDbContext db) : ControllerBase
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
        if (user is null || result == PasswordVerificationResult.Failed)
            return Unauthorized("Adresse ou mot de passe incorrect");

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = Hasher.HashPassword(user, request.Password);
            await db.SaveChangesAsync(ct);
        }

        const string scheme = CookieAuthenticationDefaults.AuthenticationScheme;
        await HttpContext.SignInAsync(scheme, Access.ToPrincipal(user, scheme));
        return new CurrentUser(user.DisplayName, user.Email, user.Role);
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
    }

    [HttpGet("me")]
    public async Task<ActionResult<CurrentUser>> Me(CancellationToken ct)
    {
        var id = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct);
        // Compte supprimé depuis l'ouverture de la session.
        return user is null ? Unauthorized() : new CurrentUser(user.DisplayName, user.Email, user.Role);
    }
}
