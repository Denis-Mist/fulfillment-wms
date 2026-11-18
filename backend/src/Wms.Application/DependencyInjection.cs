using Microsoft.Extensions.DependencyInjection;
using Wms.Application.Abstractions;
using Wms.Application.Admin;
using Wms.Application.Auth;
using Wms.Application.Catalog;
using Wms.Application.Clients;
using Wms.Application.Common;
using Wms.Application.Directory;
using Wms.Application.Notifications;
using Wms.Application.Partners;
using Wms.Application.Search;
using Wms.Application.Settings;
using Wms.Application.Warehouses;

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
        services.AddScoped<IAuditEventSource, Inbound.InboundAuditSource>();
        services.AddScoped<Inbound.InboundScope>();
        services.AddScoped<Inbound.InboundQueryHandler>();
        services.AddScoped<Inbound.InboundActionHandler>();
        services.AddSingleton<Inbound.IReceivingInspectorFactory, Inbound.RandomReceivingInspectorFactory>();
        services.AddScoped<Orders.OrderScope>();
        services.AddScoped<Orders.OrderQueryHandler>();
        services.AddScoped<Orders.OrderSales>();
        services.AddScoped<IAuditEventSource, Orders.ShippedOrdersAuditSource>();
        services.AddScoped<Tasks.TaskScope>();
        services.AddScoped<Tasks.WorkerDirectory>();
        services.AddScoped<Tasks.TaskQueryHandler>();
        services.AddScoped<Tasks.TaskCommandHandler>();
        services.AddScoped<Tasks.PickingHandler>();
        services.AddScoped<Tasks.WorkTerminalHandler>();
        services.AddScoped<Stock.StockReadModel>();
        services.AddScoped<Stock.StockQueryHandler>();
        services.AddScoped<Stock.StockOperationQueryHandler>();
        services.AddScoped<Stock.StockCommandHandler>();
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
        services.AddScoped<INotificationSource, Orders.OverdueOrdersNotificationSource>();
        services.AddScoped<NotificationsHandler>();
        services.AddScoped<GlobalSearchHandler>();
        services.AddScoped<ISearchProvider, Orders.OrderSearchProvider>();
        services.AddScoped<ISearchProvider, ProductSearchProvider>();

        // Warehouses
        services.AddScoped<WarehouseScope>();
        services.AddScoped<WarehouseListHandler>();
        services.AddScoped<ZonesQueryHandler>();
        services.AddScoped<CreateZoneHandler>();
        services.AddScoped<LocationsQueryHandler>();
        services.AddScoped<CreateLocationHandler>();
        services.AddScoped<WarehouseMapQueryHandler>();
        services.AddScoped<WarehouseProductsQueryHandler>();
        services.AddScoped<PutawayCheckHandler>();

        // Catalog
        services.AddScoped<CatalogReader>();
        services.AddScoped<ProductListHandler>();
        services.AddScoped<ProductFacetsHandler>();
        services.AddScoped<ProductDetailsHandler>();
        services.AddScoped<ProductCommandsHandler>();
        services.AddScoped<Staff.StaffHandler>();
        services.AddScoped<Fbo.FboHandler>();
        services.AddScoped<Returns.ReturnsHandler>();
        services.AddScoped<Billing.BillingHandler>();
        services.AddScoped<Integrations.IntegrationsHandler>();
        services.AddScoped<Dashboard.DashboardHandler>();
        services.AddScoped<Analytics.AnalyticsHandler>();
        return services;
    }
}
