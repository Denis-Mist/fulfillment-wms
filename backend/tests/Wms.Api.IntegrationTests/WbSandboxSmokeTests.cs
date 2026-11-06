using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Wms.Application.Integrations;
using Wms.Domain.Enums;
using Wms.Infrastructure.Marketplaces;

namespace Wms.Api.IntegrationTests;

/// <summary>
/// Ручной smoke против настоящего sandbox WB. Без WB_TEST_TOKEN сразу выходит (в CI не падает).
/// </summary>
public class WbSandboxSmokeTests
{
    [Fact]
    [Trait("Category", "MarketplaceSmoke")]
    public async Task Ping_and_list_orders_when_token_present()
    {
        var token = Environment.GetEnvironmentVariable("WB_TEST_TOKEN");
        if (string.IsNullOrWhiteSpace(token)) return;

        var client = new WbMarketplaceClient(new HttpClient { Timeout = TimeSpan.FromSeconds(30) },
            Options.Create(new MarketplaceOptions()), NullLogger<WbMarketplaceClient>.Instance);
        var creds = new MarketplaceCredentials(token, null, MarketplaceEnvironment.Sandbox);
        await client.PingAsync(creds, default);
        var orders = await client.GetFbsOrderCountAsync(creds, default);
        var cards = await client.GetCardCountAsync(creds, default);
        Assert.True(orders >= 0);
        Assert.True(cards >= 0);
    }
}
