using Wms.Application.Common;
using Wms.Domain.Enums;

namespace Wms.Application.Analytics;

public sealed record HistoryPoint(DateTimeOffset Date, int Value);

public sealed record ForecastErrors(double Mape, int Mae);

/// <summary>Модели прогноза (чистые функции): синтетическая история, Хольт–Уинтерс, скользящее среднее, ошибки.</summary>
public static class Forecasting
{
    public const int HistoryDays = 56;
    public const int BacktestDays = 14;
    private static readonly double[] Weekly = [1.18, 1.1, 1.04, 1.0, 1.08, 0.82, 0.78];

    /// <summary>Тренд + недельная сезонность + промо + шум; дни недели в UTC.</summary>
    public static List<HistoryPoint> History(uint seed, double scale, DateTimeOffset today)
    {
        var rng = new Mulberry32(seed);
        return Enumerable.Range(0, HistoryDays).Select(i =>
        {
            var date = today.AddDays(-(HistoryDays - i));
            var dow = ((int)date.UtcDateTime.DayOfWeek + 6) % 7;
            var promo = i is 30 or 31 ? 1.35 : 1;
            return new HistoryPoint(date, Round(scale * (1 + i * 0.004) * Weekly[dow] * promo * rng.Float(0.9, 1.1)));
        }).ToList();
    }

    public static double[] HoltWinters(IReadOnlyList<double> values, int horizon)
    {
        const int m = 7;
        const double alpha = 0.35, beta = 0.05, gamma = 0.3;
        var level = values.Take(m).Average();
        var trend = (values.Skip(m).Take(m).Average() - level) / m;
        var season = values.Take(m).Select(v => v - level).ToArray();
        for (var i = m; i < values.Count; i++)
        {
            var s = season[i % m];
            var prev = level;
            level = alpha * (values[i] - s) + (1 - alpha) * (level + trend);
            trend = beta * (level - prev) + (1 - beta) * trend;
            season[i % m] = gamma * (values[i] - level) + (1 - gamma) * s;
        }
        return Enumerable.Range(0, horizon).Select(h => level + (h + 1) * trend + season[(values.Count + h) % m]).ToArray();
    }

    public static double[] MovingAverage(IReadOnlyList<double> values, int horizon)
    {
        var avg = values.Skip(values.Count - 7).Sum() / 7;
        return Enumerable.Repeat(avg, horizon).ToArray();
    }

    public static double[] Predict(ForecastAlgorithm algorithm, IReadOnlyList<double> values, int horizon) =>
        algorithm == ForecastAlgorithm.HoltWinters ? HoltWinters(values, horizon) : MovingAverage(values, horizon);

    public static ForecastErrors Errors(IReadOnlyList<double> actual, IReadOnlyList<double> predicted)
    {
        double ape = 0, ae = 0;
        for (var i = 0; i < actual.Count; i++)
        {
            ae += Math.Abs(actual[i] - predicted[i]);
            ape += Math.Abs(actual[i] - predicted[i]) / Math.Max(1, actual[i]);
        }
        return new(Round(ape / actual.Count * 1000) / 10.0, Round(ae / actual.Count));
    }

    /// <summary>Math.round из JS (половина — вверх).</summary>
    public static int Round(double v) => (int)Math.Floor(v + 0.5);

    public static double Round1(double v) => Math.Floor(v * 10 + 0.5) / 10;
}
