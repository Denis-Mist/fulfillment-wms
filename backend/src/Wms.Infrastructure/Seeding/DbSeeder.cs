using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Application.Abstractions;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Infrastructure.Common;
using Wms.Infrastructure.Persistence;

namespace Wms.Infrastructure.Seeding;

/// <summary>
/// Демо-данные из frontend/src/mocks/db/seed.ts (снимок Seeding/Data/identity.json, пароль у всех «demo»).
/// </summary>
public sealed class DbSeeder(WmsDbContext db, IPasswordHasher hasher, ILogger<DbSeeder> logger)
{
    private sealed record SeedOrg(string Id, string Type, string Name, string Inn, string City, string Status,
        DateTimeOffset CreatedAt, string[]? Marketplaces);

    private sealed record SeedMembership(string OrganizationId, string Role);

    private sealed record SeedUser(string Id, string Email, string Password, string FullName, string? Phone,
        bool IsPlatformAdmin, SeedMembership[] Memberships);

    private sealed record SeedData(SeedOrg[] Organizations, SeedUser[] Users);

    public async Task SeedAsync(CancellationToken ct = default)
    {
        if (await db.Users.AnyAsync(ct)) return;
        var data = Load();
        var orgs = data.Organizations.ToDictionary(o => o.Id, o => new Organization(o.Id,
            SnakeCase.Parse<OrganizationType>(o.Type), o.Name, o.Inn, o.City,
            SnakeCase.Parse<OrganizationStatus>(o.Status), o.CreatedAt.ToUniversalTime(),
            o.Marketplaces?.Select(SnakeCase.Parse<Marketplace>)));
        db.Organizations.AddRange(orgs.Values);

        var hashes = new Dictionary<string, string>();
        foreach (var u in data.Users)
        {
            if (!hashes.TryGetValue(u.Password, out var hash)) hashes[u.Password] = hash = hasher.Hash(u.Password);
            var user = new User(u.Id, u.Email, hash, u.FullName, u.Phone, u.IsPlatformAdmin);
            foreach (var m in u.Memberships) user.JoinOrganization(orgs[m.OrganizationId], SnakeCase.Parse<Role>(m.Role));
            db.Users.Add(user);
        }
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded {Orgs} organizations and {Users} users", orgs.Count, data.Users.Length);
    }

    private static SeedData Load()
    {
        using var stream = typeof(DbSeeder).Assembly.GetManifestResourceStream("Wms.Infrastructure.Seeding.Data.identity.json")
            ?? throw new InvalidOperationException("Seed resource not found");
        return JsonSerializer.Deserialize<SeedData>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }
}
