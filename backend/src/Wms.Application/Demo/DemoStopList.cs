namespace Wms.Application.Demo;

/// <summary>
/// Стоп-лист мутаций для JWT с claim <c>demo=true</c>.
/// Обычный логин по паролю этот список не применяет.
/// </summary>
public static class DemoStopList
{
    private static readonly Rule[] Rules =
    [
        new("POST", ["api", "admin", "organizations", "*", "*"], ["approve", "reject", "block", "unblock"]),
        new("POST", ["api", "admin", "users", "*", "*"], ["reset-password", "block", "unblock"]),
        new("POST", ["api", "admin", "references"], null),
        new("POST", ["api", "admin", "references", "*", "toggle"], null),
        new("POST", ["api", "admin", "demo", "reset"], null),
        new("POST", ["api", "integrations", "*", "connect"], null),
        new("POST", ["api", "integrations", "*", "disconnect"], null),
        new("PUT", ["api", "integrations", "*"], null),
        new("POST", ["api", "integrations", "*", "sync"], null),
        new("PUT", ["api", "settings", "profile"], null),
        new("POST", ["api", "settings", "team", "invite"], null),
        new("POST", ["api", "settings", "team", "*", "revoke"], null),
        new("POST", ["api", "fc", "client-requests", "*", "*"], null),
        new("POST", ["api", "seller", "fulfillments", "*", "connect"], null),
        new("PUT", ["api", "billing", "tariffs", "*"], null),
        new("POST", ["api", "warehouses", "*", "zones"], null),
        new("POST", ["api", "warehouses", "*", "locations"], null),
        new("PUT", ["api", "staff", "*"], null),
        new("POST", ["api", "staff", "*", "shifts"], null),
        new("POST", ["api", "catalog", "products", "*", "archive"], null),
        new("POST", ["api", "analytics", "slotting", "apply"], null),
        new("POST", ["api", "stock", "writeoff"], null),
        new("POST", ["api", "tasks", "*", "cancel"], null),
    ];

    public const string ForbiddenTitle = "В демо-режиме это действие недоступно";

    public static bool IsBlocked(string method, string? path)
    {
        if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(method)) return false;
        var segments = Split(path);
        foreach (var rule in Rules)
        {
            if (!method.Equals(rule.Method, StringComparison.OrdinalIgnoreCase)) continue;
            if (Matches(rule.Segments, segments, rule.LastActions)) return true;
        }
        return false;
    }

    private static string[] Split(string path)
    {
        var trimmed = path.Trim();
        if (trimmed.Length == 0) return [];
        return trimmed.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
    }

    private static bool Matches(string[] pattern, string[] path, string[]? lastActions)
    {
        if (pattern.Length != path.Length) return false;
        for (var i = 0; i < pattern.Length; i++)
        {
            if (pattern[i] == "*") continue;
            if (!pattern[i].Equals(path[i], StringComparison.OrdinalIgnoreCase)) return false;
        }
        if (lastActions is null) return true;
        return lastActions.Any(a => a.Equals(path[^1], StringComparison.OrdinalIgnoreCase));
    }

    private readonly record struct Rule(string Method, string[] Segments, string[]? LastActions);
}
