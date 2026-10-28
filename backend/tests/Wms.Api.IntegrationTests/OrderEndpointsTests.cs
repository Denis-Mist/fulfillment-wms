using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Wms.Api.IntegrationTests;

public class OrderEndpointsTests(WmsApiFactory factory) : IClassFixture<WmsApiFactory>
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
    public async Task List_summary_and_details()
    {
        var fc = await As("director@demo.ru");
        var page = await Json(await fc.GetAsync("/api/orders?pageSize=20"));
        Assert.True(page.GetProperty("total").GetInt32() > 100);
        var items = page.GetProperty("items").EnumerateArray().ToList();
        var dates = items.Select(i => i.GetProperty("createdAt").GetString()!).ToList();
        Assert.All(dates, d => Assert.EndsWith("Z", d));
        Assert.Equal(dates.OrderByDescending(d => DateTimeOffset.Parse(d)).ToList(), dates);

        var summary = await Json(await fc.GetAsync("/api/orders/summary?status=bogus"));
        Assert.Equal(page.GetProperty("total").GetInt32(), summary.GetProperty("total").GetInt32());

        var overdue = await Json(await fc.GetAsync("/api/orders?status=overdue&pageSize=200"));
        Assert.All(overdue.GetProperty("items").EnumerateArray(), i => Assert.True(i.GetProperty("isOverdue").GetBoolean()));

        var id = items[0].GetProperty("id").GetString();
        var details = await Json(await fc.GetAsync($"/api/orders/{id}"));
        Assert.NotEmpty(details.GetProperty("lines").EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await fc.GetAsync("/api/orders/ord_missing")).StatusCode);
    }

    [Fact]
    public async Task Seller_sees_only_own_orders_and_stock_has_sales()
    {
        var seller = await As("seller@demo.ru");
        var fc = await As("director@demo.ru");
        var own = (await Json(await seller.GetAsync("/api/orders/summary"))).GetProperty("total").GetInt32();
        Assert.InRange(own, 1, (await Json(await fc.GetAsync("/api/orders/summary"))).GetProperty("total").GetInt32() - 1);

        var products = await Json(await seller.GetAsync("/api/stock/products?pageSize=200"));
        Assert.Contains(products.GetProperty("items").EnumerateArray(), p => p.GetProperty("avgDailySales").GetDouble() > 0
            && p.GetProperty("daysOfCover").ValueKind == JsonValueKind.Number);
    }
}
