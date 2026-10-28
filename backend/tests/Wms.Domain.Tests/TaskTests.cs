using Wms.Domain.Common;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Services;

namespace Wms.Domain.Tests;

public class TaskTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private static WarehouseTask Picking(params TaskItem[] items) =>
        WarehouseTask.PickingBatch("t1", "T-1", "wh", "org", Now, TaskPriority.Normal, ["o1"], "optimized", items);

    [Fact]
    public void Route_optimized_is_not_longer_than_baseline()
    {
        var nodes = new[] { new RouteNode("C-1", 30, 0), new RouteNode("A-1", 1, 10), new RouteNode("B-1", 29, 10), new RouteNode("A-2", 2, 0) };
        var baseline = RoutePlanner.Baseline(nodes, n => n);
        Assert.Equal(["A-1", "A-2", "B-1", "C-1"], baseline.Select(n => n.Code));
        Assert.True(RoutePlanner.Length(RoutePlanner.Optimized(nodes, n => n)) <= RoutePlanner.Length(baseline));
        Assert.Equal(20, RoutePlanner.Length([new RouteNode("X", 5, 5)]));
    }

    [Fact]
    public void Pick_list_merges_lines_and_falls_back_to_pick_zone()
    {
        var o1 = new Order("o1", "1", null, SalesChannel.Wb, OrderStatus.Reserved, "s", "wh", Now, Now, null, "М",
            [new OrderLine("p1", 2, 1), new OrderLine("p2", 1, 1)]);
        var o2 = new Order("o2", "2", null, SalesChannel.Wb, OrderStatus.Reserved, "s", "wh", Now, Now, null, "М", [new OrderLine("p1", 3, 1)]);
        var items = PickListBuilder.Build([o1, o2], [new StockSlot("p1", "wh", "L0", 0), new StockSlot("p1", "wh", "L1", 5)], _ => "PCK1");
        Assert.Equal(2, items.Count);
        Assert.Equal(("p1", "L1", 5), (items[0].ProductId, items[0].LocationId, items[0].Quantity));
        Assert.Equal("PCK1", items[1].LocationId);
        Assert.All(PickListBuilder.ForPacking(items, "PAK"), i => Assert.Equal("PAK", i.LocationId));
    }

    [Fact]
    public void Work_flow_scan_and_complete()
    {
        var task = Picking(new TaskItem("p1", "L1", 2));
        Assert.Throws<DomainConflictException>(() => task.ScanUnit());
        task.Assign("u1");
        Assert.Equal(WarehouseTaskStatus.Assigned, task.Status);
        task.StartWork(Now);
        Assert.Throws<DomainConflictException>(() => task.Assign(null));
        task.ScanUnit();
        Assert.Throws<DomainValidationException>(() => task.CompleteWork(Now));
        Assert.Equal(1, task.DoneUnits);
        task.ScanUnit();
        Assert.Null(task.CurrentStep);
        task.CompleteWork(Now.AddMinutes(5));
        Assert.Equal(WarehouseTaskStatus.Done, task.Status);
        Assert.Throws<DomainConflictException>(() => task.CancelByOffice());
    }

    [Fact]
    public void Unassign_returns_to_new_and_priority_depends_on_ship_by()
    {
        var task = Picking(new TaskItem("p1", "L1", 1));
        task.Assign("u1");
        task.Assign(null);
        Assert.Equal(WarehouseTaskStatus.New, task.Status);
        var urgent = new Order("o", "1", null, SalesChannel.Wb, OrderStatus.Reserved, "s", "wh", Now, Now.AddHours(2), null, "М", [new OrderLine("p", 1, 1)]);
        Assert.Equal(TaskPriority.High, TaskRules.PriorityFor([urgent], Now));
        Assert.Contains(Role.FcSupervisor, TaskRules.EligibleRoles(TaskType.Picking));
    }
}
