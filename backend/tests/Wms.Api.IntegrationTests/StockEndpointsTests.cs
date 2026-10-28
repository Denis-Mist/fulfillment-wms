using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Wms.Api.IntegrationTests;

public class StockEndpointsTests(WmsApiFactory factory) : IClassFixture<WmsApiFactory>
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

    private static async Task<int> ProductTotal(HttpClient client, string warehouseId, string productId, string sku)
    {
        var page = await Json(await client.GetAsync($"/api/stock?warehouseId={warehouseId}&search={Uri.EscapeDataString(sku)}&pageSize=200"));
        Assert.True(page.GetProperty("total").GetInt32() <= 200);
        return page.GetProperty("items").EnumerateArray().Where(i => i.GetProperty("productId").GetString() == productId)
            .Sum(i => i.GetProperty("quantity").GetInt32());
    }

    [Fact]
    public async Task Placement_increases_stock_via_api()
    {
        var fc = await As("director@demo.ru");
        var shipment = await Json(await fc.GetAsync("/api/inbound-shipments/inb_1021"));
        var warehouseId = shipment.GetProperty("warehouseId").GetString()!;
        var lines = shipment.GetProperty("lines").EnumerateArray().ToList();
        var productId = lines[0].GetProperty("productId").GetString()!;
        var sku = lines[0].GetProperty("sku").GetString()!;
        var before = await ProductTotal(fc, warehouseId, productId, sku);

        JsonElement placed = default;
        foreach (var action in new[] { "arrive", "start_receiving", "complete_receiving", "complete_placement" })
        {
            var response = await fc.PostAsync($"/api/inbound-shipments/inb_1021/actions/{action}", null);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            placed = await Json(response);
        }
        var line = placed.GetProperty("lines").EnumerateArray().Where(l => l.GetProperty("productId").GetString() == productId)
            .Sum(l => l.GetProperty("receivedQty").GetInt32() - l.GetProperty("defectQty").GetInt32());
        Assert.True(line > 0);
        Assert.Equal(before + line, await ProductTotal(fc, warehouseId, productId, sku));
    }

    [Fact]
    public async Task Summary_products_operations_and_scoping()
    {
        var seller = await As("seller@demo.ru");
        var summary = await Json(await seller.GetAsync("/api/stock/summary"));
        Assert.True(summary.GetProperty("quantity").GetInt32() > 0);
        var list = await Json(await seller.GetAsync("/api/stock?status=bogus&pageSize=1"));
        Assert.Equal("org_s1", list.GetProperty("items")[0].GetProperty("sellerId").GetString());
        var products = await Json(await seller.GetAsync("/api/stock/products?pageSize=3"));
        Assert.True(products.GetProperty("total").GetInt32() > 0);
        var fc = await As("director@demo.ru");
        var ops = await Json(await fc.GetAsync("/api/stock/operations/summary"));
        Assert.Equal(3, ops.GetProperty("byType").EnumerateObject().Count());
        Assert.Equal(4, ops.GetProperty("errors").GetArrayLength());
        var opList = await Json(await fc.GetAsync("/api/stock/operations?pageSize=2"));
        Assert.True(opList.GetProperty("total").GetInt32() > 0);
        Assert.Equal(HttpStatusCode.NotFound, (await seller.GetAsync("/api/stock/locations")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await seller.GetAsync("/api/stock/nope")).StatusCode);
    }

    [Fact]
    public async Task Operations_require_role_and_return_201()
    {
        var seller = await As("seller@demo.ru");
        Assert.Equal(HttpStatusCode.Forbidden, (await seller.PostAsJsonAsync("/api/stock/writeoff", new { stockId = "x", quantity = 1, reason = "defect" })).StatusCode);

        var fc = await As("director@demo.ru");
        var items = (await Json(await fc.GetAsync("/api/stock?pageSize=200&warehouseId=wh_pdl1"))).GetProperty("items").EnumerateArray()
            .First(i => i.GetProperty("quantity").GetInt32() - i.GetProperty("reserved").GetInt32() > 1 && i.GetProperty("status").GetString() == "available");
        var id = items.GetProperty("id").GetString();
        var qty = items.GetProperty("quantity").GetInt32();
        var off = await fc.PostAsJsonAsync("/api/stock/writeoff", new { stockId = id, quantity = 1, reason = "defect" });
        Assert.Equal(HttpStatusCode.Created, off.StatusCode);
        var op = await Json(off);
        Assert.Equal((-1, "writeoff"), (op.GetProperty("quantity").GetInt32(), op.GetProperty("type").GetString()));
        Assert.StartsWith("OP-2", op.GetProperty("number").GetString());
        Assert.Equal(qty - 1, (await Json(await fc.GetAsync($"/api/stock/{id}"))).GetProperty("quantity").GetInt32());
        var tooMuch = await fc.PostAsJsonAsync("/api/stock/writeoff", new { stockId = id, quantity = 100000, reason = "defect" });
        Assert.Equal(HttpStatusCode.Conflict, tooMuch.StatusCode);
    }
}
