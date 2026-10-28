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
            Assert.Contains(again.GetProperty("moves").EnumerateArray(), m => m.GetProperty("id").GetString() == id && m.GetProperty("applied").GetBoolean());
        }
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await fc.PostAsJsonAsync("/api/analytics/slotting/apply", new { ids = Array.Empty<string>() })).StatusCode);

        var kpi = await Json(await fc.GetAsync("/api/analytics/kpi?period=14"));
        Assert.Equal(14, kpi.GetProperty("series").GetArrayLength());
        Assert.True(kpi.GetProperty("totals").GetProperty("routeReductionPct").GetDouble() > 0);

        var seller = await As("seller@demo.ru");
        Assert.Equal(HttpStatusCode.Forbidden, (await seller.GetAsync("/api/analytics/kpi")).StatusCode);
    }
}
