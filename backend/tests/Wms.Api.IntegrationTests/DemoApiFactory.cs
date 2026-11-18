using Microsoft.AspNetCore.Hosting;

namespace Wms.Api.IntegrationTests;

/// <summary>Тестовый хост с включённым демо-режимом (дефолтный appsettings остаётся Enabled=false).</summary>
public sealed class DemoApiFactory : WmsApiFactory
{
    protected override void ApplySettings(IWebHostBuilder builder)
    {
        builder.UseSetting("Demo:Enabled", "true");
        builder.UseSetting("Demo:TokenLifetimeMinutes", "120");
        builder.UseSetting("Demo:ResetHourMoscow", "3");
    }
}

/// <summary>Реальные лимиты POST /api/auth/demo: 10/мин и 60/час.</summary>
public sealed class DemoRateLimitApiFactory : WmsApiFactory
{
    protected override void ApplySettings(IWebHostBuilder builder)
    {
        builder.UseSetting("Demo:Enabled", "true");
        builder.UseSetting("RateLimiting:DemoPerMinute", "10");
        builder.UseSetting("RateLimiting:DemoPerHour", "60");
    }
}
