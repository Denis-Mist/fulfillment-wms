using Microsoft.EntityFrameworkCore;
using Wms.Application.Abstractions;
using Wms.Application.Auth;
using Wms.Application.Common;
using Wms.Domain.Common;
using Wms.Domain.Entities;
using Wms.Domain.Enums;

namespace Wms.Application.Staff;

public sealed record EmployeeDto(string Id, string FullName, string Email, string? Phone, Role Role, string WarehouseId, string WarehouseName,
    IReadOnlyList<string> ZoneIds, IReadOnlyList<Skill> Skills, bool Active, bool OnShift, int OpsPerHour, int Norm, double Accuracy,
    DateTimeOffset HiredAt, IReadOnlyList<Shift> Shifts);

public sealed record StaffResponseDto(DateTimeOffset WeekStart, IReadOnlyList<EmployeeDto> Items);

public sealed record EmployeeInput(Role Role, string? WarehouseId, IReadOnlyList<string>? ZoneIds, IReadOnlyList<Skill>? Skills, bool Active);

/// <summary>Персонал фулфилмента: список с графиком, редактирование карточки и смен.</summary>
public sealed class StaffHandler(IAppDbContext db, IUnitOfWork uow, IClock clock)
{
    public async Task<StaffResponseDto> ListAsync(RequestContext ctx, string? warehouseId, CancellationToken ct)
    {
        var orgId = RequireFulfillment(ctx, () => AppException.Forbidden("Недоступно"));
        var members = await MembersAsync(orgId, ct);
        var profiles = await db.StaffProfiles.AsNoTracking().Where(p => members.Keys.Contains(p.Id)).ToListAsync(ct);
        var names = await WarehouseNamesAsync(ct);
        var now = clock.UtcNow;
        var items = profiles.Where(p => string.IsNullOrEmpty(warehouseId) || p.WarehouseId == warehouseId)
            .Select(p => ToDto(p, members[p.Id], names, now))
            .OrderBy(e => e.Role.ToString(), StringComparer.Ordinal)
            .ThenBy(e => e.FullName, StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("ru-RU"), false))
            .ToList();
        return new StaffResponseDto(WarehouseTime.WeekStart(now), items);
    }

    public async Task<EmployeeDto> UpdateAsync(RequestContext ctx, string id, EmployeeInput input, CancellationToken ct)
    {
        var (profile, member) = await OwnAsync(ctx, id, ct);
        if (string.IsNullOrEmpty(input.WarehouseId)
            || !await db.Warehouses.AnyAsync(w => w.Id == input.WarehouseId && w.OrganizationId == ctx.OrganizationId, ct))
            throw AppException.Unprocessable("Выберите склад");
        if (profile.Id == ctx.UserId && !input.Active) throw AppException.Unprocessable("Нельзя деактивировать себя");
        var zones = (await db.Zones.AsNoTracking().Where(z => z.WarehouseId == input.WarehouseId).Select(z => z.Id).ToListAsync(ct)).ToHashSet();
        profile.Update(input.Role, input.WarehouseId, input.ZoneIds ?? [], zones, input.Skills ?? [], input.Active);
        await uow.SaveChangesAsync(ct);
        return ToDto(profile, member, await WarehouseNamesAsync(ct), clock.UtcNow);
    }

    public async Task<EmployeeDto> SetShiftAsync(RequestContext ctx, string id, int? day, Shift? shift, CancellationToken ct)
    {
        var (profile, member) = await OwnAsync(ctx, id, ct);
        if (day is null || shift is null) throw AppException.Unprocessable("Некорректный день");
        try { profile.SetShift(day.Value, shift.Value); }
        catch (DomainValidationException e) { throw AppException.Unprocessable(e.Message, e.Detail); }
        await uow.SaveChangesAsync(ct);
        return ToDto(profile, member, await WarehouseNamesAsync(ct), clock.UtcNow);
    }

    private static EmployeeDto ToDto(StaffProfile p, (User User, Role Role) m, IReadOnlyDictionary<string, string> warehouses, DateTimeOffset now)
    {
        var role = p.EffectiveRole(m.Role);
        return new EmployeeDto(p.Id, m.User.FullName, m.User.Email, m.User.Phone, role, p.WarehouseId, warehouses.GetValueOrDefault(p.WarehouseId) ?? string.Empty,
            p.ZoneIds, p.Skills, p.Active, p.IsOnShift(WarehouseTime.DayIndex(now), WarehouseTime.Local(now).Hour), p.OpsPerHour,
            StaffProfile.NormFor(role), p.Accuracy, p.HiredAt, p.Shifts);
    }

    private async Task<(StaffProfile, (User, Role))> OwnAsync(RequestContext ctx, string id, CancellationToken ct)
    {
        static AppException NotFound() => AppException.NotFound("Сотрудник не найден");
        var orgId = RequireFulfillment(ctx, NotFound);
        var members = await MembersAsync(orgId, ct);
        if (!members.TryGetValue(id, out var member)) throw NotFound();
        var profile = await db.StaffProfiles.FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw NotFound();
        return (profile, member);
    }

    private static string RequireFulfillment(RequestContext ctx, Func<AppException> error) =>
        ctx.IsFulfillment && ctx.OrganizationId is { } orgId ? orgId : throw error();

    private async Task<Dictionary<string, (User User, Role Role)>> MembersAsync(string orgId, CancellationToken ct)
    {
        var users = await db.Users.AsNoTracking().Where(u => u.Memberships.Any(m => m.OrganizationId == orgId)).ToListAsync(ct);
        return users.ToDictionary(u => u.Id, u => (u, u.Memberships.First(m => m.OrganizationId == orgId).Role));
    }

    private Task<Dictionary<string, string>> WarehouseNamesAsync(CancellationToken ct) =>
        db.Warehouses.AsNoTracking().ToDictionaryAsync(w => w.Id, w => w.Name, ct);
}
