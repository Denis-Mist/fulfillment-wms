using Microsoft.Extensions.Hosting;
using Wms.Application.Integrations;
using Wms.Domain.Enums;

namespace Wms.Infrastructure.Marketplaces;

public sealed class MarketplaceOptions
{
    public const string Section = "Marketplace";

    /// <summary>Если не задано — Sandbox в Development, иначе Production.</summary>
    public MarketplaceEnvironment? DefaultEnvironment { get; set; }
    public bool AllowProductionStockPush { get; set; }
    public string? ProtectionKey { get; set; }
    public WbHosts Wb { get; set; } = new();
    public OzonHosts Ozon { get; set; } = new();

    public sealed class WbHosts
    {
        public string SandboxMarketplaceUrl { get; set; } = "https://marketplace-api-sandbox.wildberries.ru";
        public string SandboxContentUrl { get; set; } = "https://content-api-sandbox.wildberries.ru";
        public string SandboxStatisticsUrl { get; set; } = "https://statistics-api-sandbox.wildberries.ru";
        public string ProductionMarketplaceUrl { get; set; } = "https://marketplace-api.wildberries.ru";
        public string ProductionContentUrl { get; set; } = "https://content-api.wildberries.ru";
        public string ProductionStatisticsUrl { get; set; } = "https://statistics-api.wildberries.ru";

        public Uri Marketplace(MarketplaceEnvironment env) => new(Trim(env == MarketplaceEnvironment.Sandbox ? SandboxMarketplaceUrl : ProductionMarketplaceUrl) + "/");
        public Uri Content(MarketplaceEnvironment env) => new(Trim(env == MarketplaceEnvironment.Sandbox ? SandboxContentUrl : ProductionContentUrl) + "/");
        public Uri Statistics(MarketplaceEnvironment env) => new(Trim(env == MarketplaceEnvironment.Sandbox ? SandboxStatisticsUrl : ProductionStatisticsUrl) + "/");

        private static string Trim(string value) => value.TrimEnd('/');
    }

    public sealed class OzonHosts
    {
        public string BaseUrl { get; set; } = "https://api-seller.ozon.ru";
        /// <summary>Отдельного sandbox URL в официальной документации Seller API нет.</summary>
        public string? SandboxBaseUrl { get; set; }
    }
}

public static class MarketplaceClientNames
{
    public const string Wb = "marketplace-wb";
    public const string Ozon = "marketplace-ozon";
}

public sealed class MarketplaceSettings(Microsoft.Extensions.Options.IOptions<MarketplaceOptions> options, Microsoft.Extensions.Hosting.IHostEnvironment env) : IMarketplaceSettings
{
    public MarketplaceEnvironment DefaultEnvironment =>
        options.Value.DefaultEnvironment ?? (env.IsDevelopment() ? MarketplaceEnvironment.Sandbox : MarketplaceEnvironment.Production);

    public bool AllowProductionStockPush => options.Value.AllowProductionStockPush;
}
