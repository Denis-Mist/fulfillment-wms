using Wms.Application.Analytics;
using Wms.Domain.Enums;

namespace Wms.Application.Tests;

public class ForecastingTests
{
    private static readonly DateTimeOffset Today = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void History_is_deterministic_and_positive()
    {
        var a = Forecasting.History(42, 100, Today);
        Assert.Equal(Forecasting.HistoryDays, a.Count);
        Assert.Equal(a.Select(p => p.Value), Forecasting.History(42, 100, Today).Select(p => p.Value));
        Assert.All(a, p => Assert.True(p.Value > 0));
        Assert.Equal(Today.AddDays(-1), a[^1].Date);
    }

    [Fact]
    public void Holt_winters_tracks_seasonal_series_better_than_moving_average()
    {
        var values = Forecasting.History(7, 200, Today).Select(p => (double)p.Value).ToList();
        var train = values.Take(42).ToList();
        var test = values.Skip(42).ToList();
        var hw = Forecasting.Errors(test, Forecasting.Predict(ForecastAlgorithm.HoltWinters, train, 14));
        var ma = Forecasting.Errors(test, Forecasting.Predict(ForecastAlgorithm.MovingAverage, train, 14));
        Assert.True(hw.Mape < ma.Mape, $"{hw.Mape} vs {ma.Mape}");
    }

    [Fact]
    public void Errors_and_rounding()
    {
        var e = Forecasting.Errors([100, 200], [90, 220]);
        Assert.Equal(15, e.Mae);
        Assert.Equal(10, e.Mape);
        Assert.Equal(3, Forecasting.Round(2.5));
        Assert.Equal(-2, Forecasting.Round(-2.5));
    }
}
