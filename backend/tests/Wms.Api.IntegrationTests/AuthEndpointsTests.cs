using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Wms.Api.IntegrationTests;

public class AuthEndpointsTests(WmsApiFactory factory) : IClassFixture<WmsApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private async Task<string> LoginAsync(string email)
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new { email, password = "demo" });
        response.EnsureSuccessStatusCode();
        return (await Json(response)).GetProperty("accessToken").GetString()!;
    }

    [Fact]
    public async Task Login_returns_mock_compatible_shape()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new { email = "Director@demo.ru", password = "demo" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await Json(response);
        Assert.False(string.IsNullOrEmpty(body.GetProperty("accessToken").GetString()));
        var user = body.GetProperty("user");
        Assert.Equal("usr_fc1_1", user.GetProperty("id").GetString());
        Assert.False(user.GetProperty("isPlatformAdmin").GetBoolean());
        var membership = user.GetProperty("memberships")[0];
        Assert.Equal("org_fc1", membership.GetProperty("organizationId").GetString());
        Assert.Equal("fulfillment", membership.GetProperty("organizationType").GetString());
        Assert.Equal("active", membership.GetProperty("organizationStatus").GetString());
        Assert.Equal("fc_owner", membership.GetProperty("role").GetString());
    }

    [Fact]
    public async Task Login_with_bad_password_returns_problem_details()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new { email = "director@demo.ru", password = "nope" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await Json(response);
        Assert.Equal(401, body.GetProperty("status").GetInt32());
        Assert.Equal("Неверный email или пароль", body.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Me_requires_token()
    {
        var response = await _client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Требуется авторизация", (await Json(response)).GetProperty("title").GetString());
    }

    [Fact]
    public async Task Me_returns_current_user_for_platform_admin()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await LoginAsync("admin@demo.ru"));
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await Json(response);
        Assert.True(body.GetProperty("isPlatformAdmin").GetBoolean());
        Assert.Equal(0, body.GetProperty("memberships").GetArrayLength());
    }

    [Fact]
    public async Task Register_creates_pending_org_then_conflicts_on_duplicate()
    {
        var payload = new
        {
            organizationType = "seller",
            organization = new { name = "ООО Интеграция", inn = "7700000000", city = "Москва", marketplaces = new[] { "wb" } },
            user = new { fullName = "Иван Тестов", email = "it-register@test.ru", phone = "+7 900 000-00-00", password = "secret" },
        };
        var created = await _client.PostAsJsonAsync("/api/auth/register", payload);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var membership = (await Json(created)).GetProperty("user").GetProperty("memberships")[0];
        Assert.Equal("pending", membership.GetProperty("organizationStatus").GetString());
        Assert.Equal("seller_owner", membership.GetProperty("role").GetString());

        var duplicate = await _client.PostAsJsonAsync("/api/auth/register", payload);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }
}
