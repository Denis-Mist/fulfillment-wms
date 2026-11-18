using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace Wms.Api.Infrastructure;

public static class ForwardedHeadersSetup
{
    public static IServiceCollection AddWmsForwardedHeaders(this IServiceCollection services)
    {
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            // nginx на хосте (127.0.0.1) ставит X-Forwarded-For / X-Forwarded-Proto.
            // Не доверяем произвольному заголовку снаружи.
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
            options.KnownProxies.Add(IPAddress.Loopback);
            options.KnownProxies.Add(IPAddress.IPv6Loopback);
        });
        return services;
    }
}
