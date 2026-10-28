using Microsoft.EntityFrameworkCore;
using Wms.Application.Abstractions;
using Wms.Application.Auth;
using Wms.Application.Common;
using Wms.Domain.Common;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Services;

namespace Wms.Application.Tasks;

public sealed record ScanResultDto(TaskDetailsDto Task, string Message);
public sealed record LookupRowDto(string Id, string Primary, string Secondary, int Quantity, int Reserved);
public sealed record LookupResultDto(string Kind, string Title, string Subtitle, IReadOnlyList<LookupRowDto> Rows);

/// <summary>ТСД сотрудника: свои задания, старт, сканирование, завершение и поиск по коду.</summary>
public sealed class WorkTerminalHandler(IAppDbContext db, IUnitOfWork uow, WarehouseScope warehouses, IClock clock)
{
    public async Task<IReadOnlyList<TaskListItemDto>> ListAsync(RequestContext ctx, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var sod = WorkDay.Start(now);
        var mine = (await db.Tasks.AsNoTracking().Where(t => t.AssigneeId == ctx.UserId).OrderBy(t => EF.Property<long>(t, "Seq")).ToListAsync(ct))
            .Where(t => t.Status is WarehouseTaskStatus.Assigned or WarehouseTaskStatus.InProgress
                        || (t.Status == WarehouseTaskStatus.Done && t.FinishedAt >= sod)).ToList();
        var lookups = await TaskLookups.LoadAsync(db, mine, false, ct);
        return mine.Select(t => lookups.ToItem(t, now))
            .OrderBy(t => t.Status switch { WarehouseTaskStatus.InProgress => 0, WarehouseTaskStatus.Assigned => 1, _ => 2 })
            .ThenBy(t => t.Priority == TaskPriority.High ? 0 : 1)
            .ThenBy(t => t.CreatedAt).ToList();
    }

    public async Task<TaskDetailsDto> StartAsync(RequestContext ctx, string id, CancellationToken ct)
    {
        var task = await OwnAsync(ctx, id, ct);
        if (!task.IsOpen) throw AppException.Conflict("Задание закрыто");
        var busy = await db.Tasks.AsNoTracking().FirstOrDefaultAsync(
            t => t.AssigneeId == ctx.UserId && t.Status == WarehouseTaskStatus.InProgress && t.Id != task.Id, ct);
        if (busy is not null) throw AppException.Conflict("Сначала завершите текущее задание", busy.Number);
        Guard(() => task.StartWork(clock.UtcNow));
        await uow.SaveChangesAsync(ct);
        return await DetailsAsync(task, ct);
    }

