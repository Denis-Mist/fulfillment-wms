using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Infrastructure.Common;

namespace Wms.Infrastructure.Persistence.Configurations;

internal sealed class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> b)
    {
        b.HasKey(o => o.Id);
        b.Property(o => o.Id).HasMaxLength(64);
        b.Property(o => o.Name).HasMaxLength(256).IsRequired();
        b.Property(o => o.Inn).HasMaxLength(16);
        b.Property(o => o.City).HasMaxLength(128);
        b.Property(o => o.Type).HasConversion(new SnakeCaseEnumConverter<OrganizationType>()).HasMaxLength(32);
        b.Property(o => o.Status).HasConversion(new SnakeCaseEnumConverter<OrganizationStatus>()).HasMaxLength(32);
        b.Property(o => o.Marketplaces)
            .HasConversion(
                v => v == null ? null : v.Select(SnakeCase.ToSnake).ToArray(),
                v => v == null ? null : v.Select(SnakeCase.Parse<Marketplace>).ToList(),
                new ValueComparer<List<Marketplace>?>(
                    (a, c) => (a == null && c == null) || (a != null && c != null && a.SequenceEqual(c)),
                    v => v == null ? 0 : v.Aggregate(0, (h, m) => HashCode.Combine(h, m)),
                    v => v == null ? null : v.ToList()))
            .HasColumnType("text[]");
    }
}

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.HasKey(u => u.Id);
        b.Property(u => u.Id).HasMaxLength(64);
        b.Property(u => u.Email).HasMaxLength(256).IsRequired();
        b.HasIndex(u => u.Email).IsUnique();
        b.Property(u => u.FullName).HasMaxLength(256);
        b.Property(u => u.Phone).HasMaxLength(32);
        b.HasMany(u => u.Memberships).WithOne().HasForeignKey(m => m.UserId);
        b.Navigation(u => u.Memberships).HasField("_memberships").UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
    }
}

internal sealed class MembershipConfiguration : IEntityTypeConfiguration<Membership>
{
    public void Configure(EntityTypeBuilder<Membership> b)
    {
        b.HasKey(m => new { m.UserId, m.OrganizationId });
        b.Property(m => m.Role).HasConversion(new SnakeCaseEnumConverter<Role>()).HasMaxLength(32);
        b.HasOne(m => m.Organization).WithMany().HasForeignKey(m => m.OrganizationId);
    }
}
