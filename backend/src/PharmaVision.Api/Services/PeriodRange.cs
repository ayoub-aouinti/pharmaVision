namespace PharmaVision.Api.Services;

public enum Period { Live, Today, Week, Month }

/// <summary>Fenêtre UTC analysée et fenêtre précédente comparable.</summary>
public record PeriodRange(DateTime From, DateTime To, DateTime PreviousFrom, DateTime PreviousTo)
{
    public static Period Parse(string? value) => value?.ToLowerInvariant() switch
    {
        "live" => Period.Live,
        "7d" => Period.Week,
        "30d" => Period.Month,
        _ => Period.Today,
    };

    public static PeriodRange Resolve(Period period, DateTime nowUtc, TimeZoneInfo tz)
    {
        if (period == Period.Live)
        {
            var from = nowUtc.AddHours(-1);
            return new(from, nowUtc, from.AddHours(-1), from);
        }

        var days = period switch { Period.Week => 7, Period.Month => 30, _ => 1 };
        var localMidnight = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, tz).Date;
        var start = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localMidnight, DateTimeKind.Unspecified), tz)
            .AddDays(-(days - 1));
        // « Aujourd'hui » se compare à la même tranche d'hier ; 7 j / 30 j à la période d'avant.
        var shift = TimeSpan.FromDays(days);
        return new(start, nowUtc, start - shift, nowUtc - shift);
    }
}
