using Microsoft.Extensions.DependencyInjection;
using Wms.Application.Auth;

namespace Wms.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IRegisterRequestValidator, RegisterRequestValidator>();
        services.AddScoped<ICurrentUserMapper, CurrentUserMapper>();
        services.AddScoped<IRequestContextResolver, RequestContextResolver>();
        services.AddScoped<LoginHandler>();
        services.AddScoped<RegisterHandler>();
        services.AddScoped<GetCurrentUserHandler>();
        return services;
    }
}
