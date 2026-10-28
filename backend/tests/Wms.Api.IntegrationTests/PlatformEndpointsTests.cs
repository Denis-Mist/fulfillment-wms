using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Wms.Api.IntegrationTests;

public class PlatformEndpointsTests(WmsApiFactory factory) : IClassFixture<WmsApiFactory>
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

    private static async Task AssertProblem(HttpResponseMessage response, HttpStatusCode status, string title)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(title, (await Json(response)).GetProperty("title").GetString());
    }

    [Theory]
    [InlineData("/api/settings")]
    [InlineData("/api/notifications")]
    [InlineData("/api/clients")]
    [InlineData("/api/seller/fulfillments")]
    public async Task Pending_organization_is_blocked_like_mock(string url)
    {
        var client = await As("owner2@pending.demo.ru");
        var response = await client.GetAsync(url);
        await AssertProblem(response, HttpStatusCode.Forbidden, "Организация не активна");
        Assert.Equal("Доступ откроется после проверки администратором.", (await Json(response)).GetProperty("detail").GetString());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Anonymous_gets_401_and_wrong_role_gets_403()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/admin/overview")).StatusCode);
        var director = await As("director@demo.ru");
        await AssertProblem(await director.GetAsync("/api/admin/users"), HttpStatusCode.Forbidden, "Недостаточно прав");
        await AssertProblem(await director.GetAsync("/api/seller/fulfillments"), HttpStatusCode.Forbidden, "Недостаточно прав");
    }

    [Fact]
    public async Task Admin_lists_and_moderates_organizations()
    {
        var admin = await As("admin@demo.ru");
        var summary = await Json(await admin.GetAsync("/api/admin/organizations/summary"));
        Assert.True(summary.GetProperty("total").GetInt32() > 3);

        var pending = await Json(await admin.GetAsync("/api/admin/organizations?status=pending&pageSize=1"));
        Assert.InRange(pending.GetProperty("total").GetInt32(), 2, 3);
        Assert.Single(pending.GetProperty("items").EnumerateArray());

        await AssertProblem(await admin.PostAsync("/api/admin/organizations/org_fc1/approve", null), HttpStatusCode.Conflict,
            "Действие недоступно для текущего статуса");
        await AssertProblem(await admin.PostAsync("/api/admin/organizations/org_fc1/zap", null), HttpStatusCode.BadRequest,
            "Неизвестное действие");

        var approved = await admin.PostAsync("/api/admin/organizations/org_p3/approve", null);
        Assert.Equal("active", (await Json(approved)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Admin_references_audit_and_user_actions()
    {
        var admin = await As("admin@demo.ru");
        var refs = await Json(await admin.GetAsync("/api/admin/references"));
        Assert.True(refs.GetArrayLength() >= 21);
        Assert.Equal("ref_units_PCS", refs[0].GetProperty("id").GetString());

        var created = await admin.PostAsJsonAsync("/api/admin/references", new { kind = "carriers", code = "dpd_x", name = "DPD X" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal("ref_carriers_DPD_X", (await Json(created)).GetProperty("id").GetString());
        await AssertProblem(await admin.PostAsJsonAsync("/api/admin/references", new { kind = "carriers", code = "DPD_X", name = "Y" }),
            HttpStatusCode.Conflict, "Код DPD_X уже есть в справочнике");

        var reset = await admin.PostAsync("/api/admin/users/usr_fc1_7/reset-password", null);
        Assert.StartsWith("wms-", (await Json(reset)).GetProperty("temporaryPassword").GetString());
        await AssertProblem(await admin.PostAsync("/api/admin/users/usr_admin/block", null), HttpStatusCode.Conflict,
            "Нельзя заблокировать администратора платформы");

        var audit = await Json(await admin.GetAsync("/api/admin/audit?category=reference"));
        Assert.Contains(audit.GetProperty("items").EnumerateArray(),
            e => e.GetProperty("action").GetString() == "Добавлена запись справочника");

        var overview = await Json(await admin.GetAsync("/api/admin/overview"));
        Assert.Equal(30, overview.GetProperty("ordersByDay").GetArrayLength());
    }

    [Fact]
    public async Task Fulfillment_clients_and_requests()
    {
        var director = await As("director@demo.ru");
        var clients = await Json(await director.GetAsync("/api/fc/clients"));
        Assert.True(clients.GetArrayLength() > 0);
        var first = clients[0];
        Assert.StartsWith("Д-", first.GetProperty("contractNumber").GetString());

        var details = await director.GetAsync($"/api/fc/clients/{first.GetProperty("id").GetString()}");
        Assert.Equal(HttpStatusCode.OK, details.StatusCode);
        await AssertProblem(await director.GetAsync("/api/fc/clients/org_nope"), HttpStatusCode.NotFound, "Клиент не найден");

        var requests = await Json(await director.GetAsync("/api/fc/client-requests"));
        Assert.True(requests.GetArrayLength() >= 4);
        await AssertProblem(await director.PostAsJsonAsync("/api/fc/client-requests/creq_1/accept", new { warehouseIds = Array.Empty<string>() }),
            HttpStatusCode.UnprocessableEntity, "Выберите склады для договора");
        await AssertProblem(await director.PostAsync("/api/fc/client-requests/creq_4/decline", null),
            HttpStatusCode.Conflict, "Заявка уже рассмотрена");
        var declined = await director.PostAsync("/api/fc/client-requests/creq_2/decline", null);
        Assert.Equal("declined", (await Json(declined)).GetProperty("status").GetString());

        var manager = await As("manager@demo.ru");
        await AssertProblem(await manager.PostAsync("/api/fc/client-requests/creq_3/decline", null), HttpStatusCode.Forbidden, "Недостаточно прав");
    }

    [Fact]
    public async Task Seller_sees_partner_cards_and_connects()
    {
        var seller = await As("seller@demo.ru");
        var cards = await Json(await seller.GetAsync("/api/seller/fulfillments"));
        Assert.Equal(3, cards.GetArrayLength());
        Assert.Equal("connected", cards[0].GetProperty("connection").GetString());
        var target = cards.EnumerateArray().FirstOrDefault(c => c.GetProperty("connection").GetString() == "none");
        if (target.ValueKind == JsonValueKind.Undefined) return;
        var id = target.GetProperty("id").GetString();
        await AssertProblem(await seller.PostAsJsonAsync($"/api/seller/fulfillments/{id}/connect",
            new { skuEstimate = 10, ordersPerDay = 5, marketplaces = Array.Empty<string>() }),
            HttpStatusCode.UnprocessableEntity, "Выберите хотя бы один маркетплейс");
        var connected = await seller.PostAsJsonAsync($"/api/seller/fulfillments/{id}/connect",
            new { skuEstimate = 10, ordersPerDay = 5, marketplaces = new[] { "wb" }, message = "hi" });
        Assert.Equal("pending", (await Json(connected)).GetProperty("connection").GetString());
    }

    [Fact]
    public async Task Settings_profile_team_and_notifications()
    {
        var director = await As("director@demo.ru");
        var settings = await Json(await director.GetAsync("/api/settings"));
        Assert.Equal("org_fc1", settings.GetProperty("id").GetString());
        var profile = JsonSerializer.Deserialize<Dictionary<string, object>>(settings.GetProperty("profile").GetRawText())!;
        profile["bik"] = "12";
        await AssertProblem(await director.PutAsJsonAsync("/api/settings/profile", profile), HttpStatusCode.UnprocessableEntity,
            "БИК должен состоять из 9 цифр");
        profile["bik"] = "044525225";
        profile["inn"] = "0000000000";
        var updated = await Json(await director.PutAsJsonAsync("/api/settings/profile", profile));
        Assert.Equal("7714382910", updated.GetProperty("profile").GetProperty("inn").GetString());

        var invite = await director.PostAsJsonAsync("/api/settings/team/invite", new { email = "New.Picker@demo.ru", role = "fc_picker" });
        Assert.Equal(HttpStatusCode.Created, invite.StatusCode);
        var inviteId = (await Json(invite)).GetProperty("id").GetString();
        var team = await Json(await director.GetAsync("/api/settings/team"));
        Assert.Equal("invited", team[0].GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await director.PostAsync($"/api/settings/team/{inviteId}/revoke", null)).StatusCode);
        await AssertProblem(await director.PostAsync($"/api/settings/team/{inviteId}/revoke", null), HttpStatusCode.NotFound, "Приглашение не найдено");

        var admin = await As("admin@demo.ru");
        var notifications = await Json(await admin.GetAsync("/api/notifications"));
        Assert.Contains(notifications.EnumerateArray(), n => n.GetProperty("id").GetString()!.StartsWith("pending_"));
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync("/api/notifications/read-all", null)).StatusCode);
        var after = await Json(await admin.GetAsync("/api/notifications"));
        Assert.All(after.EnumerateArray(), n => Assert.True(n.GetProperty("read").GetBoolean()));

        Assert.Equal(0, (await Json(await director.GetAsync("/api/search?q=a"))).GetArrayLength());
    }
}
