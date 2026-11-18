using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Wms.Application.Demo;

namespace Wms.Api.IntegrationTests;

public class DemoModeTests(DemoApiFactory factory) : IClassFixture<DemoApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private async Task<(string Token, JsonElement User)> DemoLoginAsync(string persona)
    {
        var response = await _client.PostAsJsonAsync("/api/auth/demo", new { persona });
        response.EnsureSuccessStatusCode();
        var body = await Json(response);
        return (body.GetProperty("accessToken").GetString()!, body.GetProperty("user"));
    }

    private HttpClient As(string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task Status_returns_enabled_true()
    {
        var response = await _client.GetAsync("/api/auth/demo/status");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True((await Json(response)).GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public async Task Unknown_persona_returns_422()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/demo", new { persona = "wizard" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("Неизвестная персона", (await Json(response)).GetProperty("title").GetString());
    }

    [Theory]
    [InlineData("platform_admin", "usr_admin", true, null, null)]
    [InlineData("fc_owner", "usr_fc1_1", false, "org_fc1", "fc_owner")]
    [InlineData("fc_manager", "usr_fc1_2", false, "org_fc1", "fc_manager")]
    [InlineData("fc_supervisor", "usr_fc1_11", false, "org_fc1", "fc_supervisor")]
    [InlineData("fc_account_manager", "usr_fc1_12", false, "org_fc1", "fc_account_manager")]
    [InlineData("fc_receiver", "usr_fc1_4", false, "org_fc1", "fc_receiver")]
    [InlineData("fc_picker", "usr_fc1_3", false, "org_fc1", "fc_picker")]
    [InlineData("fc_packer", "usr_fc1_9", false, "org_fc1", "fc_packer")]
    [InlineData("seller_owner", "usr_s1_1", false, "org_s1", "seller_owner")]
    [InlineData("seller_manager", "usr_s1_2", false, "org_s1", "seller_manager")]
    [InlineData("pending", "usr_p1", false, "org_p1", "fc_owner")]
    public async Task Persona_logs_into_canonical_seed_user(string persona, string userId, bool admin, string? orgId, string? role)
    {
        var (token, user) = await DemoLoginAsync(persona);
        Assert.Equal(userId, user.GetProperty("id").GetString());
        Assert.True(user.GetProperty("isDemo").GetBoolean());
        Assert.Equal(admin, user.GetProperty("isPlatformAdmin").GetBoolean());
        if (orgId is null)
        {
            Assert.Equal(0, user.GetProperty("memberships").GetArrayLength());
        }
        else
        {
            var membership = user.GetProperty("memberships")[0];
            Assert.Equal(orgId, membership.GetProperty("organizationId").GetString());
            Assert.Equal(role, membership.GetProperty("role").GetString());
            if (persona == "pending")
                Assert.Equal("pending", membership.GetProperty("organizationStatus").GetString());
        }

        var payload = JwtPayload(token);
        Assert.Equal(DemoClaims.Value, payload.GetProperty(DemoClaims.Type).GetString());
        var lifetime = payload.GetProperty("exp").GetInt64() - payload.GetProperty("nbf").GetInt64();
        Assert.InRange(lifetime, 119 * 60, 121 * 60);

        var me = await As(token).GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        var meBody = await Json(me);
        Assert.True(meBody.GetProperty("isDemo").GetBoolean());
        Assert.Equal(userId, meBody.GetProperty("id").GetString());
    }

    [Fact]
    public async Task Demo_token_is_blocked_on_stop_list_but_allows_other_mutations()
    {
        var (adminToken, _) = await DemoLoginAsync("platform_admin");
        var blocked = await As(adminToken).PostAsJsonAsync("/api/admin/users/usr_fc1_1/reset-password", new { });
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
        Assert.Equal(DemoStopList.ForbiddenTitle, (await Json(blocked)).GetProperty("title").GetString());

        var reset = await As(adminToken).PostAsync("/api/admin/demo/reset", null);
        Assert.Equal(HttpStatusCode.Forbidden, reset.StatusCode);
        Assert.Equal(DemoStopList.ForbiddenTitle, (await Json(reset)).GetProperty("title").GetString());

        var (directorToken, _) = await DemoLoginAsync("fc_owner");
        var client = As(directorToken);
        client.DefaultRequestHeaders.Add("X-Organization-Id", "org_fc1");
        var allowed = await client.PostAsync("/api/notifications/read-all", null);
        Assert.Equal(HttpStatusCode.NoContent, allowed.StatusCode);
    }

    [Fact]
    public async Task Password_login_is_not_restricted_by_stop_list()
    {
        var login = await _client.PostAsJsonAsync("/api/auth/login", new { email = "admin@demo.ru", password = "demo" });
        login.EnsureSuccessStatusCode();
        var token = (await Json(login)).GetProperty("accessToken").GetString();
        var client = As(token!);
        var response = await client.PostAsJsonAsync("/api/admin/users/usr_missing/reset-password", new { });
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        if (response.StatusCode == HttpStatusCode.Forbidden)
            Assert.NotEqual(DemoStopList.ForbiddenTitle, (await Json(response)).GetProperty("title").GetString());
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Demo_picker_can_start_assigned_task()
    {
        var (token, _) = await DemoLoginAsync("fc_picker");
        var client = As(token);
        client.DefaultRequestHeaders.Add("X-Organization-Id", "org_fc1");
        var tasks = (await Json(await client.GetAsync("/api/work/tasks"))).EnumerateArray().ToList();
        Assert.NotEmpty(tasks);
        var id = tasks[0].GetProperty("id").GetString();
        var started = await client.PostAsync($"/api/work/tasks/{id}/start", null);
        Assert.True(started.IsSuccessStatusCode || started.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity,
            $"start → {(int)started.StatusCode}");
        Assert.NotEqual(HttpStatusCode.Forbidden, started.StatusCode);
    }

    private static JsonElement JwtPayload(string token)
    {
        var part = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
        part = part.PadRight((part.Length + 3) / 4 * 4, '=');
        return JsonDocument.Parse(Convert.FromBase64String(part)).RootElement;
    }
}
