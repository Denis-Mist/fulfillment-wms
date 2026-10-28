using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Wms.Api.IntegrationTests;

public class TaskEndpointsTests(WmsApiFactory factory) : IClassFixture<WmsApiFactory>
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
    public async Task Office_lists_summary_and_workers()
    {
        var fc = await As("director@demo.ru");
        var page = await Json(await fc.GetAsync("/api/tasks?pageSize=200&status=bogus"));
        Assert.Equal(55, page.GetProperty("total").GetInt32()); // задания ФФ-1
        var dates = page.GetProperty("items").EnumerateArray().Select(i => DateTimeOffset.Parse(i.GetProperty("createdAt").GetString()!)).ToList();
        Assert.Equal(dates.OrderByDescending(d => d).ToList(), dates);

        var summary = await Json(await fc.GetAsync("/api/tasks/summary"));
        Assert.Equal(55, summary.GetProperty("total").GetInt32());
        Assert.True(summary.GetProperty("byStatus").EnumerateObject().Any());

        var workers = await Json(await fc.GetAsync("/api/tasks/workers"));
        Assert.Contains(workers.EnumerateArray(), w => w.GetProperty("id").GetString() == "usr_fc1_3" && w.GetProperty("role").GetString() == "fc_picker");

        var picker = await As("picker@demo.ru");
        Assert.Equal(HttpStatusCode.Forbidden, (await picker.GetAsync("/api/tasks")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await fc.GetAsync("/api/tasks/tsk_missing")).StatusCode);
    }

    [Fact]
    public async Task Picker_scans_and_completes_task_creating_packing()
    {
        var picker = await As("picker@demo.ru");
        var mine = (await Json(await picker.GetAsync("/api/work/tasks"))).EnumerateArray().ToList();
        Assert.NotEmpty(mine);
        var current = mine.FirstOrDefault(t => t.GetProperty("status").GetString() == "in_progress");
        string id;
        if (current.ValueKind == JsonValueKind.Undefined)
        {
            id = mine.First(t => t.GetProperty("status").GetString() == "assigned").GetProperty("id").GetString()!;
            Assert.Equal(HttpStatusCode.OK, (await picker.PostAsync($"/api/work/tasks/{id}/start", null)).StatusCode);
        }
        else id = current.GetProperty("id").GetString()!;
        var other = mine.FirstOrDefault(t => t.GetProperty("status").GetString() == "assigned" && t.GetProperty("id").GetString() != id);
        if (other.ValueKind != JsonValueKind.Undefined)
        {
            var busy = await picker.PostAsync($"/api/work/tasks/{other.GetProperty("id").GetString()}/start", null);
            Assert.Equal(HttpStatusCode.Conflict, busy.StatusCode);
        }

        var bad = await picker.PostAsJsonAsync($"/api/work/tasks/{id}/scan", new { code = "nonsense" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, bad.StatusCode);
        Assert.Equal("Штрихкод не распознан", (await Json(bad)).GetProperty("title").GetString());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await picker.PostAsync($"/api/work/tasks/{id}/complete", null)).StatusCode);

        var details = await Json(await picker.GetAsync($"/api/tasks/{id}"));
        var orderIds = new List<string>();
        foreach (var step in details.GetProperty("steps").EnumerateArray())
        {
            var left = step.GetProperty("quantity").GetInt32() - step.GetProperty("done").GetInt32();
            if (left <= 0) continue;
            var loc = await Json(await picker.PostAsJsonAsync($"/api/work/tasks/{id}/scan", new { code = step.GetProperty("locationCode").GetString() }));
            Assert.StartsWith("Ячейка", loc.GetProperty("message").GetString());
            for (var i = 0; i < left; i++)
            {
                var r = await picker.PostAsJsonAsync($"/api/work/tasks/{id}/scan", new { code = step.GetProperty("barcode").GetString() });
                Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            }
        }
        var done = await Json(await picker.PostAsync($"/api/work/tasks/{id}/complete", null));
        Assert.Equal("done", done.GetProperty("status").GetString());
        Assert.EndsWith("Z", done.GetProperty("finishedAt").GetString());

        var fc = await As("director@demo.ru");
        if (done.GetProperty("type").GetString() == "picking")
        {
            var packing = await Json(await fc.GetAsync("/api/tasks?type=packing&status=new&pageSize=200"));
            Assert.Contains(packing.GetProperty("items").EnumerateArray(), t => t.GetProperty("id").GetString()!.StartsWith("tsk_p"));
        }

        var lookup = await Json(await picker.GetAsync($"/api/work/lookup?code={details.GetProperty("steps")[0].GetProperty("locationCode").GetString()}"));
        Assert.Equal("location", lookup.GetProperty("kind").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await picker.GetAsync("/api/work/lookup?code=zzz")).StatusCode);
    }

    [Fact]
    public async Task Batch_assign_and_cancel_returns_orders_to_reserve()
    {
        var fc = await As("director@demo.ru");
        var candidates = (await Json(await fc.GetAsync("/api/picking/candidates"))).EnumerateArray().ToList();
        Assert.NotEmpty(candidates);
        var wh = candidates[0].GetProperty("warehouseId").GetString();
        var ids = candidates.Where(c => c.GetProperty("warehouseId").GetString() == wh).Take(3).Select(c => c.GetProperty("id").GetString()!).ToArray();

        var preview = await Json(await fc.PostAsJsonAsync("/api/picking/preview", new { orderIds = ids }));
        Assert.True(preview.GetProperty("optimized").GetProperty("distance").GetDouble() <= preview.GetProperty("baseline").GetProperty("distance").GetDouble());

        var created = await fc.PostAsJsonAsync("/api/picking/batches", new { orderIds = ids, algorithm = "optimized" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var batch = await Json(created);
        var id = batch.GetProperty("id").GetString();
        Assert.Equal("new", batch.GetProperty("status").GetString());
        Assert.Equal(ids.Length, batch.GetProperty("ordersCount").GetInt32());

        var again = await fc.PostAsJsonAsync("/api/picking/batches", new { orderIds = ids, algorithm = "optimized" });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);

        var wrong = await fc.PostAsJsonAsync($"/api/tasks/{id}/assign", new { userId = "usr_fc1_4" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, wrong.StatusCode);
        var assigned = await Json(await fc.PostAsJsonAsync($"/api/tasks/{id}/assign", new { userId = "usr_fc1_6" }));
        Assert.Equal("assigned", assigned.GetProperty("status").GetString());

        var cancelled = await Json(await fc.PostAsync($"/api/tasks/{id}/cancel", null));
        Assert.Equal("cancelled", cancelled.GetProperty("status").GetString());
        var order = await Json(await fc.GetAsync($"/api/orders/{ids[0]}"));
        Assert.Equal("reserved", order.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await fc.PostAsJsonAsync($"/api/tasks/{id}/assign", new { userId = (string?)null })).StatusCode);

        var auto = await Json(await fc.PostAsJsonAsync("/api/tasks/auto-assign", new { }));
        Assert.True(auto.GetProperty("assigned").GetInt32() >= 0);
    }
}
