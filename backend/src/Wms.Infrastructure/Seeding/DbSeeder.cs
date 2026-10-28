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
public sealed class DbSeeder(WmsDbContext db, IPasswordHasher hasher, IClock clock, ILogger<DbSeeder> logger)
{
    private sealed record SeedOrg(string Id, string Type, string Name, string Inn, string City, string Status,
        DateTimeOffset CreatedAt, string[]? Marketplaces);

    private sealed record SeedMembership(string OrganizationId, string Role);

    private sealed record SeedUser(string Id, string Email, string Password, string FullName, string? Phone,
        bool IsPlatformAdmin, SeedMembership[] Memberships);

    private sealed record SeedData(SeedOrg[] Organizations, SeedUser[] Users);

    private sealed record SeedContract(string Id, string FulfillmentId, string SellerId, string[] WarehouseIds, DateTimeOffset StartedAt);
    private sealed record SeedWarehouse(string Id, string OrganizationId, string Name, string Code, string City, string Address, int AreaSqm, bool IsActive);
    private sealed record SeedSettings(string Id, OrgProfile Profile, bool SlaRisk);
    private sealed record PlatformData(SeedContract[] Contracts, SeedWarehouse[] Warehouses, SeedSettings[] Settings);
    private sealed record SeedReference(string Kind, string Code, string Name, string? Note);
    private sealed record SeedRequest(string CompanyName, string Inn, string City, string ContactName, string ContactPhone,
        int SkuEstimate, int OrdersPerDay, string[] Marketplaces, string Message);
    private sealed record DirectoryData(SeedReference[] References, SeedRequest[] ClientRequests);

    /// <summary>Каждый раздел идемпотентен: заполняется, только если его таблица пуста.</summary>
    public async Task SeedAsync(CancellationToken ct = default)
    {
        await SeedIdentityAsync(ct);
        await SeedPlatformAsync(ct);
        await SeedDirectoryAsync(ct);
    }

    private async Task SeedPlatformAsync(CancellationToken ct)
    {
        if (await db.Warehouses.AnyAsync(ct)) return;
        var data = Load<PlatformData>("platform.json");
        db.Warehouses.AddRange(data.Warehouses.Select(w =>
            new Warehouse(w.Id, w.OrganizationId, w.Name, w.Code, w.City, w.Address, w.AreaSqm, w.IsActive)));
        db.Contracts.AddRange(data.Contracts.Select(c =>
            new Contract(c.Id, c.FulfillmentId, c.SellerId, c.WarehouseIds, c.StartedAt.ToUniversalTime())));
        var types = await db.Organizations.AsNoTracking().ToDictionaryAsync(o => o.Id, o => o.Type, ct);
        db.Settings.AddRange(data.Settings.Where(s => types.ContainsKey(s.Id)).Select(s => new OrganizationSettings(s.Id,
            s.Profile, CardRules.Default, NotificationPrefs.DefaultFor(types[s.Id]) with { SlaRisk = s.SlaRisk })));
        await db.SaveChangesAsync(ct);
    }

    private async Task SeedDirectoryAsync(CancellationToken ct)
    {
        if (await db.References.AnyAsync(ct)) return;
        var data = Load<DirectoryData>("directory.json");
        var position = 0;
        foreach (var r in data.References)
        {
            var item = ReferenceItem.Seeded($"ref_{r.Kind}_{r.Code}", SnakeCase.Parse<ReferenceKind>(r.Kind), r.Code, r.Name, r.Note);
            item.PlaceAt(position++);
            db.References.Add(item);
        }

        // Как в моках: у каждого ФЦ одинаковый набор входящих заявок, время — относительно момента сида
        var now = clock.UtcNow;
        var fcs = await db.Organizations.AsNoTracking().Where(o => o.Type == OrganizationType.Fulfillment).Select(o => o.Id).ToListAsync(ct);
        foreach (var fc in fcs)
            for (var i = 0; i < data.ClientRequests.Length; i++)
            {
                var r = data.ClientRequests[i];
                var request = new ClientRequest(fc, $"creq_{i + 1}", r.CompanyName, r.Inn, r.City, r.ContactName, r.ContactPhone,
                    r.SkuEstimate, r.OrdersPerDay, r.Marketplaces, r.Message, now.AddHours(-(i * 19 + 3)), sellerId: null);
                if (i == 3) request.Decline(now.AddHours(-30));
                db.ClientRequests.Add(request);
            }
        await db.SaveChangesAsync(ct);
    }

    private async Task SeedIdentityAsync(CancellationToken ct)
    {
        if (await db.Users.AnyAsync(ct)) return;
        var data = Load<SeedData>("identity.json");
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

    private static T Load<T>(string file)
    {
        using var stream = typeof(DbSeeder).Assembly.GetManifestResourceStream($"Wms.Infrastructure.Seeding.Data.{file}")
            ?? throw new InvalidOperationException($"Seed resource {file} not found");
        return JsonSerializer.Deserialize<T>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }
}
