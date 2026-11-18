using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Wms.Api.Infrastructure;

public sealed class RateLimitingOptions
{
    public const string Section = "RateLimiting";
    public int AuthPerMinute { get; set; } = 20;
}

public static class RateLimitingSetup
{
    public const string AuthPolicy = "auth";
    public const string TooManyTitle = "Слишком много попыток, попробуйте позже";

    public static IServiceCollection AddWmsRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RateLimitingOptions>(configuration.GetSection(RateLimitingOptions.Section));
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = (context, _) =>
                new ValueTask(Problems.WriteAsync(context.HttpContext, StatusCodes.Status429TooManyRequests, TooManyTitle));

            options.AddPolicy(AuthPolicy, http =>
            {
                var opts = http.RequestServices.GetRequiredService<IOptions<RateLimitingOptions>>().Value;
                return RateLimitPartition.GetFixedWindowLimiter(ClientIp(http), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = Math.Max(1, opts.AuthPerMinute),
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                });
            });
        });
        return services;
    }

    private static string ClientIp(HttpContext http) =>
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
