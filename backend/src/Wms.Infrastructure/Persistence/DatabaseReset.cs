using Microsoft.EntityFrameworkCore;
using Wms.Application.Demo;
using Wms.Infrastructure.Seeding;

namespace Wms.Infrastructure.Persistence;

/// <summary>
/// Потоковый сброс: TRUNCATE ... CASCADE без загрузки строк в память, затем идемпотентный сидер.
/// Историю миграций (__EFMigrationsHistory) не трогает — её нет в модели EF.
/// </summary>
public sealed class DatabaseReset(WmsDbContext db, DbSeeder seeder) : IDatabaseReset
{
    public async Task TruncateBusinessDataAsync(CancellationToken ct)
    {
        var tables = db.Model.GetEntityTypes()
            .Select(t => t.GetTableName())
            .Where(n => !string.IsNullOrEmpty(n))
            .Distinct(StringComparer.Ordinal)
            .Select(n => "\"" + n + "\"")
            .ToArray();
        if (tables.Length == 0) return;
        var sql = "TRUNCATE TABLE " + string.Join(", ", tables) + " CASCADE;";
        await db.Database.ExecuteSqlRawAsync(sql, ct);
        db.ChangeTracker.Clear();
    }

    public Task SeedAsync(CancellationToken ct) => seeder.SeedAsync(ct);
}
