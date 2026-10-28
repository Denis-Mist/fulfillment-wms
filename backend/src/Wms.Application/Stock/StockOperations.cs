using Microsoft.EntityFrameworkCore;
using Wms.Application.Abstractions;
using Wms.Application.Auth;
using Wms.Application.Common;
using Wms.Application.Inbound;
using Wms.Domain.Common;
using Wms.Domain.Entities;
using Wms.Domain.Enums;

namespace Wms.Application.Stock;

public sealed record StockOperationDto(string Id, string Number, OperationType Type, DateTimeOffset At, string ActorName, string WarehouseId,
    string WarehouseName, string ProductId, string Sku, string ProductName, string SellerName, string? FromLocationCode,
    string? ToLocationCode, int Quantity, int? Expected, int? Counted, ErrorReason? Reason, string? Comment)
{
    public static StockOperationDto From(StockOperation o) => new(o.Id, o.Number, o.Type, o.At, o.ActorName, o.WarehouseId, o.WarehouseName,
        o.ProductId, o.Sku, o.ProductName, o.SellerName, o.FromLocationCode, o.ToLocationCode, o.Quantity, o.Expected, o.Counted, o.Reason, o.Comment);
}

public sealed record OperationError(ErrorReason Reason, int Count, int Quantity);

public sealed record OperationSummary(IReadOnlyDictionary<string, int> ByType, IReadOnlyList<OperationError> Errors, int Last30Days);

public sealed record OperationQuery(PageQuery Page, string? WarehouseId, OperationType? Type, string? StockId);

public sealed record MoveInput(string? StockId, string? ToLocationId, double? Quantity, string? Comment);
public sealed record InventoryInput(string? StockId, double? Counted, string? Comment);
public sealed record WriteoffInput(string? StockId, double? Quantity, string? Reason, string? Comment);

public static class StockRoles
{
    /// <summary>OPERATION_ROLES из моков: офис ФЦ + приёмщик.</summary>
    public static readonly Role[] Operations = [.. InboundRoles.FcOffice, Role.FcReceiver];
}

public sealed class StockOperationQueryHandler(IAppDbContext db, WarehouseScope warehouses, IClock clock)
{
    private static readonly ErrorReason[] Reasons = [ErrorReason.Defect, ErrorReason.Shortage, ErrorReason.Missort, ErrorReason.Expired];

    public async Task<Paged<StockOperationDto>> ListAsync(RequestContext ctx, OperationQuery q, CancellationToken ct)
    {
        var items = await ScopedAsync(ctx, q.WarehouseId, ct);
        if (q.Type is { } type) items = items.Where(o => o.Type == type).ToList();
        if (!string.IsNullOrEmpty(q.StockId))
        {
            var s = await db.Stock.AsNoTracking().FirstOrDefaultAsync(x => x.Id == q.StockId, ct);
            items = s is null ? [] : items.Where(o => o.ProductId == s.ProductId && o.WarehouseId == s.WarehouseId).ToList();
        }
        var term = q.Page.Term;
        var filtered = items.Where(o => term.Length == 0 || new[] { o.Number, o.Sku, o.ProductName, o.ActorName, o.FromLocationCode, o.ToLocationCode }
            .Any(v => v?.ToLowerInvariant().Contains(term) == true)).Select(StockOperationDto.From);
        return Paged<StockOperationDto>.From(filtered.SortBy(SortKey(q.Page.SortField ?? "at"), q.Page.OrderOr(SortOrder.Desc)), q.Page);
    }

    public async Task<OperationSummary> SummaryAsync(RequestContext ctx, string? warehouseId, CancellationToken ct)
    {
        var since = clock.UtcNow.AddDays(-30);
        var recent = (await ScopedAsync(ctx, warehouseId, ct)).Where(o => o.At >= since).ToList();
        var byType = Enum.GetValues<OperationType>().ToDictionary(SnakeCaseName.Of, t => recent.Count(o => o.Type == t));
        var errors = Reasons.Select(r =>
        {
            var list = recent.Where(o => o.Reason == r).ToList();
            return new OperationError(r, list.Count, list.Sum(o => Math.Abs(Math.Min(0, o.Quantity))));
        }).ToList();
        return new OperationSummary(byType, errors, recent.Count);
    }

    private async Task<List<StockOperation>> ScopedAsync(RequestContext ctx, string? warehouseId, CancellationToken ct)
    {
        var visible = await warehouses.VisibleWarehouseIdsAsync(ctx, ct);
        var query = db.StockOperations.AsNoTracking();
        if (!string.IsNullOrEmpty(warehouseId)) query = query.Where(o => o.WarehouseId == warehouseId);
        if (visible is not null) query = query.Where(o => visible.Contains(o.WarehouseId));
        return await query.OrderBy(o => EF.Property<long>(o, "Seq")).ToListAsync(ct);
    }

    private static Func<StockOperationDto, object?>? SortKey(string field) => field switch
    {
        "id" => o => o.Id, "number" => o => o.Number, "type" => o => SnakeCaseName.Of(o.Type), "at" => o => o.At,
        "actorName" => o => o.ActorName, "warehouseId" => o => o.WarehouseId, "warehouseName" => o => o.WarehouseName,
        "productId" => o => o.ProductId, "sku" => o => o.Sku, "productName" => o => o.ProductName, "sellerName" => o => o.SellerName,
        "fromLocationCode" => o => o.FromLocationCode, "toLocationCode" => o => o.ToLocationCode, "quantity" => o => o.Quantity,
        "expected" => o => o.Expected, "counted" => o => o.Counted,
        "reason" => o => o.Reason is { } r ? SnakeCaseName.Of(r) : null, "comment" => o => o.Comment,
        _ => null,
    };
}

