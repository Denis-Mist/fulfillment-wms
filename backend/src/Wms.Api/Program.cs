using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Wms.Api.Endpoints;
using Wms.Api.Infrastructure;
using Wms.Application;
using Wms.Infrastructure;
using Wms.Infrastructure.Security;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    o.SerializerOptions.Converters.Add(new UtcDateTimeOffsetConverter());
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false));
});
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ProblemDetailsExceptionHandler>();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IRequestContextAccessor, HttpRequestContextAccessor>();
builder.Services.AddScoped<IAuthorizationHandler, RoleRequirementHandler>();
builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, ProblemDetailsAuthorizationResultHandler>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((o, jwt) =>
    {
        o.MapInboundClaims = false;
        o.TokenValidationParameters = jwt.Value.ValidationParameters();
    });
builder.Services.AddAuthorization(Policies.Register);

const string CorsPolicy = "frontend";
builder.Services.AddCors(o => o.AddPolicy(CorsPolicy, p => p
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? DefaultCorsOrigins)
    .AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors(CorsPolicy);
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
var api = app.MapGroup("/api");
api.MapAuthEndpoints();
api.MapAdminEndpoints();
api.MapOrganizationEndpoints();
api.MapWarehouseEndpoints();
api.MapInboundEndpoints();
api.MapStockEndpoints();
api.MapOrderEndpoints();
api.MapTaskEndpoints();
api.MapStaffEndpoints();
api.MapFboEndpoints();
api.MapReturnsEndpoints();
api.MapBillingEndpoints();
api.MapIntegrationsEndpoints();
api.MapDashboardEndpoints();
api.MapAnalyticsEndpoints();

if (app.Configuration.GetValue("Database:MigrateOnStartup", true))
{
    await app.Services.MigrateAndSeedAsync(app.Configuration.GetValue("Database:Seed", true));
    // Акты/тарифы договоров создаются один раз при старте, а не лениво в GET-запросах биллинга.
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<Wms.Application.Billing.BillingHandler>().ProvisionAsync(default);
}

app.Run();

public partial class Program
{
    /// <summary>Источники фронтенда по умолчанию (Vite dev/preview), если Cors:Origins не задан в конфигурации.</summary>
    internal static readonly string[] DefaultCorsOrigins =
        ["http://localhost:5173", "http://127.0.0.1:5173", "http://localhost:5174", "http://127.0.0.1:5174"];
}
