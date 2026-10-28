using Wms.Api.Infrastructure;
using Wms.Application.Tasks;
using Wms.Domain.Enums;

namespace Wms.Api.Endpoints;

/// <summary>Задания склада: диспетчерская (/tasks), комплектация (/picking) и ТСД сотрудника (/work).</summary>
public static class TaskEndpoints
{
    private static readonly Role[] Office = [Role.FcOwner, Role.FcManager, Role.FcSupervisor];

    public sealed record WarehouseBody(string? WarehouseId);
    public sealed record AssignBody(string? UserId);
    public sealed record OrderIdsBody(string[]? OrderIds, string? Algorithm);
    public sealed record ScanBody(string? Code);

    public static RouteGroupBuilder MapTaskEndpoints(this RouteGroupBuilder api)
    {
        var t = api.MapGroup("/tasks").WithTags("Tasks");
        t.MapGet("/summary", async (HttpContext http, TaskQueryHandler h, CancellationToken ct) =>
            Results.Ok(await h.SummaryAsync(http.Ctx(), ListQuery.String(http.Request, "warehouseId"), ListQuery.Enum<TaskType>(http.Request, "type"), ct)))
            .RequireAccess(Office);
        t.MapGet("/workers", async (HttpContext http, TaskQueryHandler h, CancellationToken ct) =>
            Results.Ok(await h.WorkersAsync(http.Ctx(), ListQuery.String(http.Request, "warehouseId"), ct))).RequireAccess(Office);
        t.MapGet("", async (HttpContext http, TaskQueryHandler h, CancellationToken ct) =>
            Results.Ok(await h.ListAsync(http.Ctx(), new TaskQuery(ListQuery.Page(http.Request), ListQuery.String(http.Request, "status"),
                ListQuery.Enum<TaskType>(http.Request, "type"), ListQuery.String(http.Request, "assigneeId"),
                ListQuery.String(http.Request, "warehouseId")), ct))).RequireAccess(Office);
        t.MapPost("/auto-assign", async (HttpContext http, WarehouseBody? body, TaskCommandHandler h, CancellationToken ct) =>
            Results.Ok(await h.AutoAssignAsync(http.Ctx(), body?.WarehouseId, ct))).RequireAccess(Office);
        t.MapGet("/{id}", async (HttpContext http, string id, TaskQueryHandler h, CancellationToken ct) =>
            Results.Ok(await h.DetailsAsync(http.Ctx(), id, ct))).RequireAccess();
        t.MapPost("/{id}/assign", async (HttpContext http, string id, AssignBody body, TaskCommandHandler h, CancellationToken ct) =>
            Results.Ok(await h.AssignAsync(http.Ctx(), id, body.UserId, ct))).RequireAccess(Office);
        t.MapPost("/{id}/cancel", async (HttpContext http, string id, TaskCommandHandler h, CancellationToken ct) =>
            Results.Ok(await h.CancelAsync(http.Ctx(), id, ct))).RequireAccess(Office);

        var p = api.MapGroup("/picking").WithTags("Picking").RequireAccess(Office);
        p.MapGet("/candidates", async (HttpContext http, PickingHandler h, CancellationToken ct) =>
            Results.Ok(await h.CandidatesAsync(http.Ctx(), ListQuery.String(http.Request, "warehouseId"), ct)));
        p.MapPost("/preview", async (HttpContext http, OrderIdsBody body, PickingHandler h, CancellationToken ct) =>
            Results.Ok(await h.PreviewAsync(http.Ctx(), body.OrderIds ?? [], ct)));
        p.MapPost("/batches", async (HttpContext http, OrderIdsBody body, PickingHandler h, CancellationToken ct) =>
        {
            var task = await h.CreateBatchAsync(http.Ctx(), body.OrderIds ?? [], body.Algorithm, ct);
            return Results.Created($"/api/tasks/{task.Id}", task);
        });

        var w = api.MapGroup("/work").WithTags("Work").RequireAccess();
        w.MapGet("/tasks", async (HttpContext http, WorkTerminalHandler h, CancellationToken ct) => Results.Ok(await h.ListAsync(http.Ctx(), ct)));
        w.MapPost("/tasks/{id}/start", async (HttpContext http, string id, WorkTerminalHandler h, CancellationToken ct) =>
            Results.Ok(await h.StartAsync(http.Ctx(), id, ct)));
        w.MapPost("/tasks/{id}/scan", async (HttpContext http, string id, ScanBody body, WorkTerminalHandler h, CancellationToken ct) =>
            Results.Ok(await h.ScanAsync(http.Ctx(), id, body.Code, ct)));
        w.MapPost("/tasks/{id}/complete", async (HttpContext http, string id, WorkTerminalHandler h, CancellationToken ct) =>
            Results.Ok(await h.CompleteAsync(http.Ctx(), id, ct)));
        w.MapGet("/lookup", async (HttpContext http, WorkTerminalHandler h, CancellationToken ct) =>
            Results.Ok(await h.LookupAsync(http.Ctx(), ListQuery.String(http.Request, "code"), ct)));
        return api;
    }
}
