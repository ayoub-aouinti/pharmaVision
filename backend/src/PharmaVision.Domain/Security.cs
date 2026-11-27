using System.Security.Cryptography;
using System.Text;

namespace PharmaVision.Domain;

/// <summary>Clés des boîtiers : seule leur empreinte est enregistrée.</summary>
public static class DeviceKeys
{
    /// <summary>Clé aléatoire de 256 bits, affichée une seule fois à sa création.</summary>
    public static string Create() =>
        "pv_" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    // Une empreinte simple suffit pour une clé aléatoire longue (contrairement à un mot de passe).
    public static string Hash(string key) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
}

/// <summary>Protection des comptes contre les essais répétés de mots de passe.</summary>
public static class AccountPolicy
{
    public const int MinPasswordLength = 10;
    public const int MaxFailedLogins = 5;
    public static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(15);
}
