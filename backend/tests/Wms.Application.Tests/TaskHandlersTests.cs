using NSubstitute;
using Wms.Application.Abstractions;
using Wms.Application.Auth;
using Wms.Application.Common;
using Wms.Application.Tasks;
using Wms.Domain.Entities;
using Wms.Domain.Enums;

namespace Wms.Application.Tests;

public class TaskHandlersTests : IDisposable
{
    private readonly TestAppDbContext _db = new();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly IIdGenerator _ids = Substitute.For<IIdGenerator>();
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly RequestContext Office = new("usr_m", "org_fc", Role.FcManager, OrganizationStatus.Active, OrganizationType.Fulfillment);
    private static readonly RequestContext Picker = new("usr_p", "org_fc", Role.FcPicker, OrganizationStatus.Active, OrganizationType.Fulfillment);

    public TaskHandlersTests()
    {
        _clock.UtcNow.Returns(Now);
        _ids.NewSuffix().Returns("x1");
        var org = new Organization("org_fc", OrganizationType.Fulfillment, "ФЦ", "1", "М", OrganizationStatus.Active, Now);
        _db.Organizations.AddRange(org, new Organization("org_s", OrganizationType.Seller, "Селлер", "2", "М", OrganizationStatus.Active, Now));
        _db.Warehouses.AddRange(new Warehouse("wh_1", "org_fc", "Склад", "W1", "М", "ул.", 100, true), new Warehouse("wh_2", "org_fc", "Второй", "W2", "М", "ул.", 100, true));
        var picker = new User("usr_p", "p@x.ru", "h", "Пикер", null, false);
        picker.JoinOrganization(org, Role.FcPicker);
        var receiver = new User("usr_r", "r@x.ru", "h", "Приёмщик", null, false);
        receiver.JoinOrganization(org, Role.FcReceiver);
        _db.Users.AddRange(picker, receiver);
        _db.Zones.AddRange(new Zone("z_s", "wh_1", "STG", "Хранение", ZoneType.Storage), new Zone("z_p", "wh_1", "PAK", "Упаковка", ZoneType.Packing));
        _db.Locations.AddRange(new StorageLocation("l_a", "wh_1", "z_s", "A-01-1", 4, 3, 1, 100), new StorageLocation("l_b", "wh_1", "z_s", "B-01-1", 10, 3, 1, 100),
            new StorageLocation("l_pak", "wh_1", "z_p", "P-01", 1, 1, 1, 100));
        _db.Products.AddRange(new Product("prd_1", "org_s", "AAA-1", "Банан", "Еда", "111", null, 10, 100, 0.5, false, false),
            new Product("prd_2", "org_s", "BBB-2", "Яблоко", "Еда", "222", null, 10, 100, 0.5, false, false));
        _db.Stock.AddRange(new StockItem("stk_1", "prd_1", "org_s", "wh_1", "l_a", 10, 2), new StockItem("stk_2", "prd_2", "org_s", "wh_1", "l_b", 10, 0));
        _db.Orders.AddRange(Order("ord_1", "wh_1", new OrderLine("prd_1", 2, 1)), Order("ord_2", "wh_1", new OrderLine("prd_2", 1, 1)),
            Order("ord_3", "wh_2", new OrderLine("prd_1", 1, 1)));
        _db.SaveChanges();
    }

    private static Order Order(string id, string wh, params OrderLine[] lines) =>
        new(id, id.ToUpperInvariant(), null, SalesChannel.Wb, OrderStatus.Reserved, "org_s", wh, Now.AddHours(-1), Now.AddHours(10), null, "М", lines);

    public void Dispose() => _db.Dispose();

    private WarehouseScope Warehouses => new(_db);
    private PickingHandler Picking => new(_db, _db, Warehouses, _ids, _clock);
    private TaskCommandHandler Commands => new(_db, _db, new TaskScope(_db, Warehouses), new WorkerDirectory(_db), _clock);
    private WorkTerminalHandler Work => new(_db, _db, Warehouses, _clock);
    private TaskQueryHandler Queries => new(_db, new TaskScope(_db, Warehouses), new WorkerDirectory(_db), _clock);

