using Microsoft.EntityFrameworkCore;
using Wms.Application.Abstractions;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Persistence;

public sealed class WmsDbContext(DbContextOptions<WmsDbContext> options) : DbContext(options), IUnitOfWork
{
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Membership> Memberships => Set<Membership>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(WmsDbContext).Assembly);

    Task IUnitOfWork.SaveChangesAsync(CancellationToken ct) => SaveChangesAsync(ct);
}
