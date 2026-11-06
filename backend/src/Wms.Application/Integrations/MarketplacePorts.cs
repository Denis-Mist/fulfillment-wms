using Wms.Domain.Enums;

namespace Wms.Application.Integrations;

public sealed record MarketplaceCredentials(string ApiKey, string? ClientId, MarketplaceEnvironment Environment);

public sealed record MarketplaceStockItem(string Sku, int Amount);

/// <summary>Ошибка ответа HTTP-клиента маркетплейса. Handler сохраняет её в журнал и отдаёт 422/502.</summary>
public sealed class MarketplaceApiException(string message, int httpStatus, string? responseBody = null) : Exception(message)
{
    public int HttpStatus { get; } = httpStatus;
    public string? ResponseBody { get; } = responseBody;
    public bool IsClientError => HttpStatus is >= 400 and < 500;
}

/// <summary>HTTP-клиент конкретного маркетплейса. Ключи не логирует.</summary>
public interface IMarketplaceClient
{
    IntegrationChannel Channel { get; }

    Task PingAsync(MarketplaceCredentials credentials, CancellationToken ct);

    Task<int> GetFbsOrderCountAsync(MarketplaceCredentials credentials, CancellationToken ct);

    Task<int> GetCardCountAsync(MarketplaceCredentials credentials, CancellationToken ct);

    Task<int> PushStocksAsync(MarketplaceCredentials credentials, IReadOnlyList<MarketplaceStockItem> stocks, CancellationToken ct);
}

public interface IMarketplaceClientResolver
{
    IMarketplaceClient For(IntegrationChannel channel);
}

/// <summary>Шифрование ключей API перед записью в БД.</summary>
public interface ISecretProtector
{
    string Protect(string plaintext);
    string? TryUnprotect(string? ciphertext);
}

public interface IMarketplaceSettings
{
    MarketplaceEnvironment DefaultEnvironment { get; }
    bool AllowProductionStockPush { get; }
}
