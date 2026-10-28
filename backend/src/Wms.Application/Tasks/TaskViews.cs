using Microsoft.EntityFrameworkCore;
using Wms.Application.Abstractions;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Services;

namespace Wms.Application.Tasks;

public record TaskListItemDto
{
    public string Id { get; init; } = string.Empty;
    public string Number { get; init; } = string.Empty;
    public TaskType Type { get; init; }
    public WarehouseTaskStatus Status { get; init; }
    public TaskPriority Priority { get; init; }
    public string WarehouseId { get; init; } = string.Empty;
    public string WarehouseName { get; init; } = string.Empty;
    public string? AssigneeId { get; init; }
    public string? AssigneeName { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? FinishedAt { get; init; }
    /// <summary>Документ-основание: номера заказов или поставки.</summary>
    public string Reference { get; init; } = string.Empty;
    public int OrdersCount { get; init; }
    public int LinesCount { get; init; }
    public int Units { get; init; }
    public int DoneUnits { get; init; }
    public int NormMinutes { get; init; }
    public int? ElapsedMinutes { get; init; }
    public bool IsLate { get; init; }
}

public sealed record TaskStepDto(int Index, string LocationId, string LocationCode, string ZoneCode, string ProductId, string ProductName,
    string Sku, string Barcode, int Quantity, int Done, double Distance);

public sealed record TaskDetailsDto : TaskListItemDto
{
    public IReadOnlyList<TaskStepDto> Steps { get; init; } = [];
    public string Algorithm { get; init; } = RouteAlgorithms.Optimized;
    public double RouteDistance { get; init; }
    public double BaselineDistance { get; init; }
    public IReadOnlyList<Role> EligibleRoles { get; init; } = [];
}

/// <summary>Справочники для представления заданий (toItem/toDetails в моках).</summary>
public sealed class TaskLookups
{
    private TaskLookups() { }

    public IReadOnlyDictionary<string, string> WarehouseNames { get; private init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> UserNames { get; private init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> OrderNumbers { get; private init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> InboundNumbers { get; private init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, StorageLocation> Locations { get; private init; } = new Dictionary<string, StorageLocation>();
    public IReadOnlyDictionary<string, string> ZoneCodes { get; private init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, Product> Products { get; private init; } = new Dictionary<string, Product>();

    public static async Task<TaskLookups> LoadAsync(IAppDbContext db, IReadOnlyCollection<WarehouseTask> tasks, bool withSteps, CancellationToken ct)
    {
        var orderIds = tasks.SelectMany(t => t.OrderIds).Distinct().ToList();
        var inboundIds = tasks.Where(t => t.InboundId != null).Select(t => t.InboundId!).Distinct().ToList();
        var userIds = tasks.Where(t => t.AssigneeId != null).Select(t => t.AssigneeId!).Distinct().ToList();
        var locationIds = withSteps ? tasks.SelectMany(t => t.Items.Select(i => i.LocationId)).Distinct().ToList() : [];
        var productIds = withSteps ? tasks.SelectMany(t => t.Items.Select(i => i.ProductId)).Distinct().ToList() : [];
        var locations = await db.Locations.AsNoTracking().Where(l => locationIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, ct);
        var zoneIds = locations.Values.Select(l => l.ZoneId).Distinct().ToList();
        return new TaskLookups
        {
            WarehouseNames = await db.Warehouses.AsNoTracking().ToDictionaryAsync(w => w.Id, w => w.Name, ct),
            UserNames = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct),
            OrderNumbers = await db.Orders.AsNoTracking().Where(o => orderIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o.Number, ct),
            InboundNumbers = await db.InboundShipments.AsNoTracking().Where(s => inboundIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Number, ct),
            Locations = locations,
            ZoneCodes = await db.Zones.AsNoTracking().Where(z => zoneIds.Contains(z.Id)).ToDictionaryAsync(z => z.Id, z => z.Code, ct),
            Products = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct),
        };
    }

    public string Reference(WarehouseTask t)
    {
        if (t.InboundId is not null) return InboundNumbers.GetValueOrDefault(t.InboundId) ?? "—";
        if (t.OrderIds.Count > 0)
        {
            var nums = t.OrderIds.Select(id => OrderNumbers.GetValueOrDefault(id) ?? id).ToList();
            return nums.Count > 2 ? $"{string.Join(", ", nums.Take(2))} +{nums.Count - 2}" : string.Join(", ", nums);
        }
        return t.Type == TaskType.Inventory ? "Плановая" : "Подпитка зоны отбора";
    }

    public TaskListItemDto ToItem(WarehouseTask t, DateTimeOffset now) => Fill(new TaskListItemDto(), t, now);

    public TaskDetailsDto ToDetails(WarehouseTask t, DateTimeOffset now)
    {
        double x = 0, y = 0;
        var steps = t.Items.Select((it, index) =>
        {
            var loc = Locations.GetValueOrDefault(it.LocationId);
            var product = Products.GetValueOrDefault(it.ProductId);
            var d = loc is null ? 0 : RoutePlanner.Distance(x, y, loc.X, loc.Y);
            if (loc is not null) (x, y) = (loc.X, loc.Y);
            return new TaskStepDto(index, it.LocationId, loc?.Code ?? "—", loc is null ? string.Empty : ZoneCodes.GetValueOrDefault(loc.ZoneId) ?? string.Empty,
                it.ProductId, product?.Name ?? "—", product?.Sku ?? string.Empty, product?.Barcode ?? string.Empty, it.Quantity, it.Progress,
                Domain.Common.JsMath.Round1(d));
        }).ToList();
        var points = t.Items.Select(i => Locations.GetValueOrDefault(i.LocationId)).OfType<StorageLocation>()
            .Select(l => new RouteNode(l.Code, l.X, l.Y)).ToList();
        return Fill(new TaskDetailsDto
        {
            Steps = steps,
            Algorithm = t.Algorithm,
            RouteDistance = RoutePlanner.Length(points),
            BaselineDistance = RoutePlanner.Length(RoutePlanner.Baseline(points, p => p)),
            EligibleRoles = TaskRules.EligibleRoles(t.Type),
        }, t, now);
    }

    private T Fill<T>(T dto, WarehouseTask t, DateTimeOffset now) where T : TaskListItemDto
    {
        var norm = TaskRules.NormMinutes(t);
        var end = t.FinishedAt ?? now;
        int? elapsed = t.StartedAt is { } started ? (int)Domain.Common.JsMath.Round((end - started).TotalMinutes) : null;
        return dto with
        {
            Id = t.Id, Number = t.Number, Type = t.Type, Status = t.Status, Priority = t.Priority,
            WarehouseId = t.WarehouseId, WarehouseName = WarehouseNames.GetValueOrDefault(t.WarehouseId) ?? string.Empty,
            AssigneeId = t.AssigneeId, AssigneeName = t.AssigneeId is null ? null : UserNames.GetValueOrDefault(t.AssigneeId),
            CreatedAt = t.CreatedAt, StartedAt = t.StartedAt, FinishedAt = t.FinishedAt, Reference = Reference(t),
            OrdersCount = t.OrderIds.Count, LinesCount = t.Items.Count, Units = t.Units, DoneUnits = t.DoneUnits,
            NormMinutes = norm, ElapsedMinutes = elapsed, IsLate = elapsed is { } e && e > norm * 1.2,
        };
    }
}

/// <summary>Начало текущих суток (UTC) — граница «сегодня» для выработки.</summary>
internal static class WorkDay
{
    public static DateTimeOffset Start(DateTimeOffset now) => new(now.UtcDateTime.Date, TimeSpan.Zero);
}
