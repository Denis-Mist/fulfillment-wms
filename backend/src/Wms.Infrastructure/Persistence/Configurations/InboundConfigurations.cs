using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Infrastructure.Common;

namespace Wms.Infrastructure.Persistence.Configurations;

/// <summary>Поставка: строки и история — jsonb-документы в порядке моков.</summary>
internal sealed class InboundShipmentConfiguration : IEntityTypeConfiguration<InboundShipment>
{
    public void Configure(EntityTypeBuilder<InboundShipment> b)
    {
        b.ToTable("inbound_shipments");
        b.HasKey(s => s.Id);
        b.Property(s => s.Status).HasConversion(new SnakeCaseEnumConverter<InboundStatus>());
        b.Property(s => s.PackageType).HasConversion(new SnakeCaseEnumConverter<PackageType>());
        b.HasIndex(s => s.Number).IsUnique();
        b.HasIndex(s => s.SellerId);
        b.HasIndex(s => s.WarehouseId);
        b.OwnsMany(s => s.Lines, l => l.ToJson("lines"));
        b.OwnsMany(s => s.Events, e =>
        {
            e.ToJson("events");
            e.Property(x => x.Type).HasConversion(new SnakeCaseEnumConverter<InboundEventType>());
        });
        b.Ignore(s => s.ExpectedUnits);
        b.Ignore(s => s.ReceivedUnits);
        b.Ignore(s => s.DefectUnits);
        b.Ignore(s => s.ShortageUnits);
        b.Ignore(s => s.HasDiscrepancy);
        b.Ignore(s => s.IsActive);
        SeqColumn.Add(b);
    }
}

internal sealed class WarehouseTaskConfiguration : IEntityTypeConfiguration<WarehouseTask>
{
    public void Configure(EntityTypeBuilder<WarehouseTask> b)
    {
        b.ToTable("tasks");
        b.HasKey(t => t.Id);
        b.Property(t => t.Type).HasConversion(new SnakeCaseEnumConverter<TaskType>());
        b.Property(t => t.Status).HasConversion(new SnakeCaseEnumConverter<WarehouseTaskStatus>());
        b.Property(t => t.Priority).HasConversion(new SnakeCaseEnumConverter<TaskPriority>());
        b.HasIndex(t => t.WarehouseId);
        b.HasIndex(t => t.InboundId);
        b.OwnsMany(t => t.Items, i => i.ToJson("items"));
        b.Property(t => t.OrderIds).HasColumnName("order_ids");
        b.Ignore(t => t.IsOpen);
        b.Ignore(t => t.Units);
        b.Ignore(t => t.DoneUnits);
        b.Ignore(t => t.CurrentStep);
        SeqColumn.Add(b);
    }
}

internal sealed class StockOperationConfiguration : IEntityTypeConfiguration<StockOperation>
{
    public void Configure(EntityTypeBuilder<StockOperation> b)
    {
        b.ToTable("stock_operations");
        b.HasKey(o => o.Id);
        b.Property(o => o.Type).HasConversion(new SnakeCaseEnumConverter<OperationType>());
        b.Property(o => o.Reason).HasConversion(new SnakeCaseEnumConverter<ErrorReason>());
        b.HasIndex(o => o.WarehouseId);
        b.HasIndex(o => o.At);
        SeqColumn.Add(b);
    }
}
