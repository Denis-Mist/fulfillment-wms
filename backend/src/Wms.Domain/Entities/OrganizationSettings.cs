namespace Wms.Domain.Entities;

public sealed record OrgProfile(string Name, string Inn, string Kpp, string Ogrn, string LegalAddress,
    string ContactEmail, string ContactPhone, string BankName, string Bik, string Account, string CorrAccount);

public sealed record CardRules(bool RequireBarcode, bool RequireDimensions, bool RequireWeight, bool RequirePhoto,
    bool RequireExpiry, bool RequireMarking, bool AutoApprove)
{
    public static CardRules Default => new(true, true, true, false, true, true, false);
}

public sealed record NotificationPrefs(bool Email, bool InboundStatus, bool LowStock, bool SlaRisk, bool Billing)
{
    public static NotificationPrefs DefaultFor(Enums.OrganizationType type) =>
        new(true, true, true, type == Enums.OrganizationType.Fulfillment, true);
}

/// <summary>Настройки организации: реквизиты, правила карточек товара, уведомления.</summary>
public class OrganizationSettings
{
    private OrganizationSettings() { }

    public OrganizationSettings(string organizationId, OrgProfile profile, CardRules cardRules, NotificationPrefs notifications)
    {
        OrganizationId = organizationId; Profile = profile; CardRules = cardRules; Notifications = notifications;
    }

    public string OrganizationId { get; private set; } = null!;
    public OrgProfile Profile { get; private set; } = null!;
    public CardRules CardRules { get; private set; } = null!;
    public NotificationPrefs Notifications { get; private set; } = null!;

    /// <summary>ИНН из реквизитов менять нельзя — он закреплён за организацией.</summary>
    public void UpdateProfile(OrgProfile profile) => Profile = profile with { Inn = Profile.Inn };
    public void UpdateCardRules(CardRules rules) => CardRules = rules;
    public void UpdateNotifications(NotificationPrefs prefs) => Notifications = prefs;
}
