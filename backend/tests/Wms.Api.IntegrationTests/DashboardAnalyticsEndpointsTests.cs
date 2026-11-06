using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Wms.Api.IntegrationTests;

public class DashboardAnalyticsEndpointsTests(WmsApiFactory factory) : IClassFixture<WmsApiFactory>
{
    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, text);
        return JsonDocument.Parse(text).RootElement;
    }

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
    public async Task Dashboards_are_scoped_by_organization_type()
    {
        var fc = await As("director@demo.ru");
        var d = await Json(await fc.GetAsync("/api/dashboard/fulfillment?period=7d"));
        Assert.Equal(7, d.GetProperty("trend").GetArrayLength());
        Assert.Equal(5, d.GetProperty("pipeline").GetArrayLength());
        Assert.True(d.GetProperty("warehouses").GetArrayLength() > 0);
        Assert.Equal(30, (await Json(await fc.GetAsync("/api/dashboard/fulfillment?period=bogus"))).GetProperty("trend").GetArrayLength());
        Assert.Equal(HttpStatusCode.Forbidden, (await fc.GetAsync("/api/dashboard/seller")).StatusCode);

        var seller = await As("seller@demo.ru");
        var s = await Json(await seller.GetAsync("/api/dashboard/seller"));
        Assert.True(s.GetProperty("kpis").GetProperty("stockUnits").GetProperty("value").GetDouble() > 0);
        Assert.True(s.GetProperty("recentInbound").GetArrayLength() <= 5);
        Assert.Equal(HttpStatusCode.Forbidden, (await seller.GetAsync("/api/dashboard/fulfillment")).StatusCode);
    }

    [Fact]
    public async Task Forecast_slotting_and_kpi()
    {
        var fc = await As("director@demo.ru");
        var f = await Json(await fc.GetAsync("/api/analytics/forecast?horizon=50&algorithm=bogus"));
        Assert.Equal(28, f.GetProperty("horizon").GetInt32());
        Assert.Equal("holt_winters", f.GetProperty("meta").GetProperty("algorithm").GetString());
        Assert.Equal(56 + 28, f.GetProperty("series").GetArrayLength());
        Assert.EndsWith("Z", f.GetProperty("series")[0].GetProperty("date").GetString());
        var plan = await Json(await fc.PostAsJsonAsync("/api/analytics/forecast/approve", new { algorithm = "moving_average" }));
        Assert.Equal("moving_average", plan.GetProperty("algorithm").GetString());
        Assert.Equal("moving_average", (await Json(await fc.GetAsync("/api/analytics/forecast"))).GetProperty("plan").GetProperty("algorithm").GetString());

        var slot = await Json(await fc.GetAsync("/api/analytics/slotting"));
        Assert.True(slot.GetProperty("classes").GetProperty("A").GetProperty("sku").GetInt32() > 0);
        var moves = slot.GetProperty("moves");
        if (moves.GetArrayLength() > 0)
        {
            var id = moves[0].GetProperty("id").GetString();
            Assert.Equal(1, (await Json(await fc.PostAsJsonAsync("/api/analytics/slotting/apply", new { ids = new[] { id } }))).GetProperty("applied").GetInt32());
            var again = await Json(await fc.GetAsync("/api/analytics/slotting"));
            var listed = again.GetProperty("moves").EnumerateArray().Where(m => m.GetProperty("id").GetString() == id).ToList();
            if (listed.Count > 0) Assert.True(listed[0].GetProperty("applied").GetBoolean());
        }
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await fc.PostAsJsonAsync("/api/analytics/slotting/apply", new { ids = Array.Empty<string>() })).StatusCode);

        var kpi = await Json(await fc.GetAsync("/api/analytics/kpi?period=14"));
        Assert.Equal(14, kpi.GetProperty("series").GetArrayLength());
        Assert.True(kpi.GetProperty("totals").GetProperty("orders").GetInt32() > 0);
        Assert.True(kpi.GetProperty("totals").GetProperty("routeReductionPct").GetDouble() > 0);
        Assert.True(kpi.GetProperty("bySeller").GetArrayLength() > 0);
        Assert.True(kpi.GetProperty("byWarehouse").GetArrayLength() > 0);
        Assert.True(kpi.GetProperty("byWarehouse")[0].TryGetProperty("warehouseId", out _));

        var seller = await As("seller@demo.ru");
        Assert.Equal(HttpStatusCode.Forbidden, (await seller.GetAsync("/api/analytics/kpi")).StatusCode);
    }

    [Fact]
    public async Task Forecast_history_comes_from_real_orders()
    {
        var fc = await As("director@demo.ru");
        var all = await Json(await fc.GetAsync("/api/analytics/forecast?horizon=7"));
        Assert.Equal("2.0.0", all.GetProperty("meta").GetProperty("version").GetString());
        Assert.StartsWith("orders-", all.GetProperty("meta").GetProperty("dataVersion").GetString());
        var hist = all.GetProperty("series").EnumerateArray().Take(56).ToList();
        Assert.All(hist, p => Assert.True(p.TryGetProperty("actual", out var a) && a.ValueKind == JsonValueKind.Number));
        var sumAll = hist.Sum(p => p.GetProperty("actual").GetInt32());
        Assert.True(sumAll > 0, $"expected seeded orders in history, got {sumAll}");

        var one = await Json(await fc.GetAsync("/api/analytics/forecast?warehouseId=wh_pdl1&horizon=7"));
        var sumOne = one.GetProperty("series").EnumerateArray().Take(56).Sum(p => p.GetProperty("actual").GetInt32());
        Assert.True(sumOne > 0);
        Assert.True(sumOne < sumAll);
    }
}
