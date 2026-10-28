using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Wms.Application.Abstractions;
using Wms.Infrastructure.Persistence;
using Wms.Infrastructure.Persistence.Repositories;
using Wms.Infrastructure.Security;
using Wms.Infrastructure.Seeding;

namespace Wms.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Wms")
            ?? throw new InvalidOperationException("ConnectionStrings:Wms is not configured");
        services.AddDbContext<WmsDbContext>(o => o.UseNpgsql(connectionString).UseSnakeCaseNamingConvention());
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<WmsDbContext>());
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IOrganizationRepository, OrganizationRepository>();

        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.Section));
        services.AddScoped<ITokenService, JwtTokenService>();
        services.AddSingleton<IPasswordHasher, IdentityPasswordHasher>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IIdGenerator, IdGenerator>();
        services.AddScoped<DbSeeder>();
        return services;
    }

    /// <summary>Применяет миграции и сид (вызывается Api при старте).</summary>
    public static async Task MigrateAndSeedAsync(this IServiceProvider provider, bool seed, CancellationToken ct = default)
    {
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<WmsDbContext>().Database.MigrateAsync(ct);
        if (seed) await scope.ServiceProvider.GetRequiredService<DbSeeder>().SeedAsync(ct);
    }
}
