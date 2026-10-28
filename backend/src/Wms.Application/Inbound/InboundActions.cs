using Microsoft.EntityFrameworkCore;
using Wms.Application.Abstractions;
using Wms.Application.Auth;
using Wms.Application.Common;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Services;

namespace Wms.Application.Inbound;

public static class InboundRoles
{
    /// <summary>FC_OFFICE_ROLES из моков.</summary>
    public static readonly Role[] FcOffice = [Role.FcOwner, Role.FcManager, Role.FcSupervisor, Role.FcAccountManager];
}

/// <summary>Порт генератора расхождений приёмки (Domain → Application) с источником случайности.</summary>
public interface IReceivingInspectorFactory
{
    IReceivingInspector Create(DateTimeOffset now);
}

/// <summary>Как applyAction() в моках: недостача 1–4 шт с вероятностью 15 %, брак 1–2 шт с вероятностью 8 %.</summary>
public sealed class RandomReceivingInspectorFactory : IReceivingInspectorFactory
{
    public IReceivingInspector Create(DateTimeOffset now) => new Inspector(new Mulberry32((uint)(now.ToUnixTimeMilliseconds() & 0xffffffff)));

    private sealed class Inspector(Mulberry32 r) : IReceivingInspector
    {
        public ReceivingResult Inspect(InboundLine line)
        {
            var received = line.ReceivedQty == 0 ? line.ExpectedQty - (r.Bool(0.15) ? r.Int(1, 4) : 0) : line.ReceivedQty;
            return new ReceivingResult(received, r.Bool(0.08) ? r.Int(1, 2) : 0);
        }
    }
}

/// <summary>Переходы статусов поставки, генерация заданий и зачисление остатков при размещении.</summary>
public sealed class InboundActionHandler(IAppDbContext db, InboundScope scope, IReceivingInspectorFactory inspectors, IClock clock, IIdGenerator ids)
{
    public async Task<InboundShipmentDto> HandleAsync(RequestContext ctx, string id, string actionName, CancellationToken ct)
    {
        var shipment = await db.InboundShipments.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (shipment is null || !await scope.CanSeeAsync(ctx, shipment, ct)) throw AppException.NotFound("Поставка не найдена");
        if (!SnakeCaseName.TryParse<InboundAction>(actionName, out var action)) throw AppException.BadRequest("Неизвестное действие");
        var allowed = InboundShipment.IsSellerAction(action) ? [.. InboundRoles.FcOffice, .. RoleRules.SellerRoles] : InboundRoles.FcOffice;
        if (!allowed.Contains(ctx.Role)) throw new AppException(403, "Недостаточно прав для этого действия");
        if (!shipment.CanApply(action)) throw AppException.Conflict("Действие недоступно", "Статус поставки изменился. Обновите страницу.");

        var now = clock.UtcNow;
        var actor = await db.Users.AsNoTracking().Where(u => u.Id == ctx.UserId).Select(u => u.FullName).FirstOrDefaultAsync(ct) ?? "—";
        shipment.Apply(action, actor, now, action == InboundAction.CompleteReceiving ? inspectors.Create(now) : null);

        var locations = await LocationsAsync(shipment.WarehouseId, ct);
        var warehouse = await db.Warehouses.AsNoTracking().FirstAsync(w => w.Id == shipment.WarehouseId, ct);
        var tasks = await db.Tasks.Where(t => t.InboundId == shipment.Id).ToListAsync(ct);
        var taskCount = await db.Tasks.LongCountAsync(ct);
        db.Tasks.AddRange(InboundTaskPlanner.Plan(shipment, action, tasks, locations, warehouse.OrganizationId, now,
            () => { taskCount++; return ($"tsk_{ids.NewSuffix()}", WarehouseTask.NumberFor(taskCount)); }));

        if (action == InboundAction.CompletePlacement)
        {
            var stock = await db.Stock.Where(s => s.WarehouseId == shipment.WarehouseId).OrderBy(s => EF.Property<long>(s, "Seq")).ToListAsync(ct);
            db.Stock.AddRange(InboundPlacement.Place(shipment, stock, locations, productId => $"stk_{productId}_{shipment.WarehouseId}_{ids.NewSuffix()}"));
        }

        await db.SaveChangesAsync(ct);
        return (await InboundLookups.LoadAsync(db, withProducts: true, ct)).ToDetails(shipment);
    }

    private async Task<InboundLocations> LocationsAsync(string warehouseId, CancellationToken ct)
    {
        var zones = await db.Zones.AsNoTracking().Where(z => z.WarehouseId == warehouseId &&
            (z.Code == InboundLocations.ReceivingZoneCode || z.Code == InboundLocations.StorageZoneCode)).ToListAsync(ct);
        async Task<List<string>> Of(string code)
        {
            var zone = zones.FirstOrDefault(z => z.Code == code);
            if (zone is null) return [];
            return await db.Locations.AsNoTracking().Where(l => l.ZoneId == zone.Id)
                .OrderBy(l => EF.Property<long>(l, "Seq")).Select(l => l.Id).ToListAsync(ct);
        }
        return new InboundLocations((await Of(InboundLocations.ReceivingZoneCode)).FirstOrDefault(), await Of(InboundLocations.StorageZoneCode));
    }
}

/// <summary>События поставок в журнале администратора (как в adminPlatform моков).</summary>
public sealed class InboundAuditSource(IAppDbContext db) : IAuditEventSource
{
    public async Task<IReadOnlyList<AuditEventDto>> GetEventsAsync(DateTimeOffset now, CancellationToken ct)
    {
        var owners = await db.Warehouses.AsNoTracking().ToDictionaryAsync(w => w.Id, w => w.OrganizationId, ct);
        var names = await db.Organizations.AsNoTracking().ToDictionaryAsync(o => o.Id, o => o.Name, ct);
        var shipments = await db.InboundShipments.AsNoTracking().OrderBy(s => EF.Property<long>(s, "Seq")).ToListAsync(ct);
        return shipments.SelectMany(s =>
        {
            var fc = owners.GetValueOrDefault(s.WarehouseId);
            return s.Events.Select(e => new AuditEventDto($"aud_{e.Id}", e.At, AuditCategory.Inbound, e.Title, e.Description, e.ActorName,
                fc, fc is null ? null : names.GetValueOrDefault(fc), s.Number));
        }).ToList();
    }
}
