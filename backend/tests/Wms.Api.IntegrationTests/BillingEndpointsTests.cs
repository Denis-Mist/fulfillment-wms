using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Wms.Api.IntegrationTests;

public class BillingEndpointsTests(WmsApiFactory factory) : IClassFixture<WmsApiFactory>
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
    public async Task Fc_issues_and_pays_act_and_edits_tariff()
    {
        var fc = await As("director@demo.ru");
        var contracts = await Json(await fc.GetAsync("/api/billing/contracts"));
        var cid = contracts[0].GetProperty("contractId").GetString();
        var charges = await Json(await fc.GetAsync($"/api/billing/charges?pageSize=50&service=bogus&contractId={cid}"));
        Assert.True(charges.GetProperty("total").GetInt32() > 0);
        var summary = await Json(await fc.GetAsync("/api/billing/summary"));
        Assert.True(summary.GetProperty("monthAccrued").GetDecimal() >= 0);

        var acts = await Json(await fc.GetAsync($"/api/billing/acts?contractId={cid}"));
        var draft = acts.EnumerateArray().First(a => a.GetProperty("status").GetString() == "draft").GetProperty("id").GetString();
        var issued = await Json(await fc.PostAsync($"/api/billing/acts/{draft}/issue", null));
        Assert.Equal("issued", issued.GetProperty("status").GetString());
        Assert.EndsWith("Z", issued.GetProperty("issuedAt").GetString());
        var id = issued.GetProperty("id").GetString();
        Assert.Equal("paid", (await Json(await fc.PostAsync($"/api/billing/acts/{id}/pay", null))).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await fc.PostAsync($"/api/billing/acts/{id}/pay", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await fc.PostAsync("/api/billing/acts/act_none/pay", null)).StatusCode);

        var t = new { receivingPerUnit = 1, storagePerM3Day = 2, pickPerOrder = 3, packPerUnit = 4, fboPerBox = 5, returnPerUnit = 6 };
        Assert.Equal(3m, (await Json(await fc.PutAsJsonAsync($"/api/billing/tariffs/{cid}", t))).GetProperty("pickPerOrder").GetDecimal());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await fc.PutAsJsonAsync($"/api/billing/tariffs/{cid}", new { receivingPerUnit = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await fc.GetAsync("/api/billing/tariffs/ctr_none")).StatusCode);

        var seller = await As("seller@demo.ru");
        Assert.Equal(HttpStatusCode.Forbidden, (await seller.PostAsync($"/api/billing/acts/{id}/pay", null)).StatusCode);
    }

    [Fact]
    public async Task Seller_manages_integrations()
    {
        var seller = await As("seller@demo.ru");
        var list = await Json(await seller.GetAsync("/api/integrations"));
        Assert.Equal(2, list.GetArrayLength());
        Assert.Equal("wb", list[0].GetProperty("channel").GetString());
        Assert.Equal("connected", list[0].GetProperty("status").GetString());
        var synced = await Json(await seller.PostAsync("/api/integrations/wb/sync", null));
        Assert.Equal("orders", synced.GetProperty("log")[1].GetProperty("kind").GetString());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await seller.PutAsJsonAsync("/api/integrations/wb", new { autoImportFbs = true, pushStock = true, syncIntervalMin = 7 })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await seller.PostAsync("/api/integrations/yandex/sync", null)).StatusCode);
        var off = await Json(await seller.PostAsync("/api/integrations/wb/disconnect", null));
        Assert.Equal("disconnected", off.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await seller.PostAsync("/api/integrations/wb/sync", null)).StatusCode);
        var on = await Json(await seller.PostAsJsonAsync("/api/integrations/wb/connect", new { apiKey = "abcdefghijklmnopqrstuvwxyz" }));
        Assert.Equal("••••wxyz", on.GetProperty("keyMask").GetString());
        var fc = await As("director@demo.ru");
        Assert.Equal(HttpStatusCode.Forbidden, (await fc.GetAsync("/api/integrations")).StatusCode);
    }
}
