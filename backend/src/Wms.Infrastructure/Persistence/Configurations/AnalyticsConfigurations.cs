using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Infrastructure.Common;

namespace Wms.Infrastructure.Persistence.Configurations;

internal sealed class StaffPlanConfiguration : IEntityTypeConfiguration<StaffPlan>
{
    public void Configure(EntityTypeBuilder<StaffPlan> b)
    {
        b.ToTable("staff_plans");
        b.HasKey(p => p.Id);
        b.Property(p => p.Algorithm).HasConversion(new SnakeCaseEnumConverter<ForecastAlgorithm>());
    }
}

internal sealed class SlottingAppliedConfiguration : IEntityTypeConfiguration<SlottingApplied>
{
    public void Configure(EntityTypeBuilder<SlottingApplied> b)
    {
        b.ToTable("slotting_applied");
        b.HasKey(a => a.Id);
    }
}
