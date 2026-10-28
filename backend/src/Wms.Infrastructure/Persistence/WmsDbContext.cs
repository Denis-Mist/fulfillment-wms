using Microsoft.EntityFrameworkCore;
using Wms.Application.Abstractions;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Persistence;

public sealed class WmsDbContext(DbContextOptions<WmsDbContext> options) : DbContext(options), IAppDbContext
{
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<Contract> Contracts => Set<Contract>();
    public DbSet<ReferenceItem> References => Set<ReferenceItem>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<OrganizationSettings> Settings => Set<OrganizationSettings>();
    public DbSet<TeamInvite> TeamInvites => Set<TeamInvite>();
    public DbSet<ClientRequest> ClientRequests => Set<ClientRequest>();
    public DbSet<NotificationRead> NotificationReads => Set<NotificationRead>();
    public DbSet<Zone> Zones => Set<Zone>();
    public DbSet<StorageLocation> Locations => Set<StorageLocation>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<StockItem> Stock => Set<StockItem>();
    public DbSet<InboundShipment> InboundShipments => Set<InboundShipment>();
    public DbSet<WarehouseTask> Tasks => Set<WarehouseTask>();
    public DbSet<StaffProfile> StaffProfiles => Set<StaffProfile>();
    public DbSet<FboShipment> FboShipments => Set<FboShipment>();
    public DbSet<CustomerReturn> Returns => Set<CustomerReturn>();
    public DbSet<Tariff> Tariffs => Set<Tariff>();
    public DbSet<BillingAct> BillingActs => Set<BillingAct>();
    public DbSet<MarketplaceIntegration> Integrations => Set<MarketplaceIntegration>();
    public DbSet<StockOperation> StockOperations => Set<StockOperation>();
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(WmsDbContext).Assembly);

    Task IUnitOfWork.SaveChangesAsync(CancellationToken ct) => SaveChangesAsync(ct);
}