/// <summary>Перемещение, инвентаризация и списание остатка с записью в журнал (как POST /stock/* в моках).</summary>
public sealed class StockCommandHandler(IAppDbContext db, StockReadModel stock, IClock clock, IIdGenerator ids)
{
    public async Task<StockOperationDto> MoveAsync(RequestContext ctx, MoveInput? input, CancellationToken ct)
    {
        var s = await RequireAsync(ctx, input?.StockId, ct);
        var target = await db.Locations.AsNoTracking().FirstOrDefaultAsync(l => l.Id == input!.ToLocationId, ct);
        if (target is null || target.WarehouseId != s.WarehouseId) throw AppException.Unprocessable("Ячейка назначения не найдена на этом складе");
        if (target.Id == s.LocationId) throw AppException.Unprocessable("Товар уже находится в этой ячейке");
        var quantity = PositiveInt(input!.Quantity);
        if (quantity > s.Free)
            throw AppException.Conflict("Недостаточно свободного остатка", $"Свободно {s.Free} шт — зарезервированный товар перемещать нельзя.");

        var snapshot = await stock.ToDtoAsync(s, ct);
        var now = clock.UtcNow;
        s.Take(quantity);
        var existing = await db.Stock.Where(x => x.ProductId == s.ProductId && x.LocationId == target.Id && x.SellerId == s.SellerId)
            .OrderBy(x => EF.Property<long>(x, "Seq")).FirstOrDefaultAsync(ct);
        if (existing is not null) existing.Receive(quantity);
        else db.Stock.Add(new StockItem($"stk_{s.ProductId}_{target.Id}_{Base36(now)}", s.ProductId, s.SellerId, s.WarehouseId, target.Id, quantity, 0));
        if (s.IsEmpty) db.Stock.Remove(s);
        return await RecordAsync(ctx, snapshot, now, OperationType.Move, quantity, snapshot.LocationCode, target.Code, comment: input.Comment, ct: ct);
    }

    public async Task<StockOperationDto> InventoryAsync(RequestContext ctx, InventoryInput? input, CancellationToken ct)
    {
        var s = await RequireAsync(ctx, input?.StockId, ct);
        if (input!.Counted is not { } raw || raw != Math.Floor(raw) || raw < 0)
            throw AppException.Unprocessable("Фактическое количество не может быть отрицательным");
        var counted = (int)raw;
        if (counted < s.Reserved)
            throw AppException.Conflict("Факт меньше резерва", $"Под заказы зарезервировано {s.Reserved} шт. Сначала снимите резерв.");
        var snapshot = await stock.ToDtoAsync(s, ct);
        var expected = s.Quantity;
        var delta = s.Recount(counted);
        return await RecordAsync(ctx, snapshot, clock.UtcNow, OperationType.Inventory, delta, snapshot.LocationCode, null, expected, counted,
            StockOperation.InventoryReason(delta), input.Comment, ct);
    }

    public async Task<StockOperationDto> WriteoffAsync(RequestContext ctx, WriteoffInput? input, CancellationToken ct)
    {
        var s = await RequireAsync(ctx, input?.StockId, ct);
        if (string.IsNullOrEmpty(input!.Reason) || !SnakeCaseName.TryParse<ErrorReason>(input.Reason, out var reason))
            throw AppException.Unprocessable("Укажите причину списания");
        var quantity = PositiveInt(input.Quantity);
        if (quantity > s.Free) throw AppException.Conflict("Нельзя списать больше свободного остатка", $"Свободно {s.Free} шт.");
        var snapshot = await stock.ToDtoAsync(s, ct);
        s.Take(quantity);
        return await RecordAsync(ctx, snapshot, clock.UtcNow, OperationType.Writeoff, -quantity, snapshot.LocationCode, null,
            reason: reason, comment: input.Comment, ct: ct);
    }

    private async Task<StockItem> RequireAsync(RequestContext ctx, string? id, CancellationToken ct)
    {
        var s = id is null ? null : await db.Stock.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s is null || !await stock.CanSeeAsync(ctx, s, ct)) throw AppException.NotFound("Позиция остатка не найдена");
        return s;
    }

    private static int PositiveInt(double? value) => value is { } v && v == Math.Floor(v) && v > 0 && v <= int.MaxValue
        ? (int)v : throw AppException.Unprocessable("Количество должно быть больше нуля");

    private async Task<StockOperationDto> RecordAsync(RequestContext ctx, StockItemDto item, DateTimeOffset now, OperationType type, int quantity,
        string? from, string? to, int? expected = null, int? counted = null, ErrorReason? reason = null, string? comment = null,
        CancellationToken ct = default)
    {
        var actor = await db.Users.AsNoTracking().Where(u => u.Id == ctx.UserId).Select(u => u.FullName).FirstOrDefaultAsync(ct) ?? "—";
        var count = await db.StockOperations.LongCountAsync(ct);
        var op = new StockOperation($"op_{Base36(now)}_{ids.NewSuffix()}", StockOperation.NumberFor(count), type, now, actor, item.WarehouseId,
            item.WarehouseName, item.ProductId, item.Sku, item.ProductName, item.SellerName, quantity, from, to, expected, counted, reason,
            string.IsNullOrEmpty(comment) ? null : comment);
        db.StockOperations.Add(op);
        await db.SaveChangesAsync(ct);
        return StockOperationDto.From(op);
    }

    private static string Base36(DateTimeOffset at)
    {
        var n = at.ToUnixTimeMilliseconds();
        const string digits = "0123456789abcdefghijklmnopqrstuvwxyz";
        var result = string.Empty;
        do { result = digits[(int)(n % 36)] + result; n /= 36; } while (n > 0);
        return result;
    }
}
