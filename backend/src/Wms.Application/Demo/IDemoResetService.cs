namespace Wms.Application.Demo;

public interface IDemoResetService
{
    Task ResetAsync(CancellationToken ct);
}
