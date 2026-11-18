using Microsoft.Extensions.Options;
using Wms.Application.Demo;

namespace Wms.Api.Infrastructure;

/// <summary>Ежедневный сброс демо-данных в Demo:ResetHourMoscow по Europe/Moscow, только при Demo:Enabled.</summary>
public sealed class DemoResetHostedService(
    IOptions<DemoOptions> options,
    IServiceScopeFactory scopes,
    ILogger<DemoResetHostedService> logger) : BackgroundService
{
    public const string TimeZoneId = "Europe/Moscow";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled) return;
        TimeZoneInfo tz;
        try { tz = TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId); }
        catch (TimeZoneNotFoundException ex)
        {
            logger.LogError(ex, "Часовой пояс {Zone} недоступен, ночной сброс демо отключён", TimeZoneId);
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var hour = options.Value.ResetHourMoscow;
            var delay = DelayUntilNextReset(hour, tz, DateTimeOffset.UtcNow);
            logger.LogInformation("Следующий сброс демо-данных через {Delay} (час {Hour} {Zone})", delay, hour, TimeZoneId);
            try { await Task.Delay(delay, stoppingToken); }
            catch (OperationCanceledException) { return; }

            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IDemoResetService>().ResetAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Ночной сброс демо-данных не выполнен");
            }
        }
    }

    /// <summary>Задержка до ближайшего <paramref name="hour"/>:00 в зоне <paramref name="tz"/>.</summary>
    public static TimeSpan DelayUntilNextReset(int hour, TimeZoneInfo tz, DateTimeOffset utcNow)
    {
        hour = Math.Clamp(hour, 0, 23);
        var local = TimeZoneInfo.ConvertTime(utcNow, tz);
        var scheduled = new DateTimeOffset(local.Year, local.Month, local.Day, hour, 0, 0, local.Offset);
        if (local >= scheduled) scheduled = scheduled.AddDays(1);
        var targetUtc = TimeZoneInfo.ConvertTime(scheduled, TimeZoneInfo.Utc);
        var delay = targetUtc - utcNow;
        return delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
    }
}
