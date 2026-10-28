using Wms.Domain.Common;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Services;

namespace Wms.Domain.Tests;

public class InboundTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private sealed class FixedInspector(int shortage, int defect) : IReceivingInspector
    {
        public ReceivingResult Inspect(InboundLine line) => new(line.ReceivedQty == 0 ? line.ExpectedQty - shortage : line.ReceivedQty, defect);
    }

    private static InboundShipment Shipment(InboundStatus status) => new("inb_1", "ПС-00001", status, "org_s", "wh_1", Now, Now,
        PackageType.Boxes, 2, null, null, null,
        [new InboundLine("inb_1_ln1", "prd_1", 10), new InboundLine("inb_1_ln2", "prd_2", 5)], []);

    private static readonly InboundLocations Locations = new("rcv_1", ["str_1", "str_2", "str_3"]);

    [Theory]
    [InlineData(InboundStatus.InTransit, InboundAction.Arrive, InboundStatus.Arrived)]
    [InlineData(InboundStatus.Arrived, InboundAction.StartReceiving, InboundStatus.Receiving)]
    [InlineData(InboundStatus.Received, InboundAction.CompletePlacement, InboundStatus.Placed)]
    [InlineData(InboundStatus.Draft, InboundAction.Cancel, InboundStatus.Cancelled)]
    [InlineData(InboundStatus.InTransit, InboundAction.Cancel, InboundStatus.Cancelled)]
    public void Allowed_transitions_change_status_and_log_event(InboundStatus from, InboundAction action, InboundStatus to)
    {
        var s = Shipment(from);
        s.Apply(action, "Иван", Now);
        Assert.Equal(to, s.Status);
        var e = Assert.Single(s.Events);
        Assert.Equal("Иван", e.ActorName);
        Assert.StartsWith("inb_1_e1_", e.Id);
    }

    [Theory]
    [InlineData(InboundStatus.Draft, InboundAction.Arrive)]
    [InlineData(InboundStatus.Arrived, InboundAction.Cancel)]
    [InlineData(InboundStatus.Placed, InboundAction.CompletePlacement)]
    public void Forbidden_transitions_throw_conflict(InboundStatus from, InboundAction action)
    {
        var s = Shipment(from);
        Assert.False(s.CanApply(action));
        Assert.Throws<DomainConflictException>(() => s.Apply(action, "Иван", Now));
    }

    [Fact]
    public void Complete_receiving_records_discrepancies()
    {
        var s = Shipment(InboundStatus.Receiving);
        s.Apply(InboundAction.CompleteReceiving, "Иван", Now, new FixedInspector(1, 1));
        Assert.Equal(13, s.ReceivedUnits);
        Assert.True(s.HasDiscrepancy);
        Assert.Equal(["Принято 13 из 15 шт", "недостача 2 шт, брак 2 шт"], s.Events.Select(e => e.Description));
        Assert.Equal(InboundEventType.Discrepancy, s.Events[1].Type);
    }

    [Fact]
    public void Clean_receiving_has_no_discrepancy_event()
    {
        var s = Shipment(InboundStatus.Receiving);
        s.Apply(InboundAction.CompleteReceiving, "Иван", Now, new FixedInspector(0, 0));
        Assert.False(s.HasDiscrepancy);
        Assert.Single(s.Events);
    }

    [Fact]
    public void Task_planner_follows_receiving_lifecycle()
    {
        var s = Shipment(InboundStatus.InTransit);
        var tasks = new List<WarehouseTask>();
        var seq = 0;
        (string, string) Next() { seq++; return ($"tsk_{seq}", WarehouseTask.NumberFor(seq)); }
        void Step(InboundAction a, IReceivingInspector? i = null)
        {
            s.Apply(a, "Иван", Now, i);
            tasks.AddRange(InboundTaskPlanner.Plan(s, a, tasks, Locations, "org_fc", Now, Next));
        }

        Step(InboundAction.Arrive);
        var receiving = Assert.Single(tasks);
        Assert.Equal((TaskType.Receiving, WarehouseTaskStatus.New, "T-100007"), (receiving.Type, receiving.Status, receiving.Number));
        Assert.All(receiving.Items, i => Assert.Equal("rcv_1", i.LocationId));
        Assert.Equal([10, 5], receiving.Items.Select(i => i.Quantity));

        Step(InboundAction.StartReceiving);
        Assert.Equal(WarehouseTaskStatus.InProgress, receiving.Status);

        Step(InboundAction.CompleteReceiving, new FixedInspector(0, 1));
        Assert.Equal(WarehouseTaskStatus.Done, receiving.Status);
        var putaway = tasks.Single(t => t.Type == TaskType.Putaway);
        Assert.Equal(["str_1", "str_2"], putaway.Items.Select(i => i.LocationId)); // (idx·7) mod 3
        Assert.Equal([9, 4], putaway.Items.Select(i => i.Quantity));

        Step(InboundAction.CompletePlacement);
        Assert.Equal(WarehouseTaskStatus.Done, putaway.Status);
        Assert.Equal(2, tasks.Count);
    }

    [Fact]
    public void Placement_adds_good_units_to_existing_stock_or_creates_rows()
    {
        var s = Shipment(InboundStatus.Receiving);
        s.Apply(InboundAction.CompleteReceiving, "Иван", Now, new FixedInspector(0, 2));
        var existing = new StockItem("stk_1", "prd_1", "org_s", "wh_1", "loc_x", 100, 5);
        var created = InboundPlacement.Place(s, [existing], Locations, p => $"stk_{p}");
        Assert.Equal(108, existing.Quantity);
        var row = Assert.Single(created);
        Assert.Equal(("stk_prd_2", "str_2", 3, 0), (row.Id, row.LocationId, row.Quantity, row.Reserved));
    }

    [Fact]
    public void Closed_task_cannot_restart()
    {
        var t = new WarehouseTask("t", "T-1", TaskType.Putaway, "wh", "org", Now, [new TaskItem("p", "l", 3)]);
        t.Complete(Now);
        Assert.Equal(3, t.Items[0].Progress);
        Assert.Throws<DomainConflictException>(() => t.Start(Now));
    }
}
