using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PharmaVision.Api.Services;
using PharmaVision.Domain;
using PharmaVision.Infrastructure;

namespace PharmaVision.Tests;

/// <summary>API réelle sur une base en mémoire : deux officines, un titulaire et une équipe.</summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    public const string Password = "mot-de-passe-de-test";
    private readonly string _database = Guid.NewGuid().ToString();
    private bool _seeded;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", "Host=unused");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IDbContextOptionsConfiguration<PharmaDbContext>>();
            services.AddDbContext<PharmaDbContext>(o => o.UseInMemoryDatabase(_database));
            services.Remove(services.Single(s => s.ImplementationType == typeof(AlertEngine)));
        });
    }

    public HttpClient Anonymous()
    {
        var client = CreateClient();
        if (!_seeded) Seed();
        return client;
    }

    public async Task<HttpClient> SignedInAs(string email)
    {
        var client = Anonymous();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password });
        response.EnsureSuccessStatusCode();
        return client;
    }

    private void Seed()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PharmaDbContext>();
        foreach (var id in new[] { 1, 2 })
        {
            db.Pharmacies.Add(new Pharmacy
            {
                Id = id, Name = $"Pharmacie {id}", City = $"Ville {id}",
                Devices =
                [
                    new EdgeDevice
                    {
                        Id = id, Name = "Boîtier", Version = "test", ApiKey = $"key-{id}",
                        Cameras =
                        [
                            new Camera
                            {
                                Id = id, Name = "Cam", Brand = "Axis", RtspUrl = "rtsp://secret@cam", Status = CameraStatus.Online,
                                Zones = [new Zone { Id = id, Name = "Rayon", Type = ZoneType.Shelf }],
                            },
                        ],
                    },
                ],
            });
            db.Alerts.Add(new Alert
            {
                Id = id, PharmacyId = id, CameraId = id, ZoneId = id, Kind = AlertKind.LowFill,
                Title = "Rupture", Detail = "12 %", StartedAt = DateTime.UtcNow, Status = AlertStatus.Open,
            });
        }
        AddUser(db, "titulaire@test", UserRole.Titulaire, 1);
        AddUser(db, "equipe@test", UserRole.Equipe, 1);
        db.SaveChanges();
        _seeded = true;
    }

    private static void AddUser(PharmaDbContext db, string email, UserRole role, int pharmacyId)
    {
        var user = new User { Email = email, DisplayName = email, Role = role, PharmacyIds = [pharmacyId] };
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, Password);
        db.Users.Add(user);
    }
}

file static class ServiceCollectionExtensions
{
    public static void RemoveAll<T>(this IServiceCollection services)
    {
        foreach (var descriptor in services.Where(s => s.ServiceType == typeof(T)).ToList()) services.Remove(descriptor);
    }
}

public class AccessControlTests : IDisposable
{
    private readonly ApiFactory _api = new();

    public void Dispose() => _api.Dispose();

