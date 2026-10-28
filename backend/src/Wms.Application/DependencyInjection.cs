using Microsoft.Extensions.DependencyInjection;
using Wms.Application.Abstractions;
using Wms.Application.Admin;
using Wms.Application.Auth;
using Wms.Application.Clients;
using Wms.Application.Directory;
using Wms.Application.Notifications;
using Wms.Application.Partners;
using Wms.Application.Search;
using Wms.Application.Settings;

namespace Wms.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Auth
        services.AddScoped<IRegisterRequestValidator, RegisterRequestValidator>();
        services.AddScoped<ICurrentUserMapper, CurrentUserMapper>();
        services.AddScoped<IRequestContextResolver, RequestContextResolver>();
        services.AddScoped<LoginHandler>();
        services.AddScoped<RegisterHandler>();
        services.AddScoped<GetCurrentUserHandler>();

        // Admin
        services.AddScoped<IOrganizationOwnerLookup, OrganizationOwnerLookup>();
        services.AddScoped<AdminOrganizationsQueryHandler>();
        services.AddScoped<ModerateOrganizationHandler>();
        services.AddScoped<OrganizationDetailsHandler>();
        services.AddScoped<PlatformUsersQueryHandler>();
        services.AddScoped<PlatformUserActionHandler>();
        services.AddScoped<IAuditLog, AuditLog>();
        services.AddScoped<IAuditEventSource, OrganizationRegistrationAuditSource>();
        services.AddScoped<IAuditEventSource, StoredAuditSource>();
        services.AddScoped<IAuditFeed, AuditFeed>();
        services.AddScoped<AuditQueryHandler>();
        services.AddScoped<ReferencesQueryHandler>();
        services.AddScoped<CreateReferenceHandler>();
        services.AddScoped<ToggleReferenceHandler>();
        services.AddScoped<PlatformOverviewHandler>();

        // Directory / clients / partners
        services.AddScoped<ClientOptionsQueryHandler>();
        services.AddScoped<ClientItemBuilder>();
        services.AddScoped<FcClientsQueryHandler>();
        services.AddScoped<FcClientDetailsHandler>();
        services.AddScoped<ClientRequestsQueryHandler>();
        services.AddScoped<DecideClientRequestHandler>();
        services.AddScoped<FulfillmentCardBuilder>();
        services.AddScoped<FulfillmentCardsQueryHandler>();
        services.AddScoped<ConnectFulfillmentHandler>();

        // Settings
        services.AddSingleton<IOrgProfileValidator, OrgProfileValidator>();
        services.AddScoped<OrgSettingsHandler>();
        services.AddScoped<TeamQueryHandler>();
        services.AddScoped<InviteMemberHandler>();
        services.AddScoped<RevokeInviteHandler>();

        // Notifications / search
        services.AddScoped<INotificationSource, PendingOrganizationsNotificationSource>();
        services.AddScoped<NotificationsHandler>();
        services.AddScoped<GlobalSearchHandler>();
        return services;
    }
}
