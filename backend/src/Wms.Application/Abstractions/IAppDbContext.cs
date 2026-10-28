using Microsoft.EntityFrameworkCore;
using Wms.Domain.Entities;

namespace Wms.Application.Abstractions;

/// <summary>
/// Порт хранилища для сценариев чтения/модерации. Реализуется в Infrastructure (WmsDbContext);
/// Application видит только наборы сущностей и единицу работы, без провайдера БД.
/// </summary>
public interface IAppDbContext : IUnitOfWork
{
    DbSet<Organization> Organizations { get; }
    DbSet<User> Users { get; }
    DbSet<Warehouse> Warehouses { get; }
    DbSet<Contract> Contracts { get; }
    DbSet<ReferenceItem> References { get; }
    DbSet<AuditEntry> AuditEntries { get; }
    DbSet<OrganizationSettings> Settings { get; }
    DbSet<TeamInvite> TeamInvites { get; }
    DbSet<ClientRequest> ClientRequests { get; }
    DbSet<NotificationRead> NotificationReads { get; }
    DbSet<Zone> Zones { get; }
    DbSet<StorageLocation> Locations { get; }
    DbSet<Product> Products { get; }
    DbSet<StockItem> Stock { get; }
    DbSet<InboundShipment> InboundShipments { get; }
    DbSet<WarehouseTask> Tasks { get; }
    DbSet<StaffProfile> StaffProfiles { get; }
    DbSet<FboShipment> FboShipments { get; }
    DbSet<CustomerReturn> Returns { get; }
    DbSet<Tariff> Tariffs { get; }
    DbSet<BillingAct> BillingActs { get; }
    DbSet<MarketplaceIntegration> Integrations { get; }
    DbSet<StockOperation> StockOperations { get; }
    DbSet<Order> Orders { get; }
}
