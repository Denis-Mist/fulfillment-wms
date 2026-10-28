using Microsoft.EntityFrameworkCore;
using Wms.Application.Abstractions;
using Wms.Application.Auth;
using Wms.Application.Common;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Services;

namespace Wms.Application.Tasks;

public sealed record TaskSummaryDto(int Total, int Active, int Late, int Unassigned, int DoneToday, int AvgMinutes,
    IReadOnlyDictionary<string, int> ByStatus, IReadOnlyDictionary<string, int> ByType);

public sealed record WorkerLoadDto(string Id, string FullName, Role Role, int Active, int DoneToday, int UnitsToday, int AvgMinutes);

/// <summary>Фильтры списка. Status — статус, «active» или «late»; неизвестные значения игнорируются.</summary>
public sealed record TaskQuery(PageQuery Page, string? Status, TaskType? Type, string? AssigneeId, string? WarehouseId);

/// <summary>Видимость заданий — по складам организации (scopedTasks); сотрудники склада видят только свои.</summary>
public sealed class TaskScope(IAppDbContext db, WarehouseScope warehouses)
{
    public async Task<List<WarehouseTask>> ListAsync(RequestContext ctx, string? warehouseId, CancellationToken ct, bool tracking = false)
    {
        var query = tracking ? db.Tasks : db.Tasks.AsNoTracking();
        if (!string.IsNullOrEmpty(warehouseId)) query = query.Where(t => t.WarehouseId == warehouseId);
        if (await warehouses.VisibleWarehouseIdsAsync(ctx, ct) is { } visible) query = query.Where(t => visible.Contains(t.WarehouseId));
        return await query.OrderBy(t => EF.Property<long>(t, "Seq")).ToListAsync(ct);
    }

    /// <summary>Задание из видимых складов, иначе 404 «Задание не найдено».</summary>
    public async Task<WarehouseTask> FindAsync(RequestContext ctx, string id, CancellationToken ct)
    {
        var task = await db.Tasks.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (task is null || (await warehouses.VisibleWarehouseIdsAsync(ctx, ct) is { } visible && !visible.Contains(task.WarehouseId)))
            throw NotFound();
        return task;
    }

    public static AppException NotFound() => AppException.NotFound("Задание не найдено");
}

/// <summary>Сотрудники склада организации и их загрузка (workerLoads).</summary>
public sealed class WorkerDirectory(IAppDbContext db)
{
    public async Task<IReadOnlyList<(User User, Role Role)>> WorkersAsync(string organizationId, CancellationToken ct)
    {
        var users = await db.Users.AsNoTracking()
            .Where(u => u.Memberships.Any(m => m.OrganizationId == organizationId && TaskRules.Workers.Contains(m.Role)))
            .ToListAsync(ct);
        // Порядок сидирования: usr_fc1_2 раньше usr_fc1_10.
        return users.OrderBy(u => u.Id.Length).ThenBy(u => u.Id, StringComparer.Ordinal).Select(u => (u, u.Memberships.First(m => m.OrganizationId == organizationId).Role)).ToList();
    }

    public async Task<Role?> RoleInAsync(string userId, string organizationId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        return user?.Memberships.FirstOrDefault(m => m.OrganizationId == organizationId)?.Role;
    }

    public async Task<List<WorkerLoadDto>> LoadsAsync(string organizationId, IReadOnlyCollection<WarehouseTask> tasks, DateTimeOffset now, CancellationToken ct)
    {
        var sod = WorkDay.Start(now);
        return (await WorkersAsync(organizationId, ct)).Select(w =>
        {
            var mine = tasks.Where(t => t.AssigneeId == w.User.Id).ToList();
            var done = mine.Where(t => t.Status == WarehouseTaskStatus.Done && t.FinishedAt >= sod).ToList();
            var finished = mine.Where(t => t.Status == WarehouseTaskStatus.Done && t.StartedAt != null && t.FinishedAt != null).ToList();
            var avg = finished.Count == 0 ? 0 : finished.Average(t => (t.FinishedAt!.Value - t.StartedAt!.Value).TotalMinutes);
            return new WorkerLoadDto(w.User.Id, w.User.FullName, w.Role,
                mine.Count(t => t.Status is WarehouseTaskStatus.Assigned or WarehouseTaskStatus.InProgress),
                done.Count, done.Sum(t => t.Units), (int)Domain.Common.JsMath.Round(avg));
        }).ToList();
    }
}

public sealed class TaskQueryHandler(IAppDbContext db, TaskScope scope, WorkerDirectory workers, IClock clock)
{
    private static readonly WarehouseTaskStatus[] ActiveStatuses = [WarehouseTaskStatus.New, WarehouseTaskStatus.Assigned, WarehouseTaskStatus.InProgress];