    public async Task<ScanResultDto> ScanAsync(RequestContext ctx, string id, string? code, CancellationToken ct)
    {
        var task = await OwnAsync(ctx, id, ct);
        if (task.Status != WarehouseTaskStatus.InProgress) throw AppException.Conflict("Задание не в работе");
        var value = (code ?? string.Empty).Trim();
        var details = await DetailsAsync(task, ct);
        var step = details.Steps.FirstOrDefault(s => s.Done < s.Quantity) ?? throw AppException.Conflict("Все позиции уже собраны");
        if (string.Equals(value, step.LocationCode, StringComparison.OrdinalIgnoreCase))
            return new ScanResultDto(details, $"Ячейка {step.LocationCode} подтверждена");
        if (value != step.Barcode)
        {
            var other = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Barcode == value, ct);
            throw other is null
                ? AppException.Unprocessable("Штрихкод не распознан", $"Ожидается {step.Sku} в ячейке {step.LocationCode}.")
                : AppException.Unprocessable("Не тот товар", $"Отсканирован «{other.Name}», ожидается «{step.ProductName}» ({step.Sku}).");
        }
        var item = Guard(task.ScanUnit);
        await uow.SaveChangesAsync(ct);
        var left = item.Quantity - item.Progress;
        return new ScanResultDto(await DetailsAsync(task, ct), left > 0 ? $"{step.Sku}: ещё {left} шт" : $"Позиция {step.Sku} собрана");
    }

    /// <summary>Завершение; после отбора заказы собраны и для каждого создаётся задание упаковки.</summary>
    public async Task<TaskDetailsDto> CompleteAsync(RequestContext ctx, string id, CancellationToken ct)
    {
        var task = await OwnAsync(ctx, id, ct);
        var now = clock.UtcNow;
        Guard(() => task.CompleteWork(now));
        if (task.Type == TaskType.Picking)
        {
            var orders = await db.Orders.Where(o => task.OrderIds.Contains(o.Id)).ToListAsync(ct);
            var pick = await PickContext.LoadAsync(db, orders.Select(o => o.WarehouseId).Distinct().ToList(), ct);
            var number = "T-" + now.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture)[^6..];
            foreach (var orderId in task.OrderIds)
            {
                var order = orders.FirstOrDefault(o => o.Id == orderId);
                if (order is null) continue;
                order.MarkPicked();
                var packId = $"tsk_p{order.Id}";
                if (await db.Tasks.AnyAsync(t => t.Id == packId, ct)) continue;
                db.Tasks.Add(WarehouseTask.PackingFor(packId, number, order.WarehouseId, task.OrganizationId, now, task.Priority, order.Id,
                    PickListBuilder.ForPacking(pick.PickItems([order]), pick.FirstLocation(order.WarehouseId, "PAK"))));
            }
        }
        await uow.SaveChangesAsync(ct);
        return await DetailsAsync(task, ct);
    }

    public async Task<LookupResultDto> LookupAsync(RequestContext ctx, string? code, CancellationToken ct)
    {
        var value = (code ?? string.Empty).Trim();
        var upper = value.ToUpperInvariant();
        var visible = await warehouses.VisibleWarehouseIdsAsync(ctx, ct);
        var warehouseNames = await db.Warehouses.AsNoTracking().ToDictionaryAsync(w => w.Id, w => w.Name, ct);
        var location = await db.Locations.AsNoTracking().Where(l => l.Code.ToUpper() == upper && (visible == null || visible.Contains(l.WarehouseId)))
            .OrderBy(l => EF.Property<long>(l, "Seq")).FirstOrDefaultAsync(ct);
        if (location is not null)
        {
            var zone = await db.Zones.AsNoTracking().FirstOrDefaultAsync(z => z.Id == location.ZoneId, ct);
            var rows = await db.Stock.AsNoTracking().Where(s => s.LocationId == location.Id && s.Quantity > 0).OrderBy(s => EF.Property<long>(s, "Seq")).ToListAsync(ct);
            var productIds = rows.Select(r => r.ProductId).ToList();
            var products = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
            var orgs = await OrgNamesAsync(rows.Select(r => r.SellerId), ct);
            return new LookupResultDto("location", $"Ячейка {location.Code}",
                $"{zone?.Name ?? string.Empty} · {warehouseNames.GetValueOrDefault(location.WarehouseId) ?? string.Empty} · ярус {location.Level}",
                rows.Select(s => new LookupRowDto(s.Id, products.GetValueOrDefault(s.ProductId)?.Name ?? "—",
                    $"{products.GetValueOrDefault(s.ProductId)?.Sku ?? string.Empty} · {orgs.GetValueOrDefault(s.SellerId) ?? string.Empty}", s.Quantity, s.Reserved)).ToList());
        }
        var product = await db.Products.AsNoTracking().Where(p => p.Barcode == value || p.Sku.ToUpper() == upper)
            .OrderBy(p => EF.Property<long>(p, "Seq")).FirstOrDefaultAsync(ct);
        if (product is null) throw AppException.NotFound("Ничего не найдено", $"Код «{value}» не соответствует ячейке или товару.");
        var stock = await db.Stock.AsNoTracking().Where(s => s.ProductId == product.Id && s.Quantity > 0 && (visible == null || visible.Contains(s.WarehouseId)))
            .OrderBy(s => EF.Property<long>(s, "Seq")).ToListAsync(ct);
        var locationIds = stock.Select(s => s.LocationId).ToList();
        var codes = await db.Locations.AsNoTracking().Where(l => locationIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, l => l.Code, ct);
        var seller = await OrgNamesAsync([product.SellerId], ct);
        return new LookupResultDto("product", product.Name, $"{product.Sku} · {product.Barcode} · {seller.GetValueOrDefault(product.SellerId) ?? string.Empty}",
            stock.Select(s => new LookupRowDto(s.Id, codes.GetValueOrDefault(s.LocationId) ?? "—", warehouseNames.GetValueOrDefault(s.WarehouseId) ?? string.Empty,
                s.Quantity, s.Reserved)).ToList());
    }

    private async Task<Dictionary<string, string>> OrgNamesAsync(IEnumerable<string> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        return await db.Organizations.AsNoTracking().Where(o => list.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o.Name, ct);
    }

    private async Task<WarehouseTask> OwnAsync(RequestContext ctx, string id, CancellationToken ct) =>
        await db.Tasks.FirstOrDefaultAsync(t => t.Id == id && t.AssigneeId == ctx.UserId, ct) ?? throw TaskScope.NotFound();

    private async Task<TaskDetailsDto> DetailsAsync(WarehouseTask task, CancellationToken ct) =>
        (await TaskLookups.LoadAsync(db, [task], true, ct)).ToDetails(task, clock.UtcNow);

    private static void Guard(Action action) => Guard(() => { action(); return 0; });

    private static T Guard<T>(Func<T> action)
    {
        try { return action(); }
        catch (DomainConflictException e) { throw AppException.Conflict(e.Message); }
        catch (DomainValidationException e) { throw AppException.Unprocessable(e.Message, e.Detail); }
    }
}
