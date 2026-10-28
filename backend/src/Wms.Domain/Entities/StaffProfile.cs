using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

/// <summary>Кадровый профиль сотрудника ФФ: склад, зоны, навыки, график на неделю и показатели.</summary>
public sealed class StaffProfile
{
    public const int DaysInWeek = 7;

    private StaffProfile() { }

    public StaffProfile(string userId, string warehouseId, IEnumerable<string> zoneIds, IEnumerable<Skill> skills, bool active,
        int opsPerHour, double accuracy, DateTimeOffset hiredAt, IEnumerable<Shift> shifts, Role? roleOverride = null)
    {
        Id = userId;
        WarehouseId = warehouseId;
        ZoneIds = zoneIds.ToList();
        Skills = skills.ToList();
        Active = active;
        OpsPerHour = opsPerHour;
        Accuracy = accuracy;
        HiredAt = hiredAt;
        Shifts = shifts.ToList();
        RoleOverride = roleOverride;
        if (Shifts.Count != DaysInWeek) throw new DomainException("График должен содержать 7 дней");
    }

    /// <summary>Совпадает с id пользователя.</summary>
    public string Id { get; private set; } = null!;
    public string WarehouseId { get; private set; } = null!;
    public List<string> ZoneIds { get; private set; } = [];
    public List<Skill> Skills { get; private set; } = [];
    public bool Active { get; private set; }
    public Role? RoleOverride { get; private set; }
    public int OpsPerHour { get; private set; }
    public double Accuracy { get; private set; }
    public DateTimeOffset HiredAt { get; private set; }
    /// <summary>График по дням недели, 0 — понедельник.</summary>
    public List<Shift> Shifts { get; private set; } = [];

    /// <summary>Норма операций в час для роли (0 — нет нормы).</summary>
    public static int NormFor(Role role) => role switch
    {
        Role.FcReceiver => 90,
        Role.FcPicker => 60,
        Role.FcPacker => 45,
        _ => 0,
    };

    public Role EffectiveRole(Role membershipRole) => RoleOverride ?? membershipRole;

    /// <summary>На смене: дневная 8–20, ночная 20–8 (по местному времени склада).</summary>
    public bool IsOnShift(int dayOfWeek, int hour)
    {
        if (!Active) return false;
        return Shifts[dayOfWeek] switch
        {
            Shift.Day => hour is >= 8 and < 20,
            Shift.Night => hour >= 20 || hour < 8,
            _ => false,
        };
    }

    /// <summary>Изменение карточки; зоны, не принадлежащие складу, отбрасываются.</summary>
    public void Update(Role role, string warehouseId, IEnumerable<string> zoneIds, IReadOnlySet<string> warehouseZones, IEnumerable<Skill> skills, bool active)
    {
        RoleOverride = role;
        WarehouseId = warehouseId;
        ZoneIds = zoneIds.Where(warehouseZones.Contains).ToList();
        Skills = skills.ToList();
        Active = active;
    }

    public void SetShift(int day, Shift shift)
    {
        if (day is < 0 or >= DaysInWeek) throw new DomainValidationException("Некорректный день", null);
        Shifts[day] = shift;
        Shifts = [.. Shifts];
    }

    public void ShiftTimes(TimeSpan delta) => HiredAt += delta;
}
