using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Wms.Api.IntegrationTests;

public class InboundEndpointsTests(WmsApiFactory factory) : IClassFixture<WmsApiFactory>
{
    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private async Task<HttpClient> As(string email)
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "demo" });
        login.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", (await Json(login)).GetProperty("accessToken").GetString());
        return client;
    }

    [Fact]
    public async Task Seller_sees_own_shipments_summary_and_details()
    {
        var seller = await As("seller@demo.ru");
        var list = await Json(await seller.GetAsync("/api/inbound-shipments?pageSize=5&status=bogus"));
        Assert.Equal(59, list.GetProperty("total").GetInt32());
        var summary = await Json(await seller.GetAsync("/api/inbound-shipments/summary?status=placed"));
        Assert.Equal(59, summary.GetProperty("total").GetInt32());
        Assert.True(summary.GetProperty("byStatus").TryGetProperty("in_transit", out _));
        var details = await Json(await seller.GetAsync("/api/inbound-shipments/inb_1021"));
        Assert.Equal("in_transit", details.GetProperty("status").GetString());
        Assert.True(details.GetProperty("lines").GetArrayLength() > 0);
        Assert.EndsWith("Z", details.GetProperty("expectedAt").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await seller.GetAsync("/api/inbound-shipments/inb_1120")).StatusCode);
    }

    [Fact]
    public async Task Action_errors_match_mock()
    {
        var seller = await As("seller@demo.ru");
        Assert.Equal(HttpStatusCode.Forbidden, (await seller.PostAsync("/api/inbound-shipments/inb_1059/actions/arrive", null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await seller.PostAsync("/api/inbound-shipments/inb_1059/actions/fly", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await seller.PostAsync("/api/inbound-shipments/nope/actions/arrive", null)).StatusCode);
        var cancelled = await seller.PostAsync("/api/inbound-shipments/inb_1040/actions/cancel", null);
        Assert.Equal("cancelled", (await Json(cancelled)).GetProperty("status").GetString());
        var conflict = await seller.PostAsync("/api/inbound-shipments/inb_1040/actions/cancel", null);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
    }

    [Fact]
    public async Task Director_runs_full_receiving_flow()
    {
        var fc = await As("director@demo.ru");
        var before = (await Json(await fc.GetAsync("/api/inbound-shipments/summary"))).GetProperty("byStatus").GetProperty("placed").GetInt32();
        JsonElement last = default;
        foreach (var action in new[] { "arrive", "start_receiving", "complete_receiving", "complete_placement" })
        {
            var response = await fc.PostAsync($"/api/inbound-shipments/inb_1222/actions/{action}", null);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            last = await Json(response);
        }
        Assert.Equal("placed", last.GetProperty("status").GetString());
        Assert.True(last.GetProperty("events").GetArrayLength() >= 4);
        var after = (await Json(await fc.GetAsync("/api/inbound-shipments/summary"))).GetProperty("byStatus").GetProperty("placed").GetInt32();
        Assert.Equal(before + 1, after);
    }
}