    [Fact]
    public async Task Batch_validation_and_full_work_cycle()
    {
        Assert.Equal(3, (await Picking.CandidatesAsync(Office, null, default)).Count);
        var mixed = await Assert.ThrowsAsync<AppException>(() => Picking.CreateBatchAsync(Office, ["ord_1", "ord_3"], "optimized", default));
        Assert.Equal(422, mixed.Status);
        var preview = await Picking.PreviewAsync(Office, ["ord_1", "ord_2"], default);
        Assert.Equal(2, preview.StepsCount);
        Assert.Equal(3, preview.Units);

        var batch = await Picking.CreateBatchAsync(Office, ["ord_1", "ord_2"], "baseline", default);
        Assert.Equal("tsk_bx1", batch.Id);
        Assert.Equal("ORD_1, ORD_2", batch.Reference);
        Assert.Equal(["A-01-1", "B-01-1"], batch.Steps.Select(s => s.LocationCode));
        Assert.Equal(409, (await Assert.ThrowsAsync<AppException>(() => Picking.CreateBatchAsync(Office, ["ord_1"], null, default))).Status);
        Assert.Single(await Picking.CandidatesAsync(Office, null, default));

        Assert.Equal(422, (await Assert.ThrowsAsync<AppException>(() => Commands.AssignAsync(Office, batch.Id, "usr_r", default))).Status);
        Assert.Equal(1, (await Commands.AutoAssignAsync(Office, null, default)).Assigned);
        Assert.Equal("usr_p", (await Queries.DetailsAsync(Office, batch.Id, default)).AssigneeId);
        Assert.Single(await Work.ListAsync(Picker, default));

        await Work.StartAsync(Picker, batch.Id, default);
        var wrong = await Assert.ThrowsAsync<AppException>(() => Work.ScanAsync(Picker, batch.Id, "222", default));
        Assert.Equal("Не тот товар", wrong.Title);
        Assert.Equal("Ячейка A-01-1 подтверждена", (await Work.ScanAsync(Picker, batch.Id, "a-01-1", default)).Message);
        Assert.Equal("AAA-1: ещё 1 шт", (await Work.ScanAsync(Picker, batch.Id, "111", default)).Message);
        Assert.Equal("Позиция AAA-1 собрана", (await Work.ScanAsync(Picker, batch.Id, "111", default)).Message);
        Assert.Equal(422, (await Assert.ThrowsAsync<AppException>(() => Work.CompleteAsync(Picker, batch.Id, default))).Status);
        await Work.ScanAsync(Picker, batch.Id, "222", default);
        var done = await Work.CompleteAsync(Picker, batch.Id, default);
        Assert.Equal(WarehouseTaskStatus.Done, done.Status);

        Assert.All(_db.Orders.Where(o => o.Id != "ord_3"), o => Assert.Equal(OrderStatus.Picked, o.Status));
        var packing = _db.Tasks.Single(t => t.Id == "tsk_pord_1");
        Assert.Equal("l_pak", packing.Items.Single().LocationId);
        var summary = await Queries.SummaryAsync(Office, null, null, default);
        Assert.Equal(1, summary.DoneToday);
        Assert.Equal(2, summary.Unassigned);
    }

    [Fact]
    public async Task Cancel_returns_orders_and_lookup_finds_codes()
    {
        var batch = await Picking.CreateBatchAsync(Office, ["ord_1"], "optimized", default);
        var cancelled = await Commands.CancelAsync(Office, batch.Id, default);
        Assert.Equal(WarehouseTaskStatus.Cancelled, cancelled.Status);
        Assert.Equal(OrderStatus.Reserved, _db.Orders.Single(o => o.Id == "ord_1").Status);
        Assert.Equal(409, (await Assert.ThrowsAsync<AppException>(() => Commands.AssignAsync(Office, batch.Id, null, default))).Status);
        Assert.Equal(404, (await Assert.ThrowsAsync<AppException>(() => Queries.DetailsAsync(Picker, batch.Id, default))).Status);

        var location = await Work.LookupAsync(Picker, "a-01-1", default);
        Assert.Equal("Ячейка A-01-1", location.Title);
        Assert.Equal("Хранение · Склад · ярус 1", location.Subtitle);
        Assert.Equal("product", (await Work.LookupAsync(Picker, "bbb-2", default)).Kind);
        Assert.Equal(404, (await Assert.ThrowsAsync<AppException>(() => Work.LookupAsync(Picker, "zzz", default))).Status);
    }
}
