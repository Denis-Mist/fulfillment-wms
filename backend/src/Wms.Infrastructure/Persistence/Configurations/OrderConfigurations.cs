using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Infrastructure.Common;

namespace Wms.Infrastructure.Persistence.Configurations;

/// <summary>Заказ: строки — jsonb-документ в порядке моков.</summary>
internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> b)
    {
        b.ToTable("orders");
        b.HasKey(o => o.Id);
        b.Property(o => o.Status).HasConversion(new SnakeCaseEnumConverter<OrderStatus>());
        b.Property(o => o.Channel).HasConversion(new SnakeCaseEnumConverter<SalesChannel>());
        b.HasIndex(o => o.Number).IsUnique();
        b.HasIndex(o => o.SellerId);
        b.HasIndex(o => o.WarehouseId);
        b.HasIndex(o => o.CreatedAt);
        b.OwnsMany(o => o.Lines, l => l.ToJson("lines"));
        b.Ignore(o => o.IsActive);
        b.Ignore(o => o.Units);
        b.Ignore(o => o.Amount);
        b.Ignore(o => o.CountsAsSale);
        b.Ignore(o => o.ShippedOnTime);
        SeqColumn.Add(b);
    }
}
