using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wms.Application.Billing;
using Wms.Application.Common;

namespace Wms.Application.Demo;

public sealed class DemoResetService(
    IOptions<DemoOptions> options,
    IDemoResetGate gate,
    IDatabaseReset database,
    BillingHandler billing,
    ILogger<DemoResetService> logger) : IDemoResetService
{
    public async Task ResetAsync(CancellationToken ct)
    {
        if (!options.Value.Enabled) throw AppException.NotFound("Не найдено");
        if (!gate.TryBegin()) throw AppException.Conflict("Сброс демо-данных уже выполняется");
        try
        {
            logger.LogInformation("Начат сброс демо-данных к сиду");
            await database.TruncateBusinessDataAsync(ct);
            await database.SeedAsync(ct);
            await billing.ProvisionAsync(ct);
            logger.LogInformation("Сброс демо-данных завершён");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Сброс демо-данных не выполнен");
            throw;
        }
        finally
        {
            gate.End();
        }
    }
}
