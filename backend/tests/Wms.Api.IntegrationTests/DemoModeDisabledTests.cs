using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Wms.Api.IntegrationTests;

public class DemoModeDisabledTests(WmsApiFactory factory) : IClassFixture<WmsApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    [Fact]
    public async Task Status_returns_enabled_false()
    {
        var response = await _client.GetAsync("/api/auth/demo/status");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False((await Json(response)).GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public async Task Demo_login_returns_404_when_disabled()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/demo", new { persona = "fc_owner" });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Не найдено", (await Json(response)).GetProperty("title").GetString());
    }

    [Fact]
    public async Task Password_login_has_isDemo_false_and_me_matches()
    {
        var login = await _client.PostAsJsonAsync("/api/auth/login", new { email = "director@demo.ru", password = "demo" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var body = await Json(login);
        Assert.False(body.GetProperty("user").GetProperty("isDemo").GetBoolean());

        var meReq = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        meReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", body.GetProperty("accessToken").GetString());
        var me = await _client.SendAsync(meReq);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.False((await Json(me)).GetProperty("isDemo").GetBoolean());
    }

    [Fact]
    public async Task Account_manager_seed_can_open_commerce_and_is_denied_dispatch()
    {
        var login = await _client.PostAsJsonAsync("/api/auth/login", new { email = "am@fc1.demo.ru", password = "demo" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var user = (await Json(login)).GetProperty("user");
        Assert.Equal("usr_fc1_12", user.GetProperty("id").GetString());
        Assert.Equal("fc_account_manager", user.GetProperty("memberships")[0].GetProperty("role").GetString());
        Assert.False(user.GetProperty("isDemo").GetBoolean());

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", (await Json(login)).GetProperty("accessToken").GetString());
        client.DefaultRequestHeaders.Add("X-Organization-Id", "org_fc1");

        foreach (var path in new[]
                 {
                     "/api/dashboard/fulfillment", "/api/inbound-shipments", "/api/returns", "/api/stock",
                     "/api/orders", "/api/fbo", "/api/fc/clients", "/api/catalog/products", "/api/billing/contracts",
                 })
        {
            var response = await client.GetAsync(path);
            Assert.True(response.IsSuccessStatusCode, $"{path} → {(int)response.StatusCode}");
        }

        foreach (var path in new[] { "/api/tasks", "/api/picking/candidates", "/api/analytics/kpi" })
        {
            var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }
}
