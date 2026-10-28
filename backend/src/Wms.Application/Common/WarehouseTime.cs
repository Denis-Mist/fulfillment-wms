namespace Wms.Application.Common;

/// <summary>Местное время складов (Москва, UTC+3) для графика смен.</summary>
public static class WarehouseTime
{
    private static readonly TimeZoneInfo Zone = Resolve();

    private static TimeZoneInfo Resolve()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow"); }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.CreateCustomTimeZone("MSK", TimeSpan.FromHours(3), "MSK", "MSK");
        }
    }

    public static DateTimeOffset Local(DateTimeOffset now) => TimeZoneInfo.ConvertTime(now, Zone);

    /// <summary>День недели, 0 — понедельник.</summary>
    /// <summary>Конец текущих местных суток (23:59:59.999).</summary>
    public static DateTimeOffset EndOfDay(DateTimeOffset now)
    {
        var local = Local(now);
        return new DateTimeOffset(local.Date, local.Offset).AddDays(1).AddMilliseconds(-1);
    }

    public static int DayIndex(DateTimeOffset now) => ((int)Local(now).DayOfWeek + 6) % 7;

    /// <summary>Понедельник 00:00 местного времени текущей недели (в UTC).</summary>
    public static DateTimeOffset WeekStart(DateTimeOffset now)
    {
        var local = Local(now);
        var monday = local.Date.AddDays(-DayIndex(now));
        return new DateTimeOffset(monday, local.Offset).ToUniversalTime();
    }
}
