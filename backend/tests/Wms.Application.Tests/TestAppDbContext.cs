using Microsoft.EntityFrameworkCore;
using Wms.Application.Abstractions;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Infrastructure.Persistence;

namespace Wms.Application.Tests;

/// <summary>
/// In-memory IAppDbContext для unit-тестов обработчиков: те же конфигурации сущностей, что у WmsDbContext,
/// кроме Npgsql-специфичного маппинга массива перечислений.
/// </summary>
public sealed class TestAppDbContext() : DbContext(new DbContextOptionsBuilder<TestAppDbContext>()
    .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options), IAppDbContext
{
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<Contract> Contracts => Set<Contract>();
    public DbSet<ReferenceItem> References => Set<ReferenceItem>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<OrganizationSettings> Settings => Set<OrganizationSettings>();
    public DbSet<TeamInvite> TeamInvites => Set<TeamInvite>();
    public DbSet<ClientRequest> ClientRequests => Set<ClientRequest>();
    public DbSet<NotificationRead> NotificationReads => Set<NotificationRead>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(WmsDbContext).Assembly);
        modelBuilder.Entity<Organization>().Property(o => o.Marketplaces).HasConversion(
            v => string.Join(',', v!),
            v => v.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Enum.Parse<Marketplace>).ToList());
    }

    Task IUnitOfWork.SaveChangesAsync(CancellationToken ct) => SaveChangesAsync(ct);
}
