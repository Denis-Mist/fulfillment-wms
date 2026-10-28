using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Wms.Api.IntegrationTests;

public class WarehouseCatalogEndpointsTests(WmsApiFactory factory) : IClassFixture<WmsApiFactory>
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
    public async Task Warehouses_expose_seeded_topology_and_metrics()
    {
        var fc = await As("director@demo.ru");
        var list = await Json(await fc.GetAsync("/api/warehouses"));
        var pdl = list.EnumerateArray().Single(w => w.GetProperty("id").GetString() == "wh_pdl1");
        Assert.Equal((384, 194), (pdl.GetProperty("locationsCount").GetInt32(), pdl.GetProperty("occupiedLocations").GetInt32()));
        Assert.Equal(6, (await Json(await fc.GetAsync("/api/warehouses/wh_pdl1/zones"))).GetArrayLength());
        var locations = await Json(await fc.GetAsync("/api/warehouses/wh_pdl1/locations?pageSize=5"));
        Assert.Equal(384, locations.GetProperty("total").GetInt32());
        Assert.True((await Json(await fc.GetAsync("/api/warehouses/wh_pdl1/map?level=1"))).GetArrayLength() > 0);
        Assert.Equal(HttpStatusCode.NotFound, (await fc.GetAsync("/api/warehouses/wh_krd1/zones")).StatusCode);
    }

    [Fact]
    public async Task Zone_creation_requires_management_role_and_returns_201()
    {
        var picker = await As("picker@demo.ru");
        Assert.Equal(HttpStatusCode.Forbidden, (await picker.PostAsJsonAsync("/api/warehouses/wh_kzn1/zones", new { code = "QQ", name = "Q", type = "storage" })).StatusCode);
        var manager = await As("manager@demo.ru");
        var created = await manager.PostAsJsonAsync("/api/warehouses/wh_kzn1/zones", new { code = "qq", name = "Новая", type = "storage" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal("wh_kzn1_z_QQ", (await Json(created)).GetProperty("id").GetString());
        var again = await manager.PostAsJsonAsync("/api/warehouses/wh_kzn1/zones", new { code = "QQ", name = "Новая", type = "storage" });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task Putaway_check_returns_five_checks()
    {
        var fc = await As("director@demo.ru");
        var response = await fc.PostAsJsonAsync("/api/warehouses/wh_pdl1/putaway-check",
            new { locationId = "wh_pdl1_l_A-01-1", productId = "prd_ud_1", quantity = 1 });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(5, (await Json(response)).GetProperty("checks").GetArrayLength());
        var bad = await fc.PostAsJsonAsync("/api/warehouses/wh_pdl1/putaway-check", new { locationId = "nope", productId = "prd_ud_1", quantity = 1 });
        Assert.Equal("Укажите ячейку и товар", (await Json(bad)).GetProperty("title").GetString());
    }

    [Fact]
    public async Task Seller_catalog_matches_mock_counts()
    {
        var seller = await As("seller@demo.ru");
        var facets = await Json(await seller.GetAsync("/api/catalog/products/facets"));
        Assert.Equal((30, 30, 0), (facets.GetProperty("total").GetInt32(), facets.GetProperty("marking").GetInt32(), facets.GetProperty("noStock").GetInt32()));
        var page = await Json(await seller.GetAsync("/api/catalog/products?pageSize=200&flag=whatever"));
        Assert.Equal(30, page.GetProperty("total").GetInt32());
        var details = await Json(await seller.GetAsync("/api/catalog/products/prd_ud_1"));
        Assert.Equal(799, details.GetProperty("stockQty").GetInt32());
        Assert.True(details.GetProperty("stock").GetArrayLength() > 0);

        var archive = await seller.PostAsync("/api/catalog/products/prd_ud_1/archive", null);
        Assert.Equal(HttpStatusCode.Conflict, archive.StatusCode);
        Assert.Equal("Сначала отгрузите или спишите остаток на складах.", (await Json(archive)).GetProperty("detail").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await seller.GetAsync("/api/catalog/products/prd_kw_1")).StatusCode);
    }

    [Fact]
    public async Task Seller_creates_and_imports_products()
    {
        var seller = await As("owner@kw.demo.ru");
        var created = await seller.PostAsJsonAsync("/api/catalog/products", new
        {
            sku = "IT-NEW-1", name = "Тестовый товар", barcode = "4600000099991", price = 10, weightG = 100, volumeLiters = 1,
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var dup = await seller.PostAsJsonAsync("/api/catalog/products", new
        {
            sku = "it-new-1", name = "Тестовый товар", barcode = "4600000099992", price = 10, weightG = 100, volumeLiters = 1,
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, dup.StatusCode);
        var import = await Json(await seller.PostAsJsonAsync("/api/catalog/products/import", new
        {
            rows = new object[]
            {
                new { sku = "IT-NEW-1", name = "Переименован", barcode = "4600000099991", price = 12, weightG = 100, volumeLiters = 1 },
                new { sku = "IT-NEW-2", name = "x", barcode = "4600000099993", price = 1, weightG = 1, volumeLiters = 1 },
            },
        }));
        Assert.Equal((0, 1), (import.GetProperty("created").GetInt32(), import.GetProperty("updated").GetInt32()));
        Assert.Equal(3, import.GetProperty("errors")[0].GetProperty("row").GetInt32());
        var fc = await As("director@demo.ru");
        Assert.Equal(HttpStatusCode.Forbidden, (await fc.PostAsJsonAsync("/api/catalog/products/import", new { rows = Array.Empty<object>() })).StatusCode);
    }

    [Fact]
    public async Task Timestamps_are_serialized_with_z()
    {
        var admin = await As("admin@demo.ru");
        var body = await (await admin.GetAsync("/api/admin/organizations?pageSize=1")).Content.ReadAsStringAsync();
        Assert.Matches("\"createdAt\":\"\\d{4}-\\d\\d-\\d\\dT\\d\\d:\\d\\d:\\d\\d\\.\\d{3}Z\"", body);
    }
}
