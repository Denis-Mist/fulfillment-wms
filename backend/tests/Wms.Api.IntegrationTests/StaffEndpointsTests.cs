using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Wms.Api.IntegrationTests;

public class StaffEndpointsTests(WmsApiFactory factory) : IClassFixture<WmsApiFactory>
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
    public async Task Director_lists_and_edits_staff()
    {
        var fc = await As("director@demo.ru");
        var res = await Json(await fc.GetAsync("/api/staff"));
        Assert.EndsWith("Z", res.GetProperty("weekStart").GetString());
        var items = res.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(11, items.Count);
        var picker = items.Single(i => i.GetProperty("id").GetString() == "usr_fc1_3");
        Assert.Equal(60, picker.GetProperty("norm").GetInt32());
        Assert.Equal(7, picker.GetProperty("shifts").GetArrayLength());
        Assert.Contains(picker.GetProperty("onShift").ValueKind, new[] { JsonValueKind.True, JsonValueKind.False });

        var wh = picker.GetProperty("warehouseId").GetString();
        var put = await fc.PutAsJsonAsync("/api/staff/usr_fc1_3", new { role = "fc_packer", warehouseId = wh, zoneIds = new[] { "nope" }, skills = new[] { "packing" }, active = true });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var updated = await Json(put);
        Assert.Equal("fc_packer", updated.GetProperty("role").GetString());
        Assert.Equal(0, updated.GetProperty("zoneIds").GetArrayLength());

        var shift = await Json(await fc.PostAsJsonAsync("/api/staff/usr_fc1_3/shifts", new { day = 6, shift = "vacation" }));
        Assert.Equal("vacation", shift.GetProperty("shifts")[6].GetString());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await fc.PostAsJsonAsync("/api/staff/usr_fc1_3/shifts", new { day = 7, shift = "day" })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await fc.PutAsJsonAsync("/api/staff/usr_fc1_1", new { role = "fc_owner", warehouseId = wh, zoneIds = Array.Empty<string>(), skills = Array.Empty<string>(), active = false })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await fc.PutAsJsonAsync("/api/staff/usr_missing", new { role = "fc_owner", warehouseId = wh, active = true })).StatusCode);
    }

    [Fact]
    public async Task Access_rules()
    {
        var seller = await As("seller@demo.ru");
        Assert.Equal(HttpStatusCode.Forbidden, (await seller.GetAsync("/api/staff")).StatusCode);
        var picker = await As("picker@demo.ru");
        Assert.Equal(HttpStatusCode.OK, (await picker.GetAsync("/api/staff")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await picker.PostAsJsonAsync("/api/staff/usr_fc1_3/shifts", new { day = 1, shift = "off" })).StatusCode);
    }
}
