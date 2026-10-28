using Wms.Api.Infrastructure;
using Wms.Application.Billing;
using Wms.Domain.Enums;

namespace Wms.Api.Endpoints;

/// <summary>Биллинг (/billing): договоры, сводка, начисления, акты и тарифы.</summary>
public static class BillingEndpoints
{
    private static readonly Role[] Finance = [Role.FcOwner, Role.FcManager, Role.FcAccountManager];
    private static readonly Role[] TariffEditors = [Role.FcOwner, Role.FcManager];

    public static RouteGroupBuilder MapBillingEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/billing").WithTags("Billing");
        g.MapGet("/contracts", async (HttpContext http, BillingHandler h, CancellationToken ct) =>
            Results.Ok(await h.ContractsAsync(http.Ctx(), ct))).RequireAccess();
        g.MapGet("/summary", async (HttpContext http, BillingHandler h, CancellationToken ct) =>
            Results.Ok(await h.SummaryAsync(http.Ctx(), ListQuery.String(http.Request, "contractId"), ct))).RequireAccess();
        g.MapGet("/charges", async (HttpContext http, BillingHandler h, CancellationToken ct) =>
            Results.Ok(await h.ChargesPageAsync(http.Ctx(), new ChargeQuery(ListQuery.Page(http.Request), ListQuery.String(http.Request, "contractId"),
                ListQuery.Enum<BillingService>(http.Request, "service")), ct))).RequireAccess();
        g.MapGet("/acts", async (HttpContext http, BillingHandler h, CancellationToken ct) =>
            Results.Ok(await h.ActsAsync(http.Ctx(), ListQuery.String(http.Request, "contractId"), ct))).RequireAccess();
        g.MapPost("/acts/{id}/issue", async (HttpContext http, string id, BillingHandler h, CancellationToken ct) =>
            Results.Ok(await h.IssueAsync(http.Ctx(), id, ct))).RequireAccess(Finance);
        g.MapPost("/acts/{id}/pay", async (HttpContext http, string id, BillingHandler h, CancellationToken ct) =>
            Results.Ok(await h.PayAsync(http.Ctx(), id, ct))).RequireAccess(Finance);
        g.MapGet("/tariffs/{id}", async (HttpContext http, string id, BillingHandler h, CancellationToken ct) =>
            Results.Ok(await h.TariffAsync(http.Ctx(), id, ct))).RequireAccess();
        g.MapPut("/tariffs/{id}", async (HttpContext http, string id, TariffInput body, BillingHandler h, CancellationToken ct) =>
            Results.Ok(await h.UpdateTariffAsync(http.Ctx(), id, body, ct))).RequireAccess(TariffEditors);
        return api;
    }
}
