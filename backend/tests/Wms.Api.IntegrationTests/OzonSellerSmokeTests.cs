using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Wms.Application.Integrations;
using Wms.Domain.Enums;
using Wms.Infrastructure.Marketplaces;

namespace Wms.Api.IntegrationTests;

/// <summary>
/// Ручной smoke против настоящего Ozon Seller API. Без OZON_CLIENT_ID и OZON_API_KEY сразу выходит.
/// Только чтение: /v1/roles, список товаров и отправлений. Остатки не обновляет.
/// </summary>
public class OzonSellerSmokeTests
{
    [Fact]
    [Trait("Category", "MarketplaceSmoke")]
    public async Task Ping_and_list_when_keys_present()
    {
        var clientId = Environment.GetEnvironmentVariable("OZON_CLIENT_ID");
        var apiKey = Environment.GetEnvironmentVariable("OZON_API_KEY");
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(apiKey)) return;

        var client = new OzonSellerClient(new HttpClient { Timeout = TimeSpan.FromSeconds(30) },
            Options.Create(new MarketplaceOptions()), NullLogger<OzonSellerClient>.Instance);
        var creds = new MarketplaceCredentials(apiKey, clientId, MarketplaceEnvironment.Production);
        await client.PingAsync(creds, default);
        var cards = await client.GetCardCountAsync(creds, default);
        var orders = await client.GetFbsOrderCountAsync(creds, default);
        Assert.True(cards >= 0);
        Assert.True(orders >= 0);
    }
}
