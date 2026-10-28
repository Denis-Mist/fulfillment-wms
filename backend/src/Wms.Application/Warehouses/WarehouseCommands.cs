using Microsoft.EntityFrameworkCore;
using Wms.Application.Abstractions;
using Wms.Application.Auth;
using Wms.Application.Common;
using Wms.Domain.Common;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Services;

namespace Wms.Application.Warehouses;

public static class WarehouseRoles
{
    /// <summary>Изменять топологию склада могут владелец и менеджер ФЦ.</summary>
    public static readonly Role[] Management = [Role.FcOwner, Role.FcManager];
}

public sealed record ZoneInput(string? Code, string? Name, string? Type);

public sealed class CreateZoneHandler(IAppDbContext db, WarehouseScope scope)
{
    public async Task<ZoneDto> HandleAsync(RequestContext ctx, string warehouseId, ZoneInput? input, CancellationToken ct)
    {
        var warehouse = await scope.RequireWarehouseAsync(ctx, warehouseId, ct);
        input ??= new ZoneInput(null, null, null);
        var knownType = SnakeCaseName.TryParse<ZoneType>(input.Type, out var type);
        Zone zone;
        try { zone = Zone.Create(warehouse.Id, input.Code, input.Name, type); }
        catch (DomainException e) { throw AppException.BadRequest(e.Message); }
        if (!knownType) throw AppException.BadRequest("Неизвестный тип зоны");
        if (await db.Zones.AnyAsync(z => z.WarehouseId == warehouse.Id && z.Code == zone.Code, ct))
            throw AppException.Conflict("Зона с таким кодом уже существует");
        db.Zones.Add(zone);
        await db.SaveChangesAsync(ct);
        return WarehouseMapper.ToDto(zone, [], new WarehouseStock(new Dictionary<string, List<WarehouseStock.Row>>()));
    }
}

public sealed record LocationInput(string? ZoneId, string? Aisle, int? Rack, int? Level, double? CapacityLiters);

public sealed class CreateLocationHandler(IAppDbContext db, WarehouseScope scope)
{
    public async Task<StorageLocationDto> HandleAsync(RequestContext ctx, string warehouseId, LocationInput? input, CancellationToken ct)
    {
        var warehouse = await scope.RequireWarehouseAsync(ctx, warehouseId, ct);
        input ??= new LocationInput(null, null, null, null, null);
        StorageLocation location;
        try
        {
            location = StorageLocation.Create(warehouse.Id, input.ZoneId ?? string.Empty, input.Aisle, input.Rack ?? 0, input.Level ?? 0,
                input.CapacityLiters ?? 0);
        }
        catch (DomainException e) { throw AppException.BadRequest(e.Message); }
        var zone = await db.Zones.AsNoTracking().FirstOrDefaultAsync(z => z.Id == input.ZoneId && z.WarehouseId == warehouse.Id, ct)
            ?? throw AppException.BadRequest("Зона не найдена");
        if (await db.Locations.AnyAsync(l => l.WarehouseId == warehouse.Id && l.Code == location.Code, ct))
            throw AppException.Conflict($"Ячейка {location.Code} уже существует");
        db.Locations.Add(location);
        await db.SaveChangesAsync(ct);
        return WarehouseMapper.ToDto(location, zone.Code, new WarehouseStock(new Dictionary<string, List<WarehouseStock.Row>>()),
            new Dictionary<string, string>());
    }
}

public sealed record PutawayCheckInput(string? LocationId, string? ProductId, double? Quantity);

public sealed record PutawayCheckResult(bool Allowed, IReadOnlyList<PutawayCheck> Checks, double FreeLitersAfter);

/// <summary>POST /warehouses/:id/putaway-check — проверка правил размещения без изменения данных.</summary>
public sealed class PutawayCheckHandler(IAppDbContext db, WarehouseScope scope)
{
    public async Task<PutawayCheckResult> HandleAsync(RequestContext ctx, string warehouseId, PutawayCheckInput? input, CancellationToken ct)
    {
        var warehouse = await scope.RequireWarehouseAsync(ctx, warehouseId, ct);
        var location = input?.LocationId is null ? null
            : await db.Locations.AsNoTracking().FirstOrDefaultAsync(l => l.Id == input.LocationId && l.WarehouseId == warehouse.Id, ct);
        var product = input?.ProductId is null ? null : await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == input.ProductId, ct);
        if (location is null || product is null) throw AppException.BadRequest("Укажите ячейку и товар");
        if (!(input!.Quantity > 0)) throw AppException.BadRequest("Количество должно быть больше нуля");

        var zone = await db.Zones.AsNoTracking().FirstOrDefaultAsync(z => z.Id == location.ZoneId, ct);
        var stock = (await WarehouseStock.LoadAsync(db, warehouse.Id, ct)).For(location.Id)
            .Select(r => new LocationStock(r.SellerId, r.Quantity, r.UnitVolumeLiters)).ToList();
        var contracted = (await db.Contracts.AsNoTracking().Where(c => c.SellerId == product.SellerId).ToListAsync(ct))
            .Any(c => c.WarehouseIds.Contains(warehouse.Id));
        var decision = PutawayPolicy.Evaluate(location, zone, product, input.Quantity.Value, stock, contracted);
        return new PutawayCheckResult(decision.Allowed, decision.Checks, decision.FreeLitersAfter);
    }
}
