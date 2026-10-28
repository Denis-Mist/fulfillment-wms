using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

/// <summary>Утверждённый план персонала по прогнозу; ключ — «{организация}:{склад|all}».</summary>
public sealed class StaffPlan
{
    private StaffPlan() { }

    public StaffPlan(string id, ForecastAlgorithm algorithm, string approvedBy, DateTimeOffset approvedAt)
    {
        Id = id;
        Approve(algorithm, approvedBy, approvedAt);
    }

    public string Id { get; private set; } = null!;
    public ForecastAlgorithm Algorithm { get; private set; }
    public string ApprovedBy { get; private set; } = null!;
    public DateTimeOffset ApprovedAt { get; private set; }

    public static string KeyFor(string? organizationId, string? warehouseId) => $"{organizationId ?? "all"}:{warehouseId ?? "all"}";

    public void Approve(ForecastAlgorithm algorithm, string approvedBy, DateTimeOffset approvedAt)
    {
        Algorithm = algorithm; ApprovedBy = approvedBy; ApprovedAt = approvedAt;
    }
}

/// <summary>Принятая рекомендация по перемещению товара (слоттинг).</summary>
public sealed class SlottingApplied
{
    private SlottingApplied() { }

    public SlottingApplied(string id, DateTimeOffset appliedAt)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new Common.DomainValidationException("Не выбраны рекомендации");
        Id = id; AppliedAt = appliedAt;
    }

    public string Id { get; private set; } = null!;
    public DateTimeOffset AppliedAt { get; private set; }

    public void Reapply(DateTimeOffset at) => AppliedAt = at;
}
