using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using PharmaVision.Domain;

namespace PharmaVision.Api.Security;

public static class Roles
{
    public const string Titulaire = nameof(UserRole.Titulaire);
}

public static class Access
{
    public const string PharmacyClaim = "pharmacy";

    public static ClaimsPrincipal ToPrincipal(User user, string scheme)
    {
        List<Claim> claims =
        [
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.DisplayName),
            new(ClaimTypes.Role, user.Role.ToString()),
            .. user.PharmacyIds.Select(id => new Claim(PharmacyClaim, id.ToString())),
        ];
        return new ClaimsPrincipal(new ClaimsIdentity(claims, scheme));
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
