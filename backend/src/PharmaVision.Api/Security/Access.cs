using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using PharmaVision.Domain;
using PharmaVision.Infrastructure;

namespace PharmaVision.Api.Security;

public static class Roles
{
    public const string Titulaire = nameof(UserRole.Titulaire);
}

public static class Access
{
    public const string PharmacyClaim = "pharmacy";
    public const string VersionClaim = "version";

    public static ClaimsPrincipal ToPrincipal(User user, string scheme)
    {
        List<Claim> claims =
        [
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.DisplayName),
            new(ClaimTypes.Role, user.Role.ToString()),
            new(VersionClaim, user.SecurityVersion.ToString()),
            .. user.PharmacyIds.Select(id => new Claim(PharmacyClaim, id.ToString())),
        ];
        return new ClaimsPrincipal(new ClaimsIdentity(claims, scheme));
    }

    /// <summary>
    /// Appelé à chaque requête : une session tombe dès que le compte est supprimé, désactivé ou modifié.
    /// </summary>
    public static async Task ValidateSessionAsync(CookieValidatePrincipalContext context)
    {
        var db = context.HttpContext.RequestServices.GetRequiredService<PharmaDbContext>();
        var principal = context.Principal!;
        var valid = int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            && int.TryParse(principal.FindFirstValue(VersionClaim), out var version)
            && await db.Users.AnyAsync(u => u.Id == id && u.IsActive && u.SecurityVersion == version,
                context.HttpContext.RequestAborted);
        if (valid) return;
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(context.Scheme.Name);
    }

    public static List<int> PharmacyIds(this ClaimsPrincipal user) =>
        user.FindAll(PharmacyClaim).Select(c => int.Parse(c.Value)).ToList();

    public static bool CanAccess(this ClaimsPrincipal user, int pharmacyId) =>
        user.HasClaim(PharmacyClaim, pharmacyId.ToString());
}

/// <summary>
/// Refuse toute action dont le paramètre « pharmacyId » désigne une officine hors du compte.
/// Les accès par identifiant de ressource sont filtrés dans les contrôleurs.
/// </summary>
public class PharmacyScopeFilter : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (context.ActionArguments.TryGetValue("pharmacyId", out var value)
            && value is int id && !context.HttpContext.User.CanAccess(id))
            context.Result = new ForbidResult();
    }

    public void OnActionExecuted(ActionExecutedContext context) { }
}
