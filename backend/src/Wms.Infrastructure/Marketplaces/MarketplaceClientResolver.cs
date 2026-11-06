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
        IntegrationChannel.Ozon => new OzonSellerClient(http.CreateClient(MarketplaceClientNames.Ozon), options, logs.CreateLogger<OzonSellerClient>()),
        _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, "Неизвестный маркетплейс"),
    };
}
