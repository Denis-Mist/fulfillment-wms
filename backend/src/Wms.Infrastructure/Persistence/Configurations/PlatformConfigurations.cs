using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Infrastructure.Common;

namespace Wms.Infrastructure.Persistence.Configurations;

internal sealed class WarehouseConfiguration : IEntityTypeConfiguration<Warehouse>
{
    public void Configure(EntityTypeBuilder<Warehouse> b)
    {
        b.ToTable("warehouses");
        b.HasKey(w => w.Id);
        b.HasIndex(w => w.OrganizationId);
    }
}

internal sealed class ContractConfiguration : IEntityTypeConfiguration<Contract>
{
    public void Configure(EntityTypeBuilder<Contract> b)
    {
        b.ToTable("contracts");
        b.HasKey(c => c.Id);
        b.Ignore(c => c.Number);
        b.Property(c => c.WarehouseIds).HasColumnType("text[]");
        b.HasIndex(c => new { c.FulfillmentId, c.SellerId });
    }
}

internal sealed class ReferenceItemConfiguration : IEntityTypeConfiguration<ReferenceItem>
{
    public void Configure(EntityTypeBuilder<ReferenceItem> b)
    {
        b.ToTable("references");
        b.HasKey(r => r.Id);
        b.Property(r => r.Kind).HasConversion(new SnakeCaseEnumConverter<ReferenceKind>());
        b.HasIndex(r => new { r.Kind, r.Code }).IsUnique();
    }
}

internal sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> b)
    {
        b.ToTable("audit_entries");
        b.HasKey(a => a.Id);
        b.Property(a => a.Category).HasConversion(new SnakeCaseEnumConverter<AuditCategory>());
        b.HasIndex(a => a.At);
    }
}

internal sealed class OrganizationSettingsConfiguration : IEntityTypeConfiguration<OrganizationSettings>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<OrganizationSettings> b)
    {
        b.ToTable("organization_settings");
        b.HasKey(s => s.OrganizationId);
        Jsonb(b.Property(s => s.Profile));
        Jsonb(b.Property(s => s.CardRules));
        Jsonb(b.Property(s => s.Notifications));
    }

    /// <summary>Неизменяемые record-значения храним в jsonb; сравнение — по значению record.</summary>
    private static void Jsonb<T>(PropertyBuilder<T> p) where T : class =>
        p.HasColumnType("jsonb").HasConversion(
            v => JsonSerializer.Serialize(v, Json),
            v => JsonSerializer.Deserialize<T>(v, Json)!,
            new ValueComparer<T>((x, y) => Equals(x, y), v => v.GetHashCode(), v => v));
}

internal sealed class TeamInviteConfiguration : IEntityTypeConfiguration<TeamInvite>
{
    public void Configure(EntityTypeBuilder<TeamInvite> b)
    {
        b.ToTable("team_invites");
        b.HasKey(i => i.Id);
        b.Property(i => i.Role).HasConversion(new SnakeCaseEnumConverter<Role>());
        b.HasIndex(i => new { i.OrganizationId, i.Email });
    }
}

internal sealed class ClientRequestConfiguration : IEntityTypeConfiguration<ClientRequest>
{
    public void Configure(EntityTypeBuilder<ClientRequest> b)
    {
        b.ToTable("client_requests");
        b.HasKey(r => new { r.FulfillmentId, r.Id });
        b.Property(r => r.Status).HasConversion(new SnakeCaseEnumConverter<ClientRequestStatus>());
        b.Property(r => r.Marketplaces).HasColumnType("text[]");
        b.HasIndex(r => r.SellerId);
    }
}

internal sealed class NotificationReadConfiguration : IEntityTypeConfiguration<NotificationRead>
{
    public void Configure(EntityTypeBuilder<NotificationRead> b)
    {
        b.ToTable("notification_reads");
        b.HasKey(r => new { r.UserId, r.NotificationId });
    }
}
