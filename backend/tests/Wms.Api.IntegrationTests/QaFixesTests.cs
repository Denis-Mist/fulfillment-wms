using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Wms.Api.IntegrationTests;

/// <summary>Регрессии по багам QA (BE-01…BE-06).</summary>
public class QaFixesTests(WmsApiFactory factory) : IClassFixture<WmsApiFactory>
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

    private static object Registration(string email, string password = "secret", string name = "ООО Тест", string fullName = "Иван Тестов") => new
    {
        organizationType = "seller",
        organization = new { name, inn = "7700000000", city = "Москва", marketplaces = new[] { "wb" } },
        user = new { fullName, email, phone = "+7 900 000-00-00", password },
    };

    private static HttpRequestMessage Raw(string method, string url, string? content) => new(new HttpMethod(method), url)
    {
        Content = content is null ? null : new StringContent(content, Encoding.UTF8, "application/json"),
    };

    private static readonly string Long = new('x', 300);

    [Fact]
    public async Task BE01_register_with_too_long_fields_returns_400_with_field_errors()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register",
            Registration($"{Long}@test.ru", name: Long, fullName: Long));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await Json(response)).GetProperty("errors");
        Assert.True(errors.TryGetProperty("organization.name", out _));
        Assert.True(errors.TryGetProperty("user.fullName", out _));
        Assert.True(errors.TryGetProperty("user.email", out _));
    }

    [Theory]
    [InlineData("not-an-email", "secret", "user.email")]
    [InlineData("ok-be02@test.ru", "1", "user.password")]
    public async Task BE02_register_validates_email_and_password(string email, string password, string field)
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/register", Registration(email, password));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await Json(response)).GetProperty("errors").TryGetProperty(field, out _));
    }

    public static TheoryData<string, string, string> WrongRole => new()
    {
        { "seller@demo.ru", "PUT", "/api/staff/x" },
        { "seller@demo.ru", "PUT", "/api/billing/tariffs/x" },
        { "director@demo.ru", "POST", "/api/fbo" },
        { "seller@demo.ru", "POST", "/api/fbo/x/advance" },
        { "seller@demo.ru", "POST", "/api/tasks/x/assign" },
        { "seller@demo.ru", "POST", "/api/picking/preview" },
        { "seller@demo.ru", "POST", "/api/picking/batches" },
        { "seller@demo.ru", "POST", "/api/returns/x/inspect" },
        { "seller@demo.ru", "POST", "/api/staff/x/shifts" },
        { "director@demo.ru", "POST", "/api/integrations/wb/connect" },
        { "director@demo.ru", "PUT", "/api/integrations/wb" },
        { "seller@demo.ru", "POST", "/api/analytics/forecast/approve" },
        { "seller@demo.ru", "POST", "/api/analytics/slotting/apply" },
        { "seller@demo.ru", "POST", "/api/admin/references" },
    };

    [Theory]
    [MemberData(nameof(WrongRole))]
    public async Task BE03_authorization_is_checked_before_body(string email, string method, string url)
    {
        var client = await As(email);
        foreach (var body in new[] { null, "{bad json" })
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(Raw(method, url, body))).StatusCode);
    }

    [Fact]
    public async Task BE03_pending_organization_gets_403_before_body()
    {
        var client = factory.CreateClient();
        var created = await client.PostAsJsonAsync("/api/auth/register", Registration("pending-be03@test.ru"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", (await Json(created)).GetProperty("accessToken").GetString());
        foreach (var (method, url) in new[] { ("POST", "/api/work/tasks/x/scan"), ("POST", "/api/fbo"), ("PUT", "/api/integrations/wb") })
        {
            var response = await client.SendAsync(Raw(method, url, "{bad json"));
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal("Организация не активна", (await Json(response)).GetProperty("title").GetString());
        }
    }

    [Fact]
    public async Task BE03_bad_json_after_authorization_is_400()
    {
        var director = await As("director@demo.ru");
        Assert.Equal(HttpStatusCode.BadRequest, (await director.SendAsync(Raw("POST", "/api/tasks/x/assign", "{bad"))).StatusCode);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData(null)]
    [InlineData("{\"orderIds\":[]}")]
    public async Task BE04_empty_batch_is_422_choose_orders(string? body)
    {
        var director = await As("director@demo.ru");
        var response = await director.SendAsync(Raw("POST", "/api/picking/batches", body));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("Выберите заказы", (await Json(response)).GetProperty("title").GetString());
    }

    [Fact]
    public async Task BE05_forecast_approve_with_empty_body_uses_default_algorithm()
    {
        var director = await As("director@demo.ru");
        var response = await director.SendAsync(Raw("POST", "/api/analytics/forecast/approve", "{}"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("holt_winters", (await Json(response)).GetProperty("algorithm").GetString());
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await director.PostAsJsonAsync("/api/analytics/forecast/approve", new { algorithm = "bogus" })).StatusCode);
    }

    [Fact]
    public async Task BE06_long_strings_are_rejected_with_field_errors()
    {
        var director = await As("director@demo.ru");
        var seller = await As("seller@demo.ru");
        var cases = new (HttpClient Client, string Method, string Url, object Body, string Field)[]
        {
            (seller, "POST", "/api/catalog/products", new { name = Long, sku = "SKU-BE06" }, "name"),
            (director, "POST", "/api/warehouses/wh_pdl1/zones", new { name = Long, code = "Z9" }, "name"),
            (director, "PUT", "/api/settings/profile", new { legalAddress = Long }, "legalAddress"),
            (director, "POST", "/api/settings/team/invite", new { email = $"{Long}@test.ru", role = "fc_manager" }, "email"),
        };
        foreach (var (client, method, url, body, field) in cases)
        {
            var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url) { Content = JsonContent.Create(body) });
            Assert.True(response.StatusCode == HttpStatusCode.UnprocessableEntity, $"{url}: {(int)response.StatusCode}");
            Assert.True((await Json(response)).GetProperty("errors").TryGetProperty(field, out _), url);
        }
        // свободный текст допускает больше
        var ok = await director.PostAsJsonAsync("/api/analytics/forecast/approve", new { algorithm = "moving_average", comment = Long });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }
}
