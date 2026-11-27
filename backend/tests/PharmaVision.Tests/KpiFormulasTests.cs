using PharmaVision.Api.Services;
using PharmaVision.Domain;

namespace PharmaVision.Tests;

public class KpiFormulasTests
{
    private static DateTime At(int hour, int minute = 0) => new(2026, 11, 27, hour, minute, 0, DateTimeKind.Utc);

    [Fact]
    public void Ratios_follow_the_specification()
    {
        Assert.Equal(0.6, KpiFormulas.ConversionRate(60, 100));
        Assert.Equal(0.25, KpiFormulas.EngagementRate(25, 100));
        Assert.Equal(0.4, KpiFormulas.PickupRate(10, 25));
        Assert.Equal(0.5, KpiFormulas.ShelfTransformation(5, 10));
        Assert.Equal(100, KpiFormulas.AverageWaitSeconds([80, 120]));
    }

    [Fact]
    public void Empty_denominators_give_zero_and_engagement_is_capped()
    {
        Assert.Equal(0, KpiFormulas.ConversionRate(3, 0));
        Assert.Equal(0, KpiFormulas.PickupRate(3, 0));
        Assert.Equal(0, KpiFormulas.AverageWaitSeconds([]));
        Assert.Equal(1, KpiFormulas.EngagementRate(12, 10));
    }

    [Theory]
    [InlineData(null, ShelfState.Inconnu)]
    [InlineData(29.9, ShelfState.Rupture)]
    [InlineData(30.0, ShelfState.StockBas)]
    [InlineData(60.0, ShelfState.Conforme)]
    public void Shelf_state_depends_on_fill(double? fill, ShelfState expected) =>
        Assert.Equal(expected, KpiFormulas.StateOf(fill));

    [Fact]
    public void Rupture_duration_accumulates_time_below_threshold_and_ignores_long_gaps()
    {
        (DateTime, double)[] samples =
        [
            (At(9), 50), (At(9, 15), 20), (At(9, 30), 10), (At(9, 45), 90),
            (At(19, 45), 15), // dernière mesure de la journée : bornée à maxGap
        ];

        var total = KpiFormulas.RuptureDuration(samples, 30, At(23), TimeSpan.FromMinutes(20));

        Assert.Equal(TimeSpan.FromMinutes(50), total);
    }

    [Fact]
    public void Trend_is_relative_and_undefined_without_reference()
    {
        Assert.Equal(0.1, KpiFormulas.Trend(110, 100)!.Value, 6);
        Assert.Null(KpiFormulas.Trend(5, 0));
    }

    [Fact]
    public void Today_starts_at_local_midnight_and_compares_with_yesterday()
    {
        var tunis = TimeZoneInfo.FindSystemTimeZoneById("Africa/Tunis"); // UTC+1

        var range = PeriodRange.Resolve(Period.Today, At(10, 30), tunis);

        Assert.Equal(new DateTime(2026, 11, 26, 23, 0, 0, DateTimeKind.Utc), range.From);
        Assert.Equal(range.From.AddDays(-1), range.PreviousFrom);
        Assert.Equal(At(10, 30).AddDays(-1), range.PreviousTo);
    }

    [Fact]
    public void Seven_days_include_today_and_do_not_overlap_the_previous_period()
    {
        var range = PeriodRange.Resolve(Period.Week, At(10), TimeZoneInfo.Utc);

        Assert.Equal(new DateTime(2026, 11, 21, 0, 0, 0, DateTimeKind.Utc), range.From);
        Assert.True(range.PreviousTo <= range.From);
    }
}
