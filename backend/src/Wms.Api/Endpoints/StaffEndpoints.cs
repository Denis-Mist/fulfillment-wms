using Wms.Api.Infrastructure;
using Wms.Application.Staff;
using Wms.Domain.Enums;

namespace Wms.Api.Endpoints;

/// <summary>Персонал фулфилмента (/staff).</summary>
public static class StaffEndpoints
{
    private static readonly Role[] Editors = [Role.FcOwner, Role.FcManager];
    private static readonly Role[] Managers = [Role.FcOwner, Role.FcManager, Role.FcSupervisor];

    public sealed record ShiftBody(int? Day, Shift? Shift);

    public static RouteGroupBuilder MapStaffEndpoints(this RouteGroupBuilder api)
    {
        var s = api.MapGroup("/staff").WithTags("Staff");
        s.MapGet("", async (HttpContext http, StaffHandler h, CancellationToken ct) =>
            Results.Ok(await h.ListAsync(http.Ctx(), ListQuery.String(http.Request, "warehouseId"), ct))).RequireAccess();
        s.MapPut("/{id}", async (HttpContext http, string id, StaffHandler h, CancellationToken ct) =>
            Results.Ok(await h.UpdateAsync(http.Ctx(), id, (await Body.ReadOrEmptyAsync<EmployeeInput>(http, ct)), ct))).RequireAccess(Editors);
        s.MapPost("/{id}/shifts", async (HttpContext http, string id, StaffHandler h, CancellationToken ct) =>
        {
            var body = await Body.ReadOrEmptyAsync<ShiftBody>(http, ct);
            return Results.Ok(await h.SetShiftAsync(http.Ctx(), id, body.Day, body.Shift, ct));
        }).RequireAccess(Managers);
        return api;
    }
}
