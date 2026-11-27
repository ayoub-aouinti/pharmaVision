namespace PharmaVision.Api.Services;

/// <summary>Découpage d'une période en intervalles alignés sur l'heure de l'officine.</summary>
public static class Buckets
{
    public static TimeSpan SizeOf(Period period) => period switch
    {
        Period.Live => TimeSpan.FromMinutes(5),
        Period.Today => TimeSpan.FromHours(1),
        _ => TimeSpan.FromDays(1),
    };

    /// <summary>Début (UTC) de l'intervalle local contenant l'instant.</summary>
    public static DateTime Floor(DateTime utc, TimeSpan size, TimeZoneInfo tz) =>
        ToUtc(FloorLocal(utc, size, tz), tz);

    /// <summary>Tous les intervalles de la période, y compris ceux sans mesure.</summary>
    public static IEnumerable<DateTime> Between(DateTime fromUtc, DateTime toUtc, TimeSpan size, TimeZoneInfo tz)
    {
        for (var local = FloorLocal(fromUtc, size, tz); ; local += size)
        {
            if (tz.IsInvalidTime(local)) continue;
            var start = ToUtc(local, tz);
            if (start >= toUtc) yield break;
            yield return start;
        }
    }

    private static DateTime FloorLocal(DateTime utc, TimeSpan size, TimeZoneInfo tz)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(utc, tz);
        return new DateTime(local.Ticks - local.Ticks % size.Ticks, DateTimeKind.Unspecified);
    }

    private static DateTime ToUtc(DateTime local, TimeZoneInfo tz)
    {
        // Heure sautée au passage à l'heure d'été : on prend la suivante.
        while (tz.IsInvalidTime(local)) local = local.AddHours(1);
        return TimeZoneInfo.ConvertTimeToUtc(local, tz);
    }
}
