using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using PharmaVision.Api.Hubs;
using PharmaVision.Api.Security;
using PharmaVision.Api.Services;
using PharmaVision.Api.Services.Reports;
using PharmaVision.Infrastructure;
using PharmaVision.Infrastructure.Seeding;

var builder = WebApplication.CreateBuilder(args);

var connection = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Chaîne de connexion « Default » manquante");

builder.Services.AddDbContext<PharmaDbContext>(o => o.UseNpgsql(connection));
builder.Services.AddControllers(o => o.Filters.Add<PharmacyScopeFilter>()).AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSignalR().AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o =>
{
    o.Cookie.Name = "pharmavision";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    o.ExpireTimeSpan = TimeSpan.FromHours(12);
    o.Events.OnValidatePrincipal = Access.ValidateSessionAsync;
    // API : des codes 401 / 403 plutôt que des redirections vers une page de connexion.
    o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
});
// Tout point d'entrée exige une session, sauf mention contraire explicite.
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
});
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<LiveNotifier>();
builder.Services.AddScoped<KpiService>();
builder.Services.AddScoped<AnalyticsService>();
builder.Services.AddScoped<ReportBuilder>();
builder.Services.AddScoped<HistoryService>();
builder.Services.AddScoped<ImportService>();
builder.Services.AddScoped<DataSeeder>();
builder.Services.AddHostedService<AlertEngine>();
builder.Services.AddHostedService<RetentionService>();

var app = builder.Build();

// Migration et données fictives : environnement de développement uniquement.
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<PharmaDbContext>();
    db.Database.SetCommandTimeout(TimeSpan.FromMinutes(5));
    await db.Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<DataSeeder>()
        .SeedAsync(app.Configuration.GetValue("Seed:RandomSeed", 2026));
    if (app.Configuration["Seed:DemoPassword"] is { Length: > 0 } password)
        await scope.ServiceProvider.GetRequiredService<DataSeeder>().SeedUsersAsync(password);

    app.MapOpenApi().AllowAnonymous();
    app.UseSwaggerUI(o => o.SwaggerEndpoint("/openapi/v1.json", "PharmaVision API"));
}

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<LiveHub>("/hubs/live");

app.Run();
