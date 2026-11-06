using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Services;

namespace Wms.Domain.Tests;

public class AssignmentPolicyTests
{
    private static AssignmentCandidate Worker(string id, Role role = Role.FcPicker, int active = 0, bool hasProfile = true,
        string? warehouseId = "wh_1", Skill[]? skills = null, bool onShift = true) =>
        new(id, role, active, hasProfile, warehouseId, skills ?? [], onShift);

    [Fact]
    public void Empty_pool_returns_null() =>
        Assert.Null(AssignmentPolicy.Select([], TaskType.Picking, "wh_1"));

    [Fact]
    public void Skips_wrong_role_full_load_and_off_shift()
    {
        var receiver = Worker("r", Role.FcReceiver, skills: [Skill.Picking]);
        var busy = Worker("b", active: WarehouseTask.MaxActivePerWorker, skills: [Skill.Picking]);
        var off = Worker("n", onShift: false, skills: [Skill.Picking]);
        Assert.Null(AssignmentPolicy.Select([receiver, busy, off], TaskType.Picking, "wh_1"));
    }

    [Fact]
    public void Worker_without_profile_is_available_even_if_marked_off_shift()
    {
        var open = Worker("p", hasProfile: false, warehouseId: null, onShift: false);
        Assert.True(AssignmentPolicy.IsAvailable(open));
        Assert.Equal("p", AssignmentPolicy.Select([open], TaskType.Picking, "wh_1")?.Id);
    }

    [Fact]
    public void Prefers_qualified_specialist_over_unqualified_or_supervisor()
    {
        var supervisor = Worker("s", Role.FcSupervisor, skills: [Skill.Picking]);
        var plain = Worker("p", hasProfile: false, warehouseId: null);
        var skilled = Worker("q", skills: [Skill.Picking]);
        Assert.Equal("q", AssignmentPolicy.Select([supervisor, plain, skilled], TaskType.Picking, "wh_1")?.Id);
    }

    [Fact]
    public void Falls_back_to_role_match_when_nobody_is_qualified()
    {
        var picker = Worker("p", hasProfile: false, warehouseId: null);
        Assert.Equal("p", AssignmentPolicy.Select([picker], TaskType.Picking, "wh_1")?.Id);
    }

    [Fact]
    public void Among_qualified_prefers_same_warehouse_then_lighter_load()
    {
        var remote = Worker("remote", warehouseId: "wh_2", skills: [Skill.Picking], active: 0);
        var localBusy = Worker("local-busy", warehouseId: "wh_1", skills: [Skill.Picking], active: 1);
        var local = Worker("local", warehouseId: "wh_1", skills: [Skill.Picking], active: 0);
        Assert.Equal("local", AssignmentPolicy.Select([remote, localBusy, local], TaskType.Picking, "wh_1")?.Id);
    }

    [Fact]
    public void Uses_qualified_from_another_warehouse_if_none_are_local()
    {
        var remote = Worker("remote", warehouseId: "wh_2", skills: [Skill.Picking]);
        Assert.Equal("remote", AssignmentPolicy.Select([remote], TaskType.Picking, "wh_1")?.Id);
    }

    [Theory]
    [InlineData(TaskType.Receiving, Skill.Receiving, true)]
    [InlineData(TaskType.Putaway, Skill.Receiving, true)]
    [InlineData(TaskType.Picking, Skill.Picking, true)]
    [InlineData(TaskType.Packing, Skill.Packing, true)]
    [InlineData(TaskType.Inventory, Skill.Inventory, true)]
    [InlineData(TaskType.Move, Skill.Forklift, true)]
    [InlineData(TaskType.Move, Skill.Picking, true)]
    [InlineData(TaskType.Picking, Skill.Packing, false)]
    public void Qualification_matches_task_type(TaskType type, Skill skill, bool expected) =>
        Assert.Equal(expected, AssignmentPolicy.HasQualification(Worker("w", skills: [skill]), type));
}
