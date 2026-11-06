using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wms.Application.Integrations;
using Wms.Domain.Enums;

namespace Wms.Infrastructure.Marketplaces;

public sealed class MarketplaceClientResolver(IHttpClientFactory http, IOptions<MarketplaceOptions> options, ILoggerFactory logs) : IMarketplaceClientResolver
{
    public IMarketplaceClient For(IntegrationChannel channel) => channel switch
    {
        IntegrationChannel.Wb => new WbMarketplaceClient(http.CreateClient(MarketplaceClientNames.Wb), options, logs.CreateLogger<WbMarketplaceClient>()),
        IntegrationChannel.Ozon => throw new InvalidOperationException("Клиент Ozon подключается отдельным PR."),
        _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, "Неизвестный маркетплейс"),
    };
}
