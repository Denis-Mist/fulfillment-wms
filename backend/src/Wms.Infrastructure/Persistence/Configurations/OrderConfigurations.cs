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

/// <summary>FBO-отгрузка: строки и история — jsonb-документы.</summary>
internal sealed class CustomerReturnConfiguration : IEntityTypeConfiguration<CustomerReturn>
{
    public void Configure(EntityTypeBuilder<CustomerReturn> b)
    {
        b.ToTable("customer_returns");
        b.HasKey(r => r.Id);
        b.Property(r => r.Status).HasConversion(new SnakeCaseEnumConverter<ReturnStatus>());
        b.Property(r => r.Reason).HasConversion(new SnakeCaseEnumConverter<ReturnReason>());
        b.HasIndex(r => r.Number).IsUnique();
        b.HasIndex(r => r.SellerId);
        b.HasIndex(r => r.WarehouseId);
        b.OwnsMany(r => r.Lines, l =>
        {
            l.ToJson("lines");
            l.Property(x => x.Grade).HasConversion(new SnakeCaseEnumConverter<ReturnGrade>());
            l.Ignore(x => x.Restock);
        });
        b.Ignore(r => r.Units);
        b.Ignore(r => r.Resolution);
        SeqColumn.Add(b);
    }
}

internal sealed class FboShipmentConfiguration : IEntityTypeConfiguration<FboShipment>
{
    public void Configure(EntityTypeBuilder<FboShipment> b)
    {
        b.ToTable("fbo_shipments");
        b.HasKey(r => r.Id);
        b.Property(r => r.Status).HasConversion(new SnakeCaseEnumConverter<FboStatus>());
        b.Property(r => r.Marketplace).HasConversion(new SnakeCaseEnumConverter<FboMarketplace>());
        b.HasIndex(r => r.Number).IsUnique();
        b.HasIndex(r => r.SellerId);
        b.HasIndex(r => r.WarehouseId);
        b.OwnsMany(r => r.Lines, l => l.ToJson("lines"));
        b.OwnsMany(r => r.History, h =>
        {
            h.ToJson("history");
            h.Property(x => x.Status).HasConversion(new SnakeCaseEnumConverter<FboStatus>());
        });
        b.Ignore(r => r.Units);
        b.Ignore(r => r.Packed);
        b.Ignore(r => r.Boxes);
        SeqColumn.Add(b);
    }
}
