using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Wms.Api.IntegrationTests;

/// <summary>
/// Серверный демо-контур упразднён: опасные эндпоинты безопасны (404 / enabled:false),
/// обычный login/me/health и сид аккаунт-менеджера работают.
/// </summary>
public class DemoEndpointsTests(WmsApiFactory factory) : IClassFixture<WmsApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    [Fact]
    public async Task Status_always_returns_enabled_false()
    {
        var response = await _client.GetAsync("/api/auth/demo/status");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False((await Json(response)).GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public async Task Demo_login_always_returns_404_and_does_not_issue_token()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/demo", new { persona = "fc_owner" });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await Json(response);
        Assert.Equal("Не найдено", body.GetProperty("title").GetString());
        Assert.False(body.TryGetProperty("accessToken", out _));
    }

    [Fact]
    public async Task Admin_demo_reset_is_gone()
    {
        var login = await _client.PostAsJsonAsync("/api/auth/login", new { email = "admin@demo.ru", password = "demo" });
        login.EnsureSuccessStatusCode();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", (await Json(login)).GetProperty("accessToken").GetString());

        var reset = await client.PostAsync("/api/admin/demo/reset", null);
        Assert.Equal(HttpStatusCode.NotFound, reset.StatusCode);
    }

    [Fact]
    public async Task Password_login_and_me_have_isDemo_false()
    {
        var login = await _client.PostAsJsonAsync("/api/auth/login", new { email = "director@demo.ru", password = "demo" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var body = await Json(login);
        Assert.False(body.GetProperty("user").GetProperty("isDemo").GetBoolean());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("accessToken").GetString()));

        var meReq = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        meReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", body.GetProperty("accessToken").GetString());
        var me = await _client.SendAsync(meReq);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.False((await Json(me)).GetProperty("isDemo").GetBoolean());
    }

    [Fact]
    public async Task Health_returns_ok()
    {
        var response = await _client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ok", (await Json(response)).GetProperty("status").GetString());
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
