using Wms.Domain.Entities;
using Wms.Domain.Enums;

namespace Wms.Domain.Services;

/// <summary>Кандидат на автоназначение: роль, текущая загрузка, график и навыки из кадрового профиля.</summary>
public sealed record AssignmentCandidate(
    string Id,
    Role Role,
    int Active,
    bool HasProfile,
    string? WarehouseId,
    IReadOnlyCollection<Skill> Skills,
    bool OnShift);

/// <summary>
/// Политика автоназначения (FR-15): роль и лимит активных заданий, доступность по смене/графику,
/// предпочтение квалификации под тип задания и склада профиля. Ручное назначение политику не использует.
/// </summary>
public static class AssignmentPolicy
{
    /// <summary>Навык, который желателен для типа задания; для перемещения достаточно отбора, приёмки или погрузчика.</summary>
    public static bool HasQualification(AssignmentCandidate worker, TaskType type)
    {
        if (worker.Skills.Count == 0) return false;
        return type switch
        {
            TaskType.Receiving or TaskType.Putaway => worker.Skills.Contains(Skill.Receiving),
            TaskType.Picking => worker.Skills.Contains(Skill.Picking),
            TaskType.Packing => worker.Skills.Contains(Skill.Packing),
            TaskType.Inventory => worker.Skills.Contains(Skill.Inventory),
            TaskType.Move => worker.Skills.Contains(Skill.Picking)
                || worker.Skills.Contains(Skill.Receiving)
                || worker.Skills.Contains(Skill.Forklift),
            _ => false,
        };
    }

    /// <summary>Без профиля сотрудник доступен, как раньше; с профилем нужна активная смена и свободный слот.</summary>
    public static bool IsAvailable(AssignmentCandidate worker) =>
        worker.Active < WarehouseTask.MaxActivePerWorker && (!worker.HasProfile || worker.OnShift);

    public static bool CanAssign(AssignmentCandidate worker, TaskType type) =>
        TaskRules.EligibleRoles(type).Contains(worker.Role) && IsAvailable(worker);

    public static AssignmentCandidate? Select(IEnumerable<AssignmentCandidate> workers, TaskType type, string warehouseId)
    {
        var eligible = workers.Where(w => CanAssign(w, type)).ToList();
        if (eligible.Count == 0) return null;

        var qualified = eligible.Where(w => HasQualification(w, type)).ToList();
        var pool = qualified.Count > 0 ? qualified : eligible;
        var local = pool.Where(w => w.WarehouseId is null || w.WarehouseId == warehouseId).ToList();
        var chosen = local.Count > 0 ? local : pool;

        return chosen
            .OrderBy(w => w.Active)
            .ThenBy(w => w.Role == Role.FcSupervisor ? 1 : 0)
            .First();
    }
}
