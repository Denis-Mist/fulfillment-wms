using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Wms.Api.Infrastructure;

public sealed class RateLimitingOptions
{
    public const string Section = "RateLimiting";
    public int AuthPerMinute { get; set; } = 20;
    public int DemoPerMinute { get; set; } = 10;
    public int DemoPerHour { get; set; } = 60;
}

public static class RateLimitingSetup
{
    public const string AuthPolicy = "auth";
    public const string DemoPolicy = "demo";
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

            options.AddPolicy(DemoPolicy, http =>
            {
                var opts = http.RequestServices.GetRequiredService<IOptions<RateLimitingOptions>>().Value;
                return RateLimitPartition.Get(ClientIp(http), _ => new ConjunctionRateLimiter(
                    Math.Max(1, opts.DemoPerMinute), Math.Max(1, opts.DemoPerHour)));
            });
        });
        return services;
    }

    private static string ClientIp(HttpContext http) =>
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}

/// <summary>Оба окна должны пройти: 10/мин и 60/час.</summary>
internal sealed class ConjunctionRateLimiter : RateLimiter
{
    private readonly RateLimiter _perMinute;
    private readonly RateLimiter _perHour;

    public ConjunctionRateLimiter(int perMinute, int perHour)
    {
        _perMinute = new SlidingWindowRateLimiter(new SlidingWindowRateLimiterOptions
        {
            PermitLimit = perMinute,
            Window = TimeSpan.FromMinutes(1),
            SegmentsPerWindow = 6,
            QueueLimit = 0,
            AutoReplenishment = true,
        });
        _perHour = new SlidingWindowRateLimiter(new SlidingWindowRateLimiterOptions
        {
            PermitLimit = perHour,
            Window = TimeSpan.FromHours(1),
            SegmentsPerWindow = 12,
            QueueLimit = 0,
            AutoReplenishment = true,
        });
    }

    public override TimeSpan? IdleDuration
    {
        get
        {
            var a = _perMinute.IdleDuration;
            var b = _perHour.IdleDuration;
            if (a is null) return b;
            if (b is null) return a;
            return a < b ? a : b;
        }
    }

    protected override RateLimitLease AttemptAcquireCore(int permitCount)
    {
        var first = _perMinute.AttemptAcquire(permitCount);
        if (!first.IsAcquired) return first;
        var second = _perHour.AttemptAcquire(permitCount);
        if (!second.IsAcquired)
        {
            first.Dispose();
            return second;
        }
        return new CombinedLease(first, second);
    }

    protected override ValueTask<RateLimitLease> AcquireAsyncCore(int permitCount, CancellationToken cancellationToken) =>
        ValueTask.FromResult(AttemptAcquireCore(permitCount));

    public override RateLimiterStatistics? GetStatistics() => _perMinute.GetStatistics();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _perMinute.Dispose();
            _perHour.Dispose();
        }
        base.Dispose(disposing);
    }
}

internal sealed class CombinedLease(RateLimitLease first, RateLimitLease second) : RateLimitLease
{
    public override bool IsAcquired => true;

    public override IEnumerable<string> MetadataNames => first.MetadataNames.Concat(second.MetadataNames).Distinct();

    public override bool TryGetMetadata(string metadataName, out object? metadata) =>
        first.TryGetMetadata(metadataName, out metadata) || second.TryGetMetadata(metadataName, out metadata);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            first.Dispose();
            second.Dispose();
        }
    }
}
