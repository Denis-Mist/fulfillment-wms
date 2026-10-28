using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Wms.Api.IntegrationTests;

public class FboEndpointsTests(WmsApiFactory factory) : IClassFixture<WmsApiFactory>
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
    public async Task Seller_creates_submits_and_fc_advances()
    {
        var seller = await As("seller@demo.ru");
        var page = await Json(await seller.GetAsync("/api/fbo?pageSize=200&status=bogus"));
        Assert.Equal(7, page.GetProperty("total").GetInt32());
        var planned = page.GetProperty("items").EnumerateArray().Select(i => DateTimeOffset.Parse(i.GetProperty("plannedAt").GetString()!)).ToList();
        Assert.Equal(planned.OrderByDescending(d => d).ToList(), planned);
        var summary = await Json(await seller.GetAsync("/api/fbo/summary"));
        Assert.Equal(7, summary.GetProperty("total").GetInt32());

        var option = (await Json(await seller.GetAsync("/api/fbo/stock-options"))).EnumerateArray().First();
        var input = new
        {
            marketplace = "ozon", destination = "Ozon Тверь", warehouseId = option.GetProperty("warehouseId").GetString(),
            plannedAt = DateTimeOffset.UtcNow.AddDays(3),
            lines = new[] { new { productId = option.GetProperty("productId").GetString(), quantity = 1 } },
        };
        var tooMuch = input with { lines = new[] { new { productId = input.lines[0].productId, quantity = option.GetProperty("available").GetInt32() + 1 } } };
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await seller.PostAsJsonAsync("/api/fbo", tooMuch)).StatusCode);

        var created = await seller.PostAsJsonAsync("/api/fbo", input);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await Json(created)).GetProperty("id").GetString();
        var submitted = await Json(await seller.PostAsync($"/api/fbo/{id}/submit", null));
        Assert.Equal("confirmed", submitted.GetProperty("status").GetString());
        Assert.StartsWith("OZ-", submitted.GetProperty("supplyId").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await seller.PostAsync($"/api/fbo/{id}/submit", null)).StatusCode);

        var fc = await As("director@demo.ru");
        Assert.Equal(HttpStatusCode.Forbidden, (await fc.PostAsync($"/api/fbo/{id}/cancel", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await seller.PostAsJsonAsync($"/api/fbo/{id}/advance", new { to = "assembling" })).StatusCode);
        var advanced = await Json(await fc.PostAsJsonAsync($"/api/fbo/{id}/advance", new { to = "assembling" }));
        Assert.Equal("assembling", advanced.GetProperty("status").GetString());
        Assert.EndsWith("Z", advanced.GetProperty("history")[2].GetProperty("at").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await seller.PostAsync($"/api/fbo/{id}/cancel", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await fc.GetAsync("/api/fbo/fbo_missing")).StatusCode);
    }
}
