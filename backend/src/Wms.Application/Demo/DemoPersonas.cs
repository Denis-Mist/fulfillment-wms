namespace Wms.Application.Demo;

/// <summary>Канонические сид-пользователи персон демо-режима (не вторая пачка учёток).</summary>
public static class DemoPersonas
{
    public static readonly IReadOnlyDictionary<string, string> Emails = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["platform_admin"] = "admin@demo.ru",
        ["fc_owner"] = "director@demo.ru",
        ["fc_manager"] = "manager@demo.ru",
        ["fc_supervisor"] = "staff8@fc1.demo.ru",
        ["fc_account_manager"] = "am@fc1.demo.ru",
        ["fc_receiver"] = "staff1@fc1.demo.ru",
        ["fc_picker"] = "picker@demo.ru",
        ["fc_packer"] = "staff6@fc1.demo.ru",
        ["seller_owner"] = "seller@demo.ru",
        ["seller_manager"] = "manager@ud.demo.ru",
        ["pending"] = "owner1@pending.demo.ru",
    };

    public static bool TryGetEmail(string? persona, out string email)
    {
        email = "";
        if (string.IsNullOrWhiteSpace(persona)) return false;
        return Emails.TryGetValue(persona.Trim(), out email!);
    }
}
