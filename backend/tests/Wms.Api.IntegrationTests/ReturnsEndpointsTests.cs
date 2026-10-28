using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Wms.Api.IntegrationTests;

public class ReturnsEndpointsTests(WmsApiFactory factory) : IClassFixture<WmsApiFactory>
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
    public async Task Fc_receives_inspects_and_closes_return()
    {
        var seller = await As("seller@demo.ru");
        var own = await Json(await seller.GetAsync("/api/returns?pageSize=200&reason=bogus"));
        var sellers = own.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("sellerId").GetString()).Distinct().ToList();
        Assert.Single(sellers);
        Assert.Equal(own.GetProperty("total").GetInt32(), (await Json(await seller.GetAsync("/api/returns/summary"))).GetProperty("total").GetInt32());

        var fc = await As("director@demo.ru");
        var page = await Json(await fc.GetAsync("/api/returns?pageSize=200&status=expected"));
        var created = page.GetProperty("items").EnumerateArray().Select(i => DateTimeOffset.Parse(i.GetProperty("createdAt").GetString()!)).ToList();
        Assert.Equal(created.OrderByDescending(d => d).ToList(), created);
        var item = page.GetProperty("items")[0];
        Assert.NotEqual("—", item.GetProperty("orderNumber").GetString());
        var id = item.GetProperty("id").GetString();

        Assert.Equal(HttpStatusCode.Forbidden, (await seller.PostAsync($"/api/returns/{id}/receive", null)).StatusCode);
        var received = await Json(await fc.PostAsync($"/api/returns/{id}/receive", null));
        Assert.EndsWith("Z", received.GetProperty("receivedAt").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await fc.PostAsync($"/api/returns/{id}/receive", null)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await fc.PostAsJsonAsync($"/api/returns/{id}/inspect", new { grades = new { } })).StatusCode);
        var grades = received.GetProperty("lines").EnumerateArray().ToDictionary(l => l.GetProperty("productId").GetString()!, _ => "as_new");
        var inspected = await Json(await fc.PostAsJsonAsync($"/api/returns/{id}/inspect", new { grades, comment = "ок" }));
        Assert.Equal("restocked", inspected.GetProperty("resolution").GetString());
        Assert.Equal("as_new", inspected.GetProperty("lines")[0].GetProperty("grade").GetString());
        var closed = await Json(await fc.PostAsync($"/api/returns/{id}/close", null));
        Assert.Equal("closed", closed.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await fc.GetAsync("/api/returns/ret_missing")).StatusCode);
    }
}
