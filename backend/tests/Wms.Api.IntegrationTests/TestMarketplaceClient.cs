using Wms.Application.Integrations;
using Wms.Domain.Enums;

namespace Wms.Api.IntegrationTests;

/// <summary>Заглушка HTTP-клиентов маркетплейсов: интеграционные тесты API не ходят в сеть.</summary>
internal sealed class TestMarketplaceClientResolver : IMarketplaceClientResolver
{
    public IMarketplaceClient For(IntegrationChannel channel) => new TestMarketplaceClient(channel);
}

internal sealed class TestMarketplaceClient(IntegrationChannel channel) : IMarketplaceClient
{
    public IntegrationChannel Channel { get; } = channel;

    public Task PingAsync(MarketplaceCredentials credentials, CancellationToken ct) => Task.CompletedTask;

    public Task<int> GetFbsOrderCountAsync(MarketplaceCredentials credentials, CancellationToken ct) => Task.FromResult(3);

    public Task<int> GetCardCountAsync(MarketplaceCredentials credentials, CancellationToken ct) => Task.FromResult(5);

    public Task<int> PushStocksAsync(MarketplaceCredentials credentials, IReadOnlyList<MarketplaceStockItem> stocks, CancellationToken ct) =>
        Task.FromResult(stocks.Count);
}
