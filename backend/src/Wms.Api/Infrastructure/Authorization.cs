using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Wms.Application.Auth;
using Wms.Domain.Enums;

namespace Wms.Api.Infrastructure;

public static class Policies
{
    public const string PlatformAdmin = nameof(PlatformAdmin);
    public const string Fulfillment = nameof(Fulfillment);
    public const string FulfillmentManagement = nameof(FulfillmentManagement);
    public const string Worker = nameof(Worker);
    public const string Seller = nameof(Seller);
    public const string AnyMember = nameof(AnyMember);

    public static readonly IReadOnlyDictionary<string, Role[]> Roles = new Dictionary<string, Role[]>
    {
        [PlatformAdmin] = [Role.PlatformAdmin],
        [Fulfillment] = [.. RoleRules.FulfillmentRoles],
        [FulfillmentManagement] = [Role.FcOwner, Role.FcManager, Role.FcSupervisor, Role.FcAccountManager],
        [Worker] = [.. RoleRules.WorkerRoles, Role.FcSupervisor],
        [Seller] = [.. RoleRules.SellerRoles],
        [AnyMember] = Enum.GetValues<Role>(),
    };

    public static void Register(AuthorizationOptions options)
    {
        foreach (var (name, roles) in Roles)
            options.AddPolicy(name, p => p.RequireAuthenticatedUser().AddRequirements(new RoleRequirement(roles)));
    }
}

public sealed record RoleRequirement(IReadOnlyCollection<Role> Roles) : IAuthorizationRequirement;

/// <summary>Контекст запроса (пользователь + организация из X-Organization-Id), кешируется на запрос.</summary>
public interface IRequestContextAccessor
{
    Task<RequestContext?> GetAsync();
}

public sealed class HttpRequestContextAccessor(IHttpContextAccessor http, IRequestContextResolver resolver) : IRequestContextAccessor
{
    public const string OrganizationHeader = "X-Organization-Id";
    private Task<RequestContext?>? _cached;

    public Task<RequestContext?> GetAsync() => _cached ??= ResolveAsync();

    private Task<RequestContext?> ResolveAsync()
    {
        var context = http.HttpContext;
        var userId = context?.User.FindFirstValue("sub");
        if (context is null || userId is null) return Task.FromResult<RequestContext?>(null);
        return resolver.ResolveAsync(userId, context.Request.Headers[OrganizationHeader].FirstOrDefault(), context.RequestAborted);
    }
}

public sealed class RoleRequirementHandler(IRequestContextAccessor accessor) : AuthorizationHandler<RoleRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, RoleRequirement requirement)
    {
        var ctx = await accessor.GetAsync();
        if (ctx is not null && requirement.Roles.Contains(ctx.Role)) context.Succeed(requirement);
    }
}

/// <summary>401/403 от авторизации — в формате ProblemDetails.</summary>
public sealed class ProblemDetailsAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult result)
    {
        if (result.Challenged) return Problems.WriteAsync(context, 401, "Требуется авторизация");
        if (result.Forbidden) return Problems.WriteAsync(context, 403, "Нет доступа к организации");
        return _default.HandleAsync(next, context, policy, result);
    }
}