    public async Task<TaskSummaryDto> SummaryAsync(RequestContext ctx, string? warehouseId, TaskType? type, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var tasks = (await scope.ListAsync(ctx, warehouseId, ct)).Where(t => type is null || t.Type == type).ToList();
        var items = (await TaskLookups.LoadAsync(db, tasks, false, ct)) is var l ? tasks.Select(t => l.ToItem(t, now)).ToList() : [];
        var sod = WorkDay.Start(now);
        var finished = items.Where(t => t.Status == WarehouseTaskStatus.Done && t.ElapsedMinutes != null).ToList();
        return new TaskSummaryDto(items.Count,
            items.Count(t => ActiveStatuses.Contains(t.Status)),
            items.Count(t => t.Status == WarehouseTaskStatus.InProgress && t.IsLate),
            items.Count(t => t.Status == WarehouseTaskStatus.New),
            items.Count(t => t.Status == WarehouseTaskStatus.Done && t.FinishedAt >= sod),
            finished.Count == 0 ? 0 : (int)Domain.Common.JsMath.Round(finished.Average(t => (double)t.ElapsedMinutes!.Value)),
            CountBy(items, t => SnakeCaseName.Of(t.Status)),
            CountBy(items.Where(t => t.Status is not (WarehouseTaskStatus.Done or WarehouseTaskStatus.Cancelled)), t => SnakeCaseName.Of(t.Type)));
    }

    public async Task<IReadOnlyList<WorkerLoadDto>> WorkersAsync(RequestContext ctx, string? warehouseId, CancellationToken ct)
    {
        if (ctx.OrganizationId is null) return [];
        return await workers.LoadsAsync(ctx.OrganizationId, await scope.ListAsync(ctx, warehouseId, ct), clock.UtcNow, ct);
    }

    public async Task<Paged<TaskListItemDto>> ListAsync(RequestContext ctx, TaskQuery query, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var tasks = (await scope.ListAsync(ctx, query.WarehouseId, ct))
            .Where(t => (query.Type is null || t.Type == query.Type) && (string.IsNullOrEmpty(query.AssigneeId) || t.AssigneeId == query.AssigneeId))
            .ToList();
        var lookups = await TaskLookups.LoadAsync(db, tasks, false, ct);
        var term = query.Page.Term;
        var status = StatusFilter(query.Status);
        var items = tasks.Select(t => lookups.ToItem(t, now)).Where(status)
            .Where(t => term.Length == 0 || t.Number.ToLowerInvariant().Contains(term) || t.Reference.ToLowerInvariant().Contains(term)
                        || (t.AssigneeName?.ToLowerInvariant().Contains(term) ?? false));
        return Paged<TaskListItemDto>.From(items.SortBy(SortKey(query.Page.SortField ?? "createdAt"), query.Page.OrderOr(SortOrder.Desc)), query.Page);
    }

    /// <summary>Карточка; сотрудник склада (кроме бригадира) видит только свои задания.</summary>
    public async Task<TaskDetailsDto> DetailsAsync(RequestContext ctx, string id, CancellationToken ct)
    {
        var task = await scope.FindAsync(ctx, id, ct);
        if (TaskRules.Workers.Contains(ctx.Role) && ctx.Role != Role.FcSupervisor && task.AssigneeId != ctx.UserId) throw TaskScope.NotFound();
        return (await TaskLookups.LoadAsync(db, [task], true, ct)).ToDetails(task, clock.UtcNow);
    }

    public static Func<TaskListItemDto, bool> StatusFilter(string? status) => status switch
    {
        null or "" => _ => true,
        "active" => t => ActiveStatuses.Contains(t.Status),
        "late" => t => t.Status == WarehouseTaskStatus.InProgress && t.IsLate,
        _ => SnakeCaseName.TryParse<WarehouseTaskStatus>(status, out var parsed) ? t => t.Status == parsed : _ => true,
    };

    private static Dictionary<string, int> CountBy<T>(IEnumerable<T> items, Func<T, string> key)
    {
        var result = new Dictionary<string, int>();
        foreach (var item in items) result[key(item)] = result.GetValueOrDefault(key(item)) + 1;
        return result;
    }

    private static Func<TaskListItemDto, object?>? SortKey(string field) => field switch
    {
        "id" => t => t.Id,
        "number" => t => t.Number,
        "type" => t => SnakeCaseName.Of(t.Type),
        "status" => t => SnakeCaseName.Of(t.Status),
        "priority" => t => SnakeCaseName.Of(t.Priority),
        "warehouseId" => t => t.WarehouseId,
        "warehouseName" => t => t.WarehouseName,
        "assigneeId" => t => t.AssigneeId,
        "assigneeName" => t => t.AssigneeName,
        "createdAt" => t => t.CreatedAt,
        "startedAt" => t => t.StartedAt,
        "finishedAt" => t => t.FinishedAt,
        "reference" => t => t.Reference,
        "ordersCount" => t => t.OrdersCount,
        "linesCount" => t => t.LinesCount,
        "units" => t => t.Units,
        "doneUnits" => t => t.DoneUnits,
        "normMinutes" => t => t.NormMinutes,
        "elapsedMinutes" => t => t.ElapsedMinutes,
        "isLate" => t => t.IsLate,
        _ => null,
    };
}
