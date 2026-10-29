using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wms.Application.Billing;
using Wms.Infrastructure.Persistence;

namespace Wms.Api.IntegrationTests;

/// <summary>Регрессии QA этапа 2 (BE-S2-1…BE-S2-4). Свой фикстур — свежая БД (как после reset-db).</summary>
public class QaStage2Tests(WmsApiFactory factory) : IClassFixture<WmsApiFactory>
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

    private async Task<int> ActCountAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<WmsDbContext>().BillingActs.CountAsync();
    }

    [Fact]
    public async Task BES21_parallel_billing_reads_on_fresh_db_are_all_200_and_side_effect_free()
    {
        var clients = new[] { await As("director@demo.ru"), await As("seller@demo.ru") };
        var before = await ActCountAsync();
        Assert.True(before > 0, "акты должны создаваться при старте/сиде, а не в GET");

        var urls = new[] { "/api/billing/summary", "/api/billing/acts", "/api/billing/contracts" };
        var requests = Enumerable.Range(0, 30).Select(i => clients[i % 2].GetAsync(urls[i % 3]));
        var responses = await Task.WhenAll(requests);

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(before, await ActCountAsync());
    }

    [Fact]
    public async Task BES21_concurrent_provisioning_is_idempotent()
    {
        var before = await ActCountAsync();
        await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var scope = factory.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<BillingHandler>().ProvisionAsync(default);
        }));
        Assert.Equal(before, await ActCountAsync());
    }

    [Theory]
    [InlineData("http://127.0.0.1:5174")]
    [InlineData("http://localhost:5174")]
    [InlineData("http://127.0.0.1:5173")]
    [InlineData("http://localhost:5173")]
    public async Task BES22_cors_preflight_allows_dev_origins(string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/billing/summary");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "PUT");
        request.Headers.Add("Access-Control-Request-Headers", "authorization,content-type");
        var response = await factory.CreateClient().SendAsync(request);
        Assert.Equal(origin, Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
    }

    [Fact]
    public async Task BES22_cors_rejects_unknown_origin()
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/billing/summary");
        request.Headers.Add("Origin", "http://evil.example:5174");
        request.Headers.Add("Access-Control-Request-Method", "GET");
        var response = await factory.CreateClient().SendAsync(request);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    private static HttpRequestMessage Put(string url, string? body, bool chunked)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, url);
        if (body is null) return request;
        if (chunked)
        {
            request.Content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(body)));
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            request.Headers.TransferEncodingChunked = true;
        }
        else request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        return request;
    }

    [Theory]
    [InlineData("/api/settings/notifications", null, false)]
    [InlineData("/api/settings/notifications", "", false)]
    [InlineData("/api/settings/notifications", "", true)]
    [InlineData("/api/settings/notifications", "  ", true)]
    [InlineData("/api/settings/card-rules", null, false)]
    [InlineData("/api/settings/card-rules", "", false)]
    [InlineData("/api/settings/card-rules", "", true)]
    public async Task BES23_empty_settings_body_is_treated_as_empty_object(string url, string? body, bool chunked)
    {
        var client = await As("director@demo.ru");
        var empty = await client.SendAsync(Put(url, body, chunked));
        var obj = await client.SendAsync(Put(url, "{}", false));
        Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
        Assert.Equal(HttpStatusCode.OK, obj.StatusCode);
        Assert.Equal((await Json(obj)).ToString(), (await Json(empty)).ToString());
    }

    [Fact]
    public async Task BES23_malformed_settings_body_is_still_400()
    {
        var client = await As("director@demo.ru");
        var response = await client.SendAsync(Put("/api/settings/notifications", "{oops", false));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("", true)]
    public async Task BES24_admin_empty_notifications_body_matches_empty_object(string? body, bool chunked)
    {
        var admin = await As("admin@demo.ru");
        var empty = await admin.SendAsync(Put("/api/settings/notifications", body, chunked));
        var obj = await admin.SendAsync(Put("/api/settings/notifications", "{}", false));
        Assert.Equal(obj.StatusCode, empty.StatusCode);
        Assert.Equal((await Json(obj)).GetProperty("title").GetString(), (await Json(empty)).GetProperty("title").GetString());
    }
}
