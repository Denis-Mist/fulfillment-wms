using Microsoft.EntityFrameworkCore;
using Wms.Application.Abstractions;
using Wms.Application.Auth;
using Wms.Application.Common;
using Wms.Domain.Common;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Services;

namespace Wms.Application.Tasks;

public sealed record AutoAssignResult(int Assigned);

/// <summary>Диспетчерская: автоназначение, ручное назначение и отмена заданий.</summary>
public sealed class TaskCommandHandler(IAppDbContext db, IUnitOfWork uow, TaskScope scope, WorkerDirectory workers, IClock clock)
{
    /// <summary>Новые задания (срочные, затем старые) — наименее загруженному подходящему сотруднику, не более 3 активных.</summary>
    public async Task<AutoAssignResult> AutoAssignAsync(RequestContext ctx, string? warehouseId, CancellationToken ct)
    {
        if (ctx.OrganizationId is null) return new AutoAssignResult(0);
        var tasks = await scope.ListAsync(ctx, warehouseId, ct, tracking: true);
        var loads = (await workers.LoadsAsync(ctx.OrganizationId, tasks, clock.UtcNow, ct)).Select(w => new MutableLoad(w.Id, w.Role, w.Active)).ToList();
        var queue = tasks.Where(t => t.Status == WarehouseTaskStatus.New)
            .OrderBy(t => t.Priority == TaskPriority.High ? 0 : 1).ThenBy(t => t.CreatedAt).ToList();
        var assigned = 0;
        foreach (var task in queue)
        {
            var roles = TaskRules.EligibleRoles(task.Type);
            var worker = loads.Where(w => roles.Contains(w.Role) && w.Active < WarehouseTask.MaxActivePerWorker)
                .OrderBy(w => w.Active).ThenBy(w => w.Role == Role.FcSupervisor ? 1 : 0).FirstOrDefault();
            if (worker is null) continue;
            task.Assign(worker.Id);
            worker.Active++;
            assigned++;
        }
        await uow.SaveChangesAsync(ct);
        return new AutoAssignResult(assigned);
    }

    public async Task<TaskDetailsDto> AssignAsync(RequestContext ctx, string id, string? userId, CancellationToken ct)
    {
        var task = await scope.FindAsync(ctx, id, ct);
        if (!task.IsOpen) throw AppException.Conflict("Задание уже закрыто");
        if (!string.IsNullOrEmpty(userId))
        {
            var role = await workers.RoleInAsync(userId, task.OrganizationId, ct);
            if (role is null || !TaskRules.EligibleRoles(task.Type).Contains(role.Value))
                throw AppException.Unprocessable("Сотрудник не может выполнять это задание", $"Нужна роль: {TaskRules.Label(task.Type).ToLowerInvariant()}");
        }
        try { task.Assign(string.IsNullOrEmpty(userId) ? null : userId); }
        catch (DomainConflictException e) { throw AppException.Conflict(e.Message); }
        await uow.SaveChangesAsync(ct);
        return await DetailsAsync(task, ct);
    }

    /// <summary>Отмена; заказы отменённого отбора возвращаются в резерв.</summary>
    public async Task<TaskDetailsDto> CancelAsync(RequestContext ctx, string id, CancellationToken ct)
    {
        var task = await scope.FindAsync(ctx, id, ct);
        try { task.CancelByOffice(); }
        catch (DomainConflictException e) { throw AppException.Conflict(e.Message); }
        if (task.Type == TaskType.Picking)
        {
            foreach (var order in await db.Orders.Where(o => task.OrderIds.Contains(o.Id)).ToListAsync(ct)) order.ReturnToReserve();
        }
        await uow.SaveChangesAsync(ct);
        return await DetailsAsync(task, ct);
    }

    private async Task<TaskDetailsDto> DetailsAsync(WarehouseTask task, CancellationToken ct) =>
        (await TaskLookups.LoadAsync(db, [task], true, ct)).ToDetails(task, clock.UtcNow);

    private sealed class MutableLoad(string id, Role role, int active)
    {
        public string Id { get; } = id;
        public Role Role { get; } = role;
        public int Active { get; set; } = active;
    }
}
