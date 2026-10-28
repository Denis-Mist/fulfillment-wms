using Microsoft.EntityFrameworkCore;
using Wms.Application.Abstractions;
using Wms.Application.Auth;
using Wms.Application.Common;
using Wms.Domain.Entities;
using Wms.Domain.Enums;

namespace Wms.Application.Partners;

public sealed record FcWarehouseInfo(string Id, string Name, string City, string Address, int AreaSqm);
public sealed record FcStartRates(int ReceivingPerUnit, int StoragePerM3Day, int PickingPerOrder, int PackingPerUnit);
public sealed record CardContract(string Id, DateTimeOffset StartedAt, IReadOnlyList<string> WarehouseIds);

public sealed record FulfillmentCard(string Id, string Name, string City, IReadOnlyList<string> Cities, double Rating,
    int ClientsCount, IReadOnlyList<string> Services, IReadOnlyList<string> Marketplaces, IReadOnlyList<FcWarehouseInfo> Warehouses,
    FcStartRates Rates, int SlaHours, string Connection, CardContract? Contract, DateTimeOffset? RequestedAt);

public sealed record ConnectRequestInput(double SkuEstimate, double OrdersPerDay, IReadOnlyList<string>? Marketplaces, string? Message);

/// <summary>Витринные параметры ФЦ — детерминированно из id (порт cardFor() из моков).</summary>
public sealed record FulfillmentShowcase(double Rating, int ExtraClients, IReadOnlyList<string> Services,
    IReadOnlyList<string> Marketplaces, FcStartRates Rates, int SlaHours)
{
    private static readonly string[] AllServices = ["storage", "fbs", "fbo", "marking", "returns", "photo", "kits"];
    private static readonly int[] Sla = [12, 24, 24, 48];

    public static FulfillmentShowcase For(string fulfillmentId)
    {
        var rng = new Mulberry32(Mulberry32.SeedOf(fulfillmentId));
        var services = AllServices.Where((_, i) => i < 3 || rng.Bool(0.6)).ToList();
        var rating = Math.Round(rng.Float(4.2, 4.95) * 10, MidpointRounding.AwayFromZero) / 10;
        var extra = rng.Int(8, 60);
        IReadOnlyList<string> marketplaces = rng.Bool(0.7) ? ["wb", "ozon", "yandex"] : ["wb", "ozon"];
        var rates = new FcStartRates(rng.Int(3, 8), 30 + rng.Int(0, 5) * 5, rng.Int(35, 60), rng.Int(6, 14));
        return new FulfillmentShowcase(rating, extra, services, marketplaces, rates, rng.Pick(Sla));
    }
}

public sealed class FulfillmentCardBuilder(IAppDbContext db)
{
    public async Task<FulfillmentCard> BuildAsync(Organization fc, string sellerId, CancellationToken ct)
    {
        var showcase = FulfillmentShowcase.For(fc.Id);
        var warehouses = (await db.Warehouses.AsNoTracking().Where(w => w.OrganizationId == fc.Id && w.IsActive).OrderBy(w => w.Id).ToListAsync(ct))
            .Select(w => new FcWarehouseInfo(w.Id, w.Name, w.City, w.Address, w.AreaSqm)).ToList();
        var contracts = await db.Contracts.AsNoTracking().Where(c => c.FulfillmentId == fc.Id).OrderBy(c => c.Id).ToListAsync(ct);
        var contract = contracts.FirstOrDefault(c => c.SellerId == sellerId);
        var mine = await db.ClientRequests.AsNoTracking().Where(r => r.FulfillmentId == fc.Id && r.SellerId == sellerId)
            .OrderByDescending(r => r.CreatedAt).FirstOrDefaultAsync(ct);
        var connection = contract is not null ? "connected" : mine is not null ? SnakeCaseName.Of(mine.Status) : "none";
        return new FulfillmentCard(fc.Id, fc.Name, fc.City, warehouses.Select(w => w.City).Distinct().ToList(), showcase.Rating,
            contracts.Count + showcase.ExtraClients, showcase.Services, showcase.Marketplaces, warehouses, showcase.Rates,
            showcase.SlaHours, connection,
            contract is null ? null : new CardContract(contract.Id, contract.StartedAt, contract.WarehouseIds), mine?.CreatedAt);
    }
}

public sealed class FulfillmentCardsQueryHandler(IAppDbContext db, FulfillmentCardBuilder builder)
{
    private static readonly Dictionary<string, int> ConnectionOrder = new()
    {
        ["connected"] = 0, ["accepted"] = 1, ["pending"] = 2, ["none"] = 3, ["declined"] = 4,
    };

    public async Task<IReadOnlyList<FulfillmentCard>> HandleAsync(RequestContext ctx, CancellationToken ct)
    {
        if (ctx.OrganizationId is null) return [];
        var cards = new List<FulfillmentCard>();
        foreach (var fc in await ActiveFulfillments(db).ToListAsync(ct))
            cards.Add(await builder.BuildAsync(fc, ctx.OrganizationId, ct));
        return cards.OrderBy(c => ConnectionOrder[c.Connection]).ThenByDescending(c => c.Rating).ToList();
    }

    internal static IQueryable<Organization> ActiveFulfillments(IAppDbContext db) =>
        db.Organizations.AsNoTracking()
            .Where(o => o.Type == OrganizationType.Fulfillment && o.Status == OrganizationStatus.Active)
            .OrderBy(o => o.CreatedAt).ThenBy(o => o.Id);
}

/// <summary>Заявка селлера на подключение к ФЦ (только seller_owner).</summary>
public sealed class ConnectFulfillmentHandler(IAppDbContext db, FulfillmentCardBuilder builder, IClock clock, IIdGenerator ids)
{
    public async Task<FulfillmentCard> HandleAsync(RequestContext ctx, string fulfillmentId, ConnectRequestInput? input, CancellationToken ct)
    {
        var fc = await FulfillmentCardsQueryHandler.ActiveFulfillments(db).FirstOrDefaultAsync(o => o.Id == fulfillmentId, ct);
        var seller = ctx.OrganizationId is null ? null
            : await db.Organizations.AsNoTracking().FirstOrDefaultAsync(o => o.Id == ctx.OrganizationId, ct);
        if (fc is null || seller is null) throw AppException.NotFound("Фулфилмент-центр не найден");
        var current = await builder.BuildAsync(fc, seller.Id, ct);
        if (current.Connection == "connected") throw AppException.Conflict("Договор уже заключён");
        if (current.Connection == "pending") throw AppException.Conflict("Заявка уже на рассмотрении");
        if (input is null || !(input.SkuEstimate > 0) || !(input.OrdersPerDay >= 0))
            throw AppException.Unprocessable("Укажите ассортимент и объём заказов");
        if (input.Marketplaces is not { Count: > 0 }) throw AppException.Unprocessable("Выберите хотя бы один маркетплейс");
        var user = await db.Users.AsNoTracking().FirstAsync(u => u.Id == ctx.UserId, ct);
        db.ClientRequests.Add(new ClientRequest(fc.Id, $"creq_{ids.NewSuffix()}", seller.Name, seller.Inn, seller.City,
            user.FullName, user.Phone ?? "", (int)Math.Round(input.SkuEstimate, MidpointRounding.AwayFromZero),
            (int)Math.Round(input.OrdersPerDay, MidpointRounding.AwayFromZero), input.Marketplaces,
            input.Message?.Trim() ?? "", clock.UtcNow, seller.Id));
        await db.SaveChangesAsync(ct);
        return await builder.BuildAsync(fc, seller.Id, ct);
    }
}
