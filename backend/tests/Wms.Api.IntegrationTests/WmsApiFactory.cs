using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Wms.Application.Integrations;
using Testcontainers.PostgreSql;

namespace Wms.Api.IntegrationTests;

/// <summary>
/// Поднимает Api на настоящем Postgres: из WMS_TEST_CONNECTION (если задан) или Testcontainers (нужен Docker).
/// </summary>
public class WmsApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private PostgreSqlContainer? _container;
    private string _connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        var external = Environment.GetEnvironmentVariable("WMS_TEST_CONNECTION");
        if (!string.IsNullOrWhiteSpace(external))
        {
            _connectionString = $"{external};Database=wms_test_{Guid.NewGuid():N}";
            return;
        }
        _container = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await _container.StartAsync();
        _connectionString = _container.GetConnectionString();
    }

    protected virtual void ApplySettings(IWebHostBuilder builder) { }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Wms", _connectionString);
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Database:Seed", "true");
        // Общий лимит логина 20/мин иначе мешает пачкам интеграционных тестов на одном IP.
        builder.UseSetting("RateLimiting:AuthPerMinute", "10000");
        builder.UseSetting("RateLimiting:DemoPerMinute", "10000");
        builder.UseSetting("RateLimiting:DemoPerHour", "100000");
        ApplySettings(builder);
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IMarketplaceClientResolver, TestMarketplaceClientResolver>();
        });
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        if (_container is not null) await _container.DisposeAsync();
    }
}
