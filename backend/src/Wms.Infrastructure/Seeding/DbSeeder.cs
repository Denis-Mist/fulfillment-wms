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
    private sealed record SeedZone(string Id, string WarehouseId, string Code, string Name, string Type);
    private sealed record SeedLocation(string Id, string WarehouseId, string ZoneId, string Code, double X, double Y, int Level, double CapacityLiters);
    private sealed record SeedProduct(string Id, string SellerId, string Sku, string Name, string Category, string Barcode, string? WbBarcode,
        double Price, double WeightG, double VolumeLiters, bool TracksExpiry, bool RequiresMarking);
    private sealed record SeedStock(string Id, string ProductId, string SellerId, string WarehouseId, string LocationId, int Quantity, int Reserved);
    private sealed record CatalogData(SeedZone[] Zones, SeedLocation[] Locations, SeedProduct[] Products, SeedStock[] Stock);
    private sealed record SeedInboundLine(string Id, string ProductId, int ExpectedQty, int ReceivedQty, int DefectQty, string? LotNumber, DateTimeOffset? ExpiresAt);
    private sealed record SeedInboundEvent(string Id, string Type, DateTimeOffset At, string Title, string ActorName, string? Description);
    private sealed record SeedInbound(string Id, string Number, string Status, string SellerId, string WarehouseId, DateTimeOffset CreatedAt,
        DateTimeOffset ExpectedAt, string PackageType, int PackagesCount, string? Comment, string? DriverName, string? VehicleNumber,
        SeedInboundLine[] Lines, SeedInboundEvent[] Events);
    private sealed record InboundData(SeedInbound[] Inbound);
    private sealed record SeedOperation(string Id, string Number, string Type, DateTimeOffset At, string ActorName, string WarehouseId,
        string ProductId, string? FromLocationCode, string? ToLocationCode, int Quantity, int? Expected, int? Counted, string? Reason);
    private sealed record OperationsData(SeedOperation[] Operations);
    private sealed record SeedOrderLine(string ProductId, int Quantity, double Price);
    private sealed record SeedOrder(string Id, string Number, string? ExternalNumber, string Channel, string Status, string SellerId,
        string WarehouseId, DateTimeOffset CreatedAt, DateTimeOffset ShipBy, DateTimeOffset? ShippedAt, string DeliveryCity, SeedOrderLine[] Lines);
    private sealed record OrdersData(SeedOrder[] Orders);
    private sealed record DirectoryData(SeedReference[] References, SeedRequest[] ClientRequests);

    /// <summary>Каждый раздел идемпотентен: заполняется, только если его таблица пуста.</summary>
    public async Task SeedAsync(CancellationToken ct = default)
    {
        await SeedIdentityAsync(ct);
        await SeedPlatformAsync(ct);
        await SeedDirectoryAsync(ct);
        await SeedCatalogAsync(ct);
        await SeedInboundAsync(ct);
        await SeedOperationsAsync(ct);
        await SeedOrdersAsync(ct);
    }

    /// <summary>Момент, на который сняты демо-данные (seed(Date.UTC(2026, 9, 1))).</summary>
    public static readonly DateTimeOffset SnapshotAt = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Заказы за 120 дней (Seeding/Data/orders.json). В моках они строятся относительно Date.now, поэтому даты
    /// сдвигаются от снимка к моменту сидирования — окна «за 30 дней» и просрочки остаются осмысленными.
    /// </summary>
    private async Task SeedOrdersAsync(CancellationToken ct)
    {
        if (await db.Orders.AnyAsync(ct)) return;
        var data = Load<OrdersData>("orders.json");
        var shift = clock.UtcNow - SnapshotAt;
        if (shift < TimeSpan.Zero) shift = TimeSpan.Zero;
        foreach (var o in data.Orders)
        {
            var order = new Order(o.Id, o.Number, o.ExternalNumber, SnakeCase.Parse<SalesChannel>(o.Channel), SnakeCase.Parse<OrderStatus>(o.Status),
                o.SellerId, o.WarehouseId, o.CreatedAt, o.ShipBy, o.ShippedAt, o.DeliveryCity,
                o.Lines.Select(l => new OrderLine(l.ProductId, l.Quantity, l.Price)));
            order.ShiftTimes(shift);
            db.Orders.Add(order);
        }
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded {Orders} orders", data.Orders.Length);
    }

    /// <summary>Синтетический журнал операций (seedOperations() из stock.ts на 2026-10-01, Seeding/Data/operations.json).</summary>
    private async Task SeedOperationsAsync(CancellationToken ct)
    {
        if (await db.StockOperations.AnyAsync(ct)) return;
        var data = Load<OperationsData>("operations.json");
        var products = await db.Products.AsNoTracking().ToDictionaryAsync(p => p.Id, ct);
        var orgs = await db.Organizations.AsNoTracking().ToDictionaryAsync(o => o.Id, o => o.Name, ct);
        var warehouses = await db.Warehouses.AsNoTracking().ToDictionaryAsync(w => w.Id, w => w.Name, ct);
        db.StockOperations.AddRange(data.Operations.Select(o =>
        {
            var product = products.GetValueOrDefault(o.ProductId);
            return new StockOperation(o.Id, o.Number, SnakeCase.Parse<OperationType>(o.Type), o.At, o.ActorName, o.WarehouseId,
                warehouses.GetValueOrDefault(o.WarehouseId) ?? "—", o.ProductId, product?.Sku ?? "—", product?.Name ?? "Товар",
                (product is null ? null : orgs.GetValueOrDefault(product.SellerId)) ?? "—", o.Quantity, o.FromLocationCode, o.ToLocationCode,
                o.Expected, o.Counted, o.Reason is null ? null : SnakeCase.Parse<ErrorReason>(o.Reason));
        }));
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded {Operations} stock operations", data.Operations.Length);
    }

    /// <summary>Поставки (снимок seed.ts, Seeding/Data/inbound.json).</summary>
    private async Task SeedInboundAsync(CancellationToken ct)
    {
        if (await db.InboundShipments.AnyAsync(ct)) return;
        var data = Load<InboundData>("inbound.json");
        db.InboundShipments.AddRange(data.Inbound.Select(i => new InboundShipment(i.Id, i.Number, SnakeCase.Parse<InboundStatus>(i.Status),
            i.SellerId, i.WarehouseId, i.CreatedAt, i.ExpectedAt, SnakeCase.Parse<PackageType>(i.PackageType), i.PackagesCount,
            i.Comment, i.DriverName, i.VehicleNumber,
            i.Lines.Select(l => new InboundLine(l.Id, l.ProductId, l.ExpectedQty, l.ReceivedQty, l.DefectQty, l.LotNumber, l.ExpiresAt)),
            i.Events.Select(e => new InboundEvent(e.Id, SnakeCase.Parse<InboundEventType>(e.Type), e.At, e.Title, e.ActorName, e.Description)))));
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded {Inbound} inbound shipments", data.Inbound.Length);
    }

    /// <summary>Топология складов, товары и остатки (снимок seed.ts, Seeding/Data/catalog.json); порядок вставки = порядок моков.</summary>
    private async Task SeedCatalogAsync(CancellationToken ct)
    {
        if (await db.Zones.AnyAsync(ct)) return;
        var data = Load<CatalogData>("catalog.json");
        db.Zones.AddRange(data.Zones.Select(z => new Zone(z.Id, z.WarehouseId, z.Code, z.Name, SnakeCase.Parse<ZoneType>(z.Type))));
        await db.SaveChangesAsync(ct);
        db.Locations.AddRange(data.Locations.Select(l => new StorageLocation(l.Id, l.WarehouseId, l.ZoneId, l.Code, l.X, l.Y, l.Level, l.CapacityLiters)));
        await db.SaveChangesAsync(ct);
        db.Products.AddRange(data.Products.Select(p => new Product(p.Id, p.SellerId, p.Sku, p.Name, p.Category, p.Barcode, p.WbBarcode,
            p.Price, p.WeightG, p.VolumeLiters, p.TracksExpiry, p.RequiresMarking)));
        await db.SaveChangesAsync(ct);
        db.Stock.AddRange(data.Stock.Select(s => new StockItem(s.Id, s.ProductId, s.SellerId, s.WarehouseId, s.LocationId, s.Quantity, s.Reserved)));
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded {Zones} zones, {Locations} locations, {Products} products, {Stock} stock rows",
            data.Zones.Length, data.Locations.Length, data.Products.Length, data.Stock.Length);
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
