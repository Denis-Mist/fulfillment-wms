using Microsoft.EntityFrameworkCore;
using Wms.Application.Abstractions;
using Wms.Application.Auth;
using Wms.Application.Common;
using Wms.Domain.Common;
using Wms.Domain.Entities;
using Wms.Domain.Enums;

namespace Wms.Application.Admin;

public sealed record ReferenceItemDto(string Id, ReferenceKind Kind, string Code, string Name, string? Note, bool IsActive)
{
    public static ReferenceItemDto From(ReferenceItem r) => new(r.Id, r.Kind, r.Code, r.Name, r.Note, r.IsActive);
}

public sealed record ReferenceInput(ReferenceKind Kind, string? Code, string? Name, string? Note);

public sealed class ReferencesQueryHandler(IAppDbContext db)
{
    public async Task<IReadOnlyList<ReferenceItemDto>> HandleAsync(ReferenceKind? kind, CancellationToken ct)
    {
        var query = db.References.AsNoTracking();
        if (kind is not null) query = query.Where(r => r.Kind == kind);
        return (await query.OrderBy(r => r.Position).ToListAsync(ct)).Select(ReferenceItemDto.From).ToList();
    }
}

public sealed class CreateReferenceHandler(IAppDbContext db, IAuditLog audit)
{
    public async Task<ReferenceItemDto> HandleAsync(RequestContext ctx, ReferenceInput input, CancellationToken ct)
    {
        var code = ReferenceItem.NormalizeCode(input.Code);
        ReferenceItem item;
        try
        {
            item = ReferenceItem.Create(ReferenceItem.IdFor(SnakeCaseName.Of(input.Kind), code), input.Kind, input.Code, input.Name, input.Note);
        }
        catch (DomainException e) { throw AppException.BadRequest(e.Message); }
        if (await db.References.AnyAsync(r => r.Kind == input.Kind && r.Code == code, ct))
            throw AppException.Conflict($"Код {code} уже есть в справочнике");
        item.PlaceAt(await db.References.CountAsync(ct));
        db.References.Add(item);
        await audit.AppendAsync(ctx, AuditCategory.Reference, "Добавлена запись справочника", $"{code} · {item.Name}", null, ct);
        await db.SaveChangesAsync(ct);
        return ReferenceItemDto.From(item);
    }
}

public sealed class ToggleReferenceHandler(IAppDbContext db, IAuditLog audit)
{
    public async Task<ReferenceItemDto> HandleAsync(RequestContext ctx, string id, CancellationToken ct)
    {
        var item = await db.References.FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw AppException.NotFound("Запись не найдена");
        item.Toggle();
        await audit.AppendAsync(ctx, AuditCategory.Reference,
            item.IsActive ? "Запись справочника включена" : "Запись справочника архивирована", $"{item.Code} · {item.Name}", null, ct);
        await db.SaveChangesAsync(ct);
        return ReferenceItemDto.From(item);
    }
}
