using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Infrastructure.Common;

namespace Wms.Infrastructure.Persistence.Configurations;

/// <summary>Теневой порядковый номер: списки отдаются в порядке вставки, как массивы моков.</summary>
internal static class SeqColumn
{
    public const string Name = "Seq";

    public static void Add<T>(EntityTypeBuilder<T> b) where T : class
    {
        b.Property<long>(Name).HasColumnName("seq").UseIdentityByDefaultColumn();
        b.HasIndex(Name);
    }
}

internal sealed class ZoneConfiguration : IEntityTypeConfiguration<Zone>
{
    public void Configure(EntityTypeBuilder<Zone> b)
    {
        b.ToTable("zones");
        b.HasKey(z => z.Id);
        b.Property(z => z.Type).HasConversion(new SnakeCaseEnumConverter<ZoneType>());
        b.Ignore(z => z.AllowsStorage);
        b.HasIndex(z => new { z.WarehouseId, z.Code }).IsUnique();
        SeqColumn.Add(b);
    }
}

internal sealed class StorageLocationConfiguration : IEntityTypeConfiguration<StorageLocation>
{
    public void Configure(EntityTypeBuilder<StorageLocation> b)
    {
        b.ToTable("locations");
        b.HasKey(l => l.Id);
        b.Ignore(l => l.Aisle);
        b.Ignore(l => l.Rack);
        b.HasIndex(l => new { l.WarehouseId, l.Code }).IsUnique();
        b.HasIndex(l => l.ZoneId);
        SeqColumn.Add(b);
    }
}

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> b)
    {
        b.ToTable("products");
        b.HasKey(p => p.Id);
        b.HasIndex(p => p.SellerId);
        SeqColumn.Add(b);
    }
}

internal sealed class StockItemConfiguration : IEntityTypeConfiguration<StockItem>
{
    public void Configure(EntityTypeBuilder<StockItem> b)
    {
        b.ToTable("stock");
        b.HasKey(s => s.Id);
        b.HasIndex(s => s.WarehouseId);
        b.HasIndex(s => s.ProductId);
        b.HasIndex(s => s.LocationId);
        SeqColumn.Add(b);
    }
}
