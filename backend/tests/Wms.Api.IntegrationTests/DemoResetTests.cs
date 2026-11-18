using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Wms.Api.Infrastructure;
using Wms.Application.Demo;

namespace Wms.Api.IntegrationTests;

public class DemoResetTests(DemoApiFactory factory) : IClassFixture<DemoApiFactory>
{
    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private async Task<HttpClient> AsPasswordAsync(string email)
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "demo" });
        login.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", (await Json(login)).GetProperty("accessToken").GetString());
        return client;
    }

    [Fact]
    public async Task Admin_reset_restores_seed_including_account_manager()
    {
        var payload = new
        {
            organizationType = "seller",
            organization = new { name = "ООО Временная", inn = "7700000099", city = "Казань", marketplaces = new[] { "wb" } },
            user = new { fullName = "Временный", email = "temp-reset@test.ru", phone = "+7 900 111-11-11", password = "secret" },
        };
        var created = await factory.CreateClient().PostAsJsonAsync("/api/auth/register", payload);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var before = await factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new { email = "temp-reset@test.ru", password = "secret" });
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);

        var admin = await AsPasswordAsync("admin@demo.ru");
        var reset = await admin.PostAsync("/api/admin/demo/reset", null);
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        Assert.Equal("ok", (await Json(reset)).GetProperty("status").GetString());

        var after = await factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new { email = "temp-reset@test.ru", password = "secret" });
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);

        var seed = await factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new { email = "am@fc1.demo.ru", password = "demo" });
        Assert.Equal(HttpStatusCode.OK, seed.StatusCode);
        Assert.Equal("usr_fc1_12", (await Json(seed)).GetProperty("user").GetProperty("id").GetString());
    }

    [Fact]
    public async Task Health_stays_ok_while_api_returns_503_during_reset()
    {
        var gate = factory.Services.GetRequiredService<IDemoResetGate>();
        Assert.True(gate.TryBegin());
        try
        {
            var client = factory.CreateClient();
            var health = await client.GetAsync("/health");
            Assert.Equal(HttpStatusCode.OK, health.StatusCode);

            var status = await client.GetAsync("/api/auth/demo/status");
            Assert.Equal(HttpStatusCode.OK, status.StatusCode);
            Assert.True((await Json(status)).GetProperty("enabled").GetBoolean());

            var login = await client.PostAsJsonAsync("/api/auth/login", new { email = "director@demo.ru", password = "demo" });
            Assert.Equal(HttpStatusCode.ServiceUnavailable, login.StatusCode);
            Assert.Equal(DemoGuardMiddleware.ResetTitle, (await Json(login)).GetProperty("title").GetString());
            Assert.Equal(DemoGuardMiddleware.ResetRetryAfterSeconds.ToString(), login.Headers.RetryAfter?.Delta?.TotalSeconds.ToString("0")
                ?? login.Headers.RetryAfter?.ToString());
            Assert.True(login.Headers.TryGetValues("Retry-After", out var values));
            Assert.Equal("60", values.Single());
        }
        finally
        {
            gate.End();
        }

        var after = await factory.CreateClient().GetAsync("/api/auth/demo/status");
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
    }

    [Fact]
    public void Next_reset_delay_is_computed_in_moscow_time()
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById(DemoResetHostedService.TimeZoneId);
        var noonMoscowUtc = new DateTimeOffset(2026, 11, 18, 9, 0, 0, TimeSpan.Zero);
        Assert.Equal(TimeSpan.FromHours(15), DemoResetHostedService.DelayUntilNextReset(3, tz, noonMoscowUtc));

        var atThreeUtc = new DateTimeOffset(2026, 11, 18, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal(TimeSpan.FromHours(24), DemoResetHostedService.DelayUntilNextReset(3, tz, atThreeUtc));
    }
}