    [Theory]
    [InlineData("/api/pharmacies")]
    [InlineData("/api/alerts?pharmacyId=1")]
    [InlineData("/api/alerts/history?pharmacyId=1")]
    [InlineData("/api/alerts/1/thumbnail")]
    [InlineData("/api/cameras?pharmacyId=1")]
    [InlineData("/api/cameras/1/snapshot")]
    [InlineData("/api/dashboard/kpis?pharmacyId=1")]
    [InlineData("/api/auth/me")]
    public async Task Anonymous_requests_are_rejected(string url) =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.Anonymous().GetAsync(url)).StatusCode);

    [Fact]
    public async Task Live_hub_requires_a_session() =>
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await _api.Anonymous().PostAsync("/hubs/live/negotiate?negotiateVersion=1", null)).StatusCode);

    [Theory]
    [InlineData("equipe@test", "mauvais")]
    [InlineData("inconnu@test", ApiFactory.Password)]
    public async Task Bad_credentials_are_rejected(string email, string password)
    {
        var response = await _api.Anonymous().PostAsJsonAsync("/api/auth/login", new { email, password });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Repeated_logins_are_throttled()
    {
        var client = _api.Anonymous();
        HttpStatusCode last = default;
        for (var i = 0; i < 11; i++)
            last = (await client.PostAsJsonAsync("/api/auth/login", new { email = "equipe@test", password = "x" })).StatusCode;

        Assert.Equal(HttpStatusCode.TooManyRequests, last);
    }

    [Theory]
    [InlineData("/api/alerts?pharmacyId=2", HttpStatusCode.Forbidden)]
    [InlineData("/api/alerts/history?pharmacyId=2", HttpStatusCode.Forbidden)]
    [InlineData("/api/cameras?pharmacyId=2", HttpStatusCode.Forbidden)]
    [InlineData("/api/dashboard/kpis?pharmacyId=2", HttpStatusCode.Forbidden)]
    [InlineData("/api/dashboard/shelves?pharmacyId=2", HttpStatusCode.Forbidden)]
    [InlineData("/api/alerts/2/thumbnail", HttpStatusCode.NotFound)]
    [InlineData("/api/cameras/2/snapshot", HttpStatusCode.NotFound)]
    public async Task Another_pharmacy_stays_invisible(string url, HttpStatusCode expected)
    {
        var client = await _api.SignedInAs("titulaire@test");

        Assert.Equal(expected, (await client.GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task Another_pharmacy_cannot_be_modified()
    {
        var client = await _api.SignedInAs("titulaire@test");
        var zone = new
        {
            cameraId = 2, name = "Intrus", type = "Shelf",
            polygon = new[] { new { x = 0.1, y = 0.1 }, new { x = 0.2, y = 0.1 }, new { x = 0.2, y = 0.2 } },
        };
        var camera = new { pharmacyId = 2, name = "Intrus", brand = "Axis", rtspUrl = "rtsp://x" };

        Assert.Equal(HttpStatusCode.NotFound,
            (await client.PostAsJsonAsync("/api/alerts/2/action", new { action = "resolve" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/api/zones", zone)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync("/api/zones/2", zone)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync("/api/zones/2")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/cameras", camera)).StatusCode);
        // La caméra 1 est la sienne mais le formulaire ne permet pas de la déplacer ailleurs.
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync("/api/cameras/2", camera)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/api/cameras/2/reference-photo", null)).StatusCode);
    }

    [Fact]
    public async Task Staff_handles_alerts_but_cannot_change_the_configuration()
    {
        var client = await _api.SignedInAs("equipe@test");

        var action = await client.PostAsJsonAsync("/api/alerts/1/action", new { action = "restock" });
        Assert.Equal(HttpStatusCode.OK, action.StatusCode);
        Assert.Contains("equipe@test", await action.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync("/api/zones/1")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync("/api/cameras/1")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/api/cameras/discover?pharmacyId=1", null)).StatusCode);

        var cameras = await client.GetStringAsync("/api/cameras?pharmacyId=1");
        Assert.DoesNotContain("secret", cameras);
    }

    [Fact]
    public async Task Owner_sees_the_stream_address_and_logout_ends_the_session()
    {
        var client = await _api.SignedInAs("titulaire@test");
        Assert.Contains("secret", await client.GetStringAsync("/api/cameras?pharmacyId=1"));

        await client.PostAsync("/api/auth/logout", null);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public void Alert_statistics_measure_reaction_and_resolution_times()
    {
        var start = new DateTime(2026, 11, 27, 9, 0, 0, DateTimeKind.Utc);
        Alert Build(AlertStatus status, int? ackMinutes, int? endMinutes) => new()
        {
            Title = "t", Detail = "d", StartedAt = start, Status = status,
            AcknowledgedAt = ackMinutes is { } a ? start.AddMinutes(a) : null,
            EndedAt = endMinutes is { } e ? start.AddMinutes(e) : null,
        };

        var stats = AlertStats.Of([
            Build(AlertStatus.Open, null, null),
            Build(AlertStatus.Resolved, 2, 10),
            Build(AlertStatus.Resolved, null, 30),
        ]);

        Assert.Equal((3, 1, 2, 1), (stats.Total, stats.Active, stats.Resolved, stats.AutoResolved));
        Assert.Equal(120, stats.AvgAcknowledgeSeconds);
        Assert.Equal(1200, stats.AvgResolutionSeconds);
    }
}
