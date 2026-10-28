using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Infrastructure.Common;

namespace Wms.Infrastructure.Persistence.Configurations;

/// <summary>Кадровый профиль: навыки и график — строки через запятую в snake_case.</summary>
internal sealed class StaffProfileConfiguration : IEntityTypeConfiguration<StaffProfile>
{
    public void Configure(EntityTypeBuilder<StaffProfile> b)
    {
        b.ToTable("staff_profiles");
        b.HasKey(p => p.Id);
        b.Property(p => p.ZoneIds).HasColumnType("text[]");
        EnumList(b.Property(p => p.Skills));
        EnumList(b.Property(p => p.Shifts));
        b.Property(p => p.RoleOverride).HasConversion(new SnakeCaseEnumConverter<Role>());
        b.HasIndex(p => p.WarehouseId);
    }

    private static void EnumList<T>(PropertyBuilder<List<T>> p) where T : struct, Enum =>
        p.HasConversion(
            v => string.Join(',', v.Select(SnakeCase.ToSnake)),
            v => v.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(SnakeCase.Parse<T>).ToList(),
            new ValueComparer<List<T>>((a, c) => a!.SequenceEqual(c!), v => v.Aggregate(0, (h, x) => HashCode.Combine(h, x)), v => v.ToList()));
}
