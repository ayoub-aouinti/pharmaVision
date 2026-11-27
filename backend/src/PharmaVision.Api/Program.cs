using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using PharmaVision.Api.Hubs;
using PharmaVision.Api.Services;
using PharmaVision.Infrastructure;
using PharmaVision.Infrastructure.Seeding;

var builder = WebApplication.CreateBuilder(args);

var connection = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Chaîne de connexion « Default » manquante");

builder.Services.AddDbContext<PharmaDbContext>(o => o.UseNpgsql(connection));
builder.Services.AddControllers().AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSignalR().AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<LiveNotifier>();
builder.Services.AddScoped<KpiService>();
builder.Services.AddScoped<DataSeeder>();
builder.Services.AddHostedService<AlertEngine>();

var app = builder.Build();

// Itération 1 : pas d'authentification, l'API reste réservée au poste de développement.
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<PharmaDbContext>();
    db.Database.SetCommandTimeout(TimeSpan.FromMinutes(5));
    await db.Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<DataSeeder>()
        .SeedAsync(app.Configuration.GetValue("Seed:RandomSeed", 2026));

    app.MapOpenApi();
    app.UseSwaggerUI(o => o.SwaggerEndpoint("/openapi/v1.json", "PharmaVision API"));
}

app.MapControllers();
app.MapHub<LiveHub>("/hubs/live");

app.Run();
