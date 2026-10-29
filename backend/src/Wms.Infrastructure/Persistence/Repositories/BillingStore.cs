using Microsoft.EntityFrameworkCore;
using Wms.Application.Abstractions;
using Wms.Application.Common;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Persistence.Repositories;

/// <summary>PostgreSQL: INSERT … ON CONFLICT DO NOTHING — повторные и конкурентные вставки не дают 23505.</summary>
internal sealed class BillingStore(WmsDbContext db) : IBillingStore
{
    public async Task AddMissingAsync(IReadOnlyCollection<Tariff> tariffs, IReadOnlyCollection<BillingAct> acts, CancellationToken ct)
    {
        foreach (var t in tariffs)
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO tariffs (contract_id, receiving_per_unit, storage_per_m3day, pick_per_order, pack_per_unit, fbo_per_box, return_per_unit)
                VALUES ({t.ContractId}, {t.ReceivingPerUnit}, {t.StoragePerM3Day}, {t.PickPerOrder}, {t.PackPerUnit}, {t.FboPerBox}, {t.ReturnPerUnit})
                ON CONFLICT DO NOTHING
                """, ct);
        foreach (var a in acts) await InsertActAsync(a, ct);
    }

    public async Task<bool> TryAddActAsync(BillingAct act, CancellationToken ct) => await InsertActAsync(act, ct) > 0;

    private Task<int> InsertActAsync(BillingAct a, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO billing_acts (id, contract_id, period, amount, status, issued_at, paid_at)
            VALUES ({a.Id}, {a.ContractId}, {a.Period}, {a.Amount}, {SnakeCaseName.Of(a.Status)}, {a.IssuedAt}, {a.PaidAt})
            ON CONFLICT DO NOTHING
            """, ct);
}
