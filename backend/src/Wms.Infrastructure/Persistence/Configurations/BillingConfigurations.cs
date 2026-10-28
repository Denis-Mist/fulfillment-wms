using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Infrastructure.Common;

namespace Wms.Infrastructure.Persistence.Configurations;

internal sealed class TariffConfiguration : IEntityTypeConfiguration<Tariff>
{
    public void Configure(EntityTypeBuilder<Tariff> b)
    {
        b.ToTable("tariffs");
        b.HasKey(t => t.ContractId);
        foreach (var p in new[] { nameof(Tariff.ReceivingPerUnit), nameof(Tariff.StoragePerM3Day), nameof(Tariff.PickPerOrder),
                     nameof(Tariff.PackPerUnit), nameof(Tariff.FboPerBox), nameof(Tariff.ReturnPerUnit) })
            b.Property<decimal>(p).HasPrecision(12, 2);
    }
}

internal sealed class BillingActConfiguration : IEntityTypeConfiguration<BillingAct>
{
    public void Configure(EntityTypeBuilder<BillingAct> b)
    {
        b.ToTable("billing_acts");
        b.HasKey(a => a.Id);
        b.Property(a => a.Status).HasConversion(new SnakeCaseEnumConverter<ActStatus>());
        b.Property(a => a.Amount).HasPrecision(14, 2);
        b.HasIndex(a => new { a.ContractId, a.Period });
        b.Ignore(a => a.DueAt);
    }
}

internal sealed class MarketplaceIntegrationConfiguration : IEntityTypeConfiguration<MarketplaceIntegration>
{
    private static readonly JsonSerializerOptions LogJson = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) } };

    public void Configure(EntityTypeBuilder<MarketplaceIntegration> b)
    {
        b.ToTable("marketplace_integrations");
        b.HasKey(i => new { i.OrganizationId, i.Channel });
        b.Property(i => i.Channel).HasConversion(new SnakeCaseEnumConverter<IntegrationChannel>());
        b.Property(i => i.Status).HasConversion(new SnakeCaseEnumConverter<IntegrationStatus>());
        // Журнал — jsonb-документ целиком (вставка в начало списка несовместима с ключами owned-коллекций EF).
        b.Property<List<IntegrationLogEntry>>("_log").HasColumnName("log").HasColumnType("jsonb")
            .HasConversion(new ValueConverter<List<IntegrationLogEntry>, string>(
                v => JsonSerializer.Serialize(v, LogJson), v => JsonSerializer.Deserialize<List<IntegrationLogEntry>>(v, LogJson) ?? new()),
                new ValueComparer<List<IntegrationLogEntry>>((x, y) => x!.SequenceEqual(y!), v => v.Aggregate(0, (h, e) => HashCode.Combine(h, e.GetHashCode())), v => v.ToList()));
        b.Ignore(i => i.Log);
    }
}
