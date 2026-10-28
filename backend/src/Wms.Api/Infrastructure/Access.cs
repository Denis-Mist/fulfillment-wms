using Wms.Application.Auth;
using Wms.Application.Common;
using Wms.Domain.Enums;

namespace Wms.Api.Infrastructure;

/// <summary>
/// Аналог withAuth() из моков: 401 → нет организации (403) → организация не активна (403) → роль (403).
/// Контекст кладётся в HttpContext.Items и достаётся эндпоинтом через <see cref="AccessExtensions.Ctx"/>.
/// </summary>
public sealed class AccessFilter(IReadOnlyCollection<Role>? roles, bool allowInactive) : IEndpointFilter
{
    private const string ItemKey = "wms.request-context";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext invocation, EndpointFilterDelegate next)
    {
        var http = invocation.HttpContext;
        var ctx = await http.RequestServices.GetRequiredService<IRequestContextAccessor>().GetAsync();
        if (ctx is null) return Reject(403, "Нет доступа к организации");
        if (ctx.OrganizationInactive && !allowInactive)
            return Reject(403, "Организация не активна", "Доступ откроется после проверки администратором.");
        if (roles is not null && !roles.Contains(ctx.Role)) return Reject(403, "Недостаточно прав");
        http.Items[ItemKey] = ctx;
        return await next(invocation);
    }

    private static IResult Reject(int status, string title, string? detail = null) =>
        Results.Problem(Problems.Create(status, title, detail));

    internal static RequestContext From(HttpContext http) =>
        http.Items[ItemKey] as RequestContext ?? throw AppException.Unauthorized();
}

public static class AccessExtensions
{
    /// <summary>Требует JWT (401 «Требуется авторизация») и доступ к организации с одной из ролей (null — любая).</summary>
    public static TBuilder RequireAccess<TBuilder>(this TBuilder builder, params Role[] roles) where TBuilder : IEndpointConventionBuilder
    {
        builder.RequireAuthorization();
        builder.AddEndpointFilter(new AccessFilter(roles.Length == 0 ? null : roles, allowInactive: false));
        return builder;
    }

    public static RequestContext Ctx(this HttpContext http) => AccessFilter.From(http);
}

/// <summary>Параметры списков — как listParams() в моках (некорректные числа → значения по умолчанию).</summary>
public static class ListQuery
{
    public static PageQuery Page(HttpRequest request)
    {
        var q = request.Query;
        int? Num(string key) => int.TryParse(q[key].FirstOrDefault(), out var v) ? v : null;
        var order = q["sortOrder"].FirstOrDefault() switch { "asc" => SortOrder.Asc, "desc" => SortOrder.Desc, _ => (SortOrder?)null };
        return new PageQuery(Num("page"), Num("pageSize"), q["search"].FirstOrDefault(), q["sortField"].FirstOrDefault(), order);
    }

    /// <summary>Фильтр-перечисление в snake_case; неизвестное значение игнорируется (фильтр не применяется).</summary>
    public static T? Enum<T>(HttpRequest request, string key) where T : struct, System.Enum
    {
        var raw = request.Query[key].FirstOrDefault();
        return !string.IsNullOrEmpty(raw) && SnakeCaseName.TryParse<T>(raw, out var value) ? value : null;
    }

    public static string? String(HttpRequest request, string key) =>
        request.Query[key].FirstOrDefault() is { Length: > 0 } s ? s : null;
}
