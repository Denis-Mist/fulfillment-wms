using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Wms.Api.IntegrationTests;

/// <summary>Отдельный fixture: создание партий меняет число заданий и не должно ломать подсчёт в TaskEndpointsTests.</summary>
public class PickingBatchEndpointsTests(WmsApiFactory factory) : IClassFixture<WmsApiFactory>
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
    public async Task Auto_batch_suggestions_and_create_persist_metrics()
    {
        var fc = await As("director@demo.ru");
        var suggestions = await Json(await fc.GetAsync("/api/picking/suggestions?warehouseId=wh_pdl1"));
        Assert.True(suggestions.GetProperty("batches").GetArrayLength() >= 1);
        var planned = suggestions.GetProperty("batches")[0];
        Assert.True(planned.GetProperty("route").GetProperty("optimized").GetProperty("distance").GetDouble()
            <= planned.GetProperty("route").GetProperty("baseline").GetProperty("distance").GetDouble());

        var created = await Json(await fc.PostAsJsonAsync("/api/picking/batches/auto", new { warehouseId = "wh_pdl1", algorithm = "optimized" }));
        Assert.True(created.GetProperty("created").GetInt32() >= 1);
        foreach (var task in created.GetProperty("tasks").EnumerateArray())
        {
            Assert.True(task.GetProperty("optimizedDistance").GetDouble() <= task.GetProperty("baselineDistance").GetDouble());
            Assert.True(task.GetProperty("volumeLiters").GetDouble() > 0);
            Assert.Equal(60, task.GetProperty("toteCapacityLiters").GetDouble());
            Assert.True(task.GetProperty("estimatedMinutes").GetInt32() >= 1);
            Assert.NotEmpty(task.GetProperty("steps").EnumerateArray());
            await fc.PostAsync($"/api/tasks/{task.GetProperty("id").GetString()}/cancel", null);
        }

        var flagged = await Json(await fc.PostAsJsonAsync("/api/picking/batches", new { auto = true, warehouseId = "wh_pdl1" }));
        Assert.True(flagged.GetProperty("created").GetInt32() >= 1);
        foreach (var task in flagged.GetProperty("tasks").EnumerateArray())
            await fc.PostAsync($"/api/tasks/{task.GetProperty("id").GetString()}/cancel", null);
    }
}
