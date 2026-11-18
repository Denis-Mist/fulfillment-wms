using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Wms.Api.Infrastructure;

namespace Wms.Api.IntegrationTests;

public class DemoRateLimitTests(DemoRateLimitApiFactory factory) : IClassFixture<DemoRateLimitApiFactory>
{
    [Fact]
    public async Task Demo_login_returns_429_after_minute_limit()
    {
        var client = factory.CreateClient();
        var codes = new List<HttpStatusCode>();
        JsonDocument? limited = null;
        for (var i = 0; i < 11; i++)
        {
            var response = await client.PostAsJsonAsync("/api/auth/demo", new { persona = "fc_owner" });
            codes.Add(response.StatusCode);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                limited = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        }

        Assert.Contains(HttpStatusCode.OK, codes);
        Assert.Contains(HttpStatusCode.TooManyRequests, codes);
        Assert.NotNull(limited);
        Assert.Equal(429, limited!.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(RateLimitingSetup.TooManyTitle, limited.RootElement.GetProperty("title").GetString());
    }
}
