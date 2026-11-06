using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Wms.Api.IntegrationTests;

/// <summary>Регрессии по багам QA интеллектуальных модулей (BE-FR-1…BE-FR-4).</summary>
public class QaFrFixesTests(WmsApiFactory factory) : IClassFixture<WmsApiFactory>
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
    public async Task BE_FR_1_unknown_forecast_algorithm_returns_422()
    {
        var fc = await As("director@demo.ru");
        var response = await fc.GetAsync("/api/analytics/forecast?algorithm=unknown");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("Неизвестный алгоритм", (await Json(response)).GetProperty("title").GetString());
    }

    [Fact]
    public async Task BE_FR_2_unknown_slotting_algorithm_returns_422()
    {
        var fc = await As("director@demo.ru");
        var response = await fc.GetAsync("/api/analytics/slotting?warehouseId=wh_khm1&algorithm=unknown");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("Неизвестный алгоритм", (await Json(response)).GetProperty("title").GetString());
    }

    [Fact]
    public async Task BE_FR_3_apply_does_not_count_unknown_recommendation_ids()
    {
        var fc = await As("director@demo.ru");
        var missing = await Json(await fc.PostAsJsonAsync("/api/analytics/slotting/apply", new { ids = new[] { "slot_does_not_exist" } }));
        Assert.Equal(0, missing.GetProperty("applied").GetInt32());

        var slot = await Json(await fc.GetAsync("/api/analytics/slotting"));
        var moves = slot.GetProperty("moves").EnumerateArray().Select(m => m.GetProperty("id").GetString()!).ToList();
        if (moves.Count == 0) return;
        var mixed = await Json(await fc.PostAsJsonAsync("/api/analytics/slotting/apply",
            new { ids = new[] { moves[0], "slot_fake_xxx" } }));
        Assert.Equal(1, mixed.GetProperty("applied").GetInt32());
    }

    [Fact]
    public async Task BE_FR_4_assign_unknown_user_returns_404()
    {
        var fc = await As("director@demo.ru");
        var items = (await Json(await fc.GetAsync("/api/tasks?pageSize=50&status=new"))).GetProperty("items").EnumerateArray().ToList();
        Assert.NotEmpty(items);
        var id = items[0].GetProperty("id").GetString();
        var response = await fc.PostAsJsonAsync($"/api/tasks/{id}/assign", new { userId = "usr_does_not_exist" });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Сотрудник не найден", (await Json(response)).GetProperty("title").GetString());
    }
}
