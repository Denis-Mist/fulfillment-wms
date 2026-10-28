using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Wms.Application.Abstractions;
using Wms.Application.Auth;
using Wms.Application.Common;
using Wms.Domain.Entities;
using Wms.Domain.Enums;

namespace Wms.Application.Settings;

public sealed record OrgSettingsDto(string Id, OrgProfile Profile, CardRules CardRules, NotificationPrefs Notifications)
{
    public static OrgSettingsDto From(OrganizationSettings s) => new(s.OrganizationId, s.Profile, s.CardRules, s.Notifications);
}

public static class SettingsRoles
{
    /// <summary>OWNERS в моках: могут менять реквизиты и команду.</summary>
    public static readonly Role[] Owners = [Role.FcOwner, Role.FcManager, Role.SellerOwner];
    public static readonly Role[] CardRuleEditors = [Role.FcOwner, Role.FcManager];
}

/// <summary>Проверка реквизитов (сообщения — как в моках, HTTP 422).</summary>
public interface IOrgProfileValidator
{
    void Validate(OrgProfile? profile);
}

public sealed partial class OrgProfileValidator : IOrgProfileValidator
{
    [GeneratedRegex(@"^\d{9}$")] private static partial Regex Bik();
    [GeneratedRegex(@"^\d{20}$")] private static partial Regex Account20();
    [GeneratedRegex(@"^\S+@\S+\.\S+$")] private static partial Regex Email();

    public void Validate(OrgProfile? p)
    {
        if (p is null || string.IsNullOrWhiteSpace(p.Name)) throw AppException.Unprocessable("Укажите название организации");
        if (!Bik().IsMatch(p.Bik ?? "")) throw AppException.Unprocessable("БИК должен состоять из 9 цифр");
        if (!Account20().IsMatch(p.Account ?? "")) throw AppException.Unprocessable("Расчётный счёт должен состоять из 20 цифр");
        if (!Account20().IsMatch(p.CorrAccount ?? "")) throw AppException.Unprocessable("Корр. счёт должен состоять из 20 цифр");
        if (!Email().IsMatch(p.ContactEmail ?? "")) throw AppException.Unprocessable("Некорректный email");
    }
}

public sealed class OrgSettingsHandler(IAppDbContext db, IOrgProfileValidator validator)
{
    public async Task<OrgSettingsDto> GetAsync(RequestContext ctx, CancellationToken ct) =>
        OrgSettingsDto.From(await LoadAsync(ctx, tracked: false, ct));

    public Task<OrgSettingsDto> UpdateProfileAsync(RequestContext ctx, OrgProfile? profile, CancellationToken ct)
    {
        validator.Validate(profile);
        return UpdateAsync(ctx, s => s.UpdateProfile(profile!), ct);
    }

    public Task<OrgSettingsDto> UpdateCardRulesAsync(RequestContext ctx, CardRules? rules, CancellationToken ct) =>
        UpdateAsync(ctx, s => s.UpdateCardRules(rules ?? throw AppException.BadRequest("Некорректный запрос")), ct);

    public Task<OrgSettingsDto> UpdateNotificationsAsync(RequestContext ctx, NotificationPrefs? prefs, CancellationToken ct) =>
        UpdateAsync(ctx, s => s.UpdateNotifications(prefs ?? throw AppException.BadRequest("Некорректный запрос")), ct);

    private async Task<OrgSettingsDto> UpdateAsync(RequestContext ctx, Action<OrganizationSettings> change, CancellationToken ct)
    {
        var settings = await LoadAsync(ctx, tracked: true, ct);
        change(settings);
        await db.SaveChangesAsync(ct);
        return OrgSettingsDto.From(settings);
    }

    private async Task<OrganizationSettings> LoadAsync(RequestContext ctx, bool tracked, CancellationToken ct)
    {
        var query = tracked ? db.Settings : db.Settings.AsNoTracking();
        return (ctx.OrganizationId is null ? null : await query.FirstOrDefaultAsync(s => s.OrganizationId == ctx.OrganizationId, ct))
            ?? throw AppException.NotFound("Организация не найдена");
    }
}

/// <summary>Настройки по умолчанию для новой организации (реквизиты из регистрации).</summary>
public static class DefaultSettings
{
    public static OrganizationSettings For(Organization o) => new(o.Id,
        new OrgProfile(o.Name, o.Inn, o.Inn.Length == 10 ? $"{o.Inn[..4]}01001" : "", "", "", "", "", "", "", "", ""),
        CardRules.Default, NotificationPrefs.DefaultFor(o.Type));
}
