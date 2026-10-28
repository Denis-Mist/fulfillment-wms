using Microsoft.EntityFrameworkCore;
using Wms.Application.Abstractions;
using Wms.Application.Auth;
using Wms.Application.Common;
using Wms.Domain.Common;
using Wms.Domain.Entities;
using Wms.Domain.Enums;

namespace Wms.Application.Clients;

public sealed record ClientRequestDto(string Id, string CompanyName, string Inn, string City, string ContactName,
    string ContactPhone, int SkuEstimate, int OrdersPerDay, IReadOnlyList<string> Marketplaces, string Message,
    DateTimeOffset CreatedAt, ClientRequestStatus Status, DateTimeOffset? DecidedAt, string? SellerId)
{
    public static ClientRequestDto From(ClientRequest r) => new(r.Id, r.CompanyName, r.Inn, r.City, r.ContactName,
        r.ContactPhone, r.SkuEstimate, r.OrdersPerDay, r.Marketplaces, r.Message, r.CreatedAt, r.Status, r.DecidedAt, r.SellerId);
}

public sealed record DecideClientRequestInput(IReadOnlyList<string>? WarehouseIds);

public sealed class ClientRequestsQueryHandler(IAppDbContext db)
{
    public async Task<IReadOnlyList<ClientRequestDto>> HandleAsync(RequestContext ctx, CancellationToken ct)
    {
        if (!ctx.IsFulfillment) return [];
        return (await db.ClientRequests.AsNoTracking().Where(r => r.FulfillmentId == ctx.OrganizationId)
                .OrderByDescending(r => r.CreatedAt).ToListAsync(ct))
            .Select(ClientRequestDto.From).ToList();
    }
}

/// <summary>Решение по заявке: accept (с выбором своих складов) или decline.</summary>
public sealed class DecideClientRequestHandler(IAppDbContext db, IClock clock)
{
    public static readonly Role[] Roles = [Role.FcOwner, Role.FcAccountManager];

    public async Task<ClientRequestDto> HandleAsync(RequestContext ctx, string id, string decision,
        DecideClientRequestInput? input, CancellationToken ct)
    {
        if (!ctx.IsFulfillment) throw AppException.InsufficientRights();
        var request = await db.ClientRequests.FirstOrDefaultAsync(r => r.FulfillmentId == ctx.OrganizationId && r.Id == id, ct)
            ?? throw AppException.NotFound("Заявка не найдена");
        if (request.Status != ClientRequestStatus.Pending) throw AppException.Conflict("Заявка уже рассмотрена");
        try
        {
            if (decision == "accept")
            {
                var own = await db.Warehouses.Where(w => w.OrganizationId == ctx.OrganizationId).Select(w => w.Id).ToListAsync(ct);
                if (input?.WarehouseIds is not { Count: > 0 } ids || ids.Any(w => !own.Contains(w)))
                    throw AppException.Unprocessable("Выберите склады для договора");
                request.Accept(clock.UtcNow);
            }
            else if (decision == "decline") request.Decline(clock.UtcNow);
            else throw AppException.NotFound("Неизвестное действие");
        }
        catch (DomainConflictException e) { throw AppException.Conflict(e.Message); }
        await db.SaveChangesAsync(ct);
        return ClientRequestDto.From(request);
    }
}
