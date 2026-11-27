using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using PharmaVision.Api.Controllers;
using PharmaVision.Domain;
using PharmaVision.Infrastructure;

namespace PharmaVision.Tests;

public class SettingsTests : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ApiFactory _api = new();

    public void Dispose() => _api.Dispose();

    private const string NewPassword = "un-nouveau-mot-de-passe";

    private static Task<HttpResponseMessage> Login(HttpClient client, string email, string password) =>
        client.PostAsJsonAsync("/api/auth/login", new { email, password });

    private static async Task<UserDto> UserAsync(HttpClient owner, string email) =>
        (await owner.GetFromJsonAsync<List<UserDto>>("/api/users", Json))!.Single(u => u.Email == email);

    private static object Changes(UserRole role = UserRole.Equipe, bool active = true, int pharmacy = 1) =>
        new { displayName = "Équipe", role = role.ToString(), pharmacyIds = new[] { pharmacy }, isActive = active };

    [Fact]
    public async Task Account_is_locked_after_repeated_failures()
    {
        var client = _api.Anonymous();
        for (var i = 0; i < AccountPolicy.MaxFailedLogins; i++) await Login(client, "equipe@test", "faux");

        var blocked = await Login(client, "equipe@test", ApiFactory.Password);

        Assert.Equal(HttpStatusCode.Unauthorized, blocked.StatusCode);
        // Le titulaire lève le blocage en fixant un nouveau mot de passe.
        var owner = await _api.SignedInAs("titulaire@test");
        var staff = await UserAsync(owner, "equipe@test");
        Assert.True(staff.Locked);
        await owner.PostAsJsonAsync($"/api/users/{staff.Id}/password", new { password = NewPassword });
        Assert.Equal(HttpStatusCode.OK, (await Login(_api.Anonymous(), "equipe@test", NewPassword)).StatusCode);
    }

    [Fact]
    public async Task Changing_the_password_closes_the_other_sessions()
    {
        var first = await _api.SignedInAs("equipe@test");
        var second = await _api.SignedInAs("equipe@test");

        var wrong = await first.PostAsJsonAsync("/api/auth/password", new { currentPassword = "faux", newPassword = NewPassword });
        var weak = await first.PostAsJsonAsync("/api/auth/password",
            new { currentPassword = ApiFactory.Password, newPassword = "court" });
        var done = await first.PostAsJsonAsync("/api/auth/password",
            new { currentPassword = ApiFactory.Password, newPassword = NewPassword });

        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, done.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await first.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await second.GetAsync("/api/auth/me")).StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Disabling_or_deleting_an_account_ends_its_session_at_once(bool delete)
    {
        var staff = await _api.SignedInAs("equipe@test");
        var owner = await _api.SignedInAs("titulaire@test");
        var id = (await UserAsync(owner, "equipe@test")).Id;

        var response = delete
            ? await owner.DeleteAsync($"/api/users/{id}")
            : await owner.PutAsJsonAsync($"/api/users/{id}", Changes(active: false));

        response.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await staff.GetAsync("/api/alerts?pharmacyId=1")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await Login(_api.Anonymous(), "equipe@test", ApiFactory.Password)).StatusCode);
    }

    [Fact]
    public async Task Owner_creates_accounts_within_their_pharmacies_only()
    {
        var owner = await _api.SignedInAs("titulaire@test");
        object Account(string email, int pharmacy) => new
        {
            email, displayName = "Nouveau", role = "Equipe", pharmacyIds = new[] { pharmacy }, password = NewPassword,
        };

        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsJsonAsync("/api/users", Account("Nouveau@Test", 1))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsJsonAsync("/api/users", Account("nouveau@test", 1))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.PostAsJsonAsync("/api/users", Account("autre@test", 2))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Login(_api.Anonymous(), "nouveau@test", NewPassword)).StatusCode);
    }

    [Fact]
    public async Task Owner_cannot_change_their_own_rights_or_reach_foreign_accounts()
    {
        var owner = await _api.SignedInAs("titulaire@test");
        var self = await UserAsync(owner, "titulaire@test");
        using (var scope = _api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PharmaDbContext>();
            db.Users.Add(new User { Id = 99, Email = "voisin@test", DisplayName = "Voisin", PharmacyIds = [1, 2] });
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PutAsJsonAsync($"/api/users/{self.Id}", Changes())).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.DeleteAsync($"/api/users/{self.Id}")).StatusCode);
        // Un compte qui couvre une officine hors du périmètre reste invisible.
        Assert.DoesNotContain("voisin", await owner.GetStringAsync("/api/users"));
        Assert.Equal(HttpStatusCode.NotFound, (await owner.PutAsJsonAsync("/api/users/99", Changes())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.DeleteAsync("/api/users/99")).StatusCode);
    }

    [Theory]
    [InlineData("GET", "/api/users")]
    [InlineData("GET", "/api/devices?pharmacyId=1")]
    [InlineData("POST", "/api/devices/1/key")]
    [InlineData("DELETE", "/api/users/1")]
    public async Task Staff_cannot_open_the_settings(string method, string url)
    {
        var staff = await _api.SignedInAs("equipe@test");

        var response = await staff.SendAsync(new HttpRequestMessage(new HttpMethod(method), url));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Retention_settings_are_bounded_and_hide_old_images()
    {
        var owner = await _api.SignedInAs("titulaire@test");
        object Settings(int days, int months) =>
            new { name = "Pharmacie", city = "Ville", thumbnailDays = days, statisticsMonths = months };
        using (var scope = _api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PharmaDbContext>();
            db.Alerts.Single(a => a.Id == 1).StartedAt = DateTime.UtcNow.AddDays(-10);
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/api/alerts/1/thumbnail")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PutAsJsonAsync("/api/pharmacies/1", Settings(7, 1))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.PutAsJsonAsync("/api/pharmacies/2", Settings(7, 12))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PutAsJsonAsync("/api/pharmacies/1", Settings(7, 12))).StatusCode);
        Assert.Equal(HttpStatusCode.Gone, (await owner.GetAsync("/api/alerts/1/thumbnail")).StatusCode);
    }

    [Fact]
    public async Task Device_key_is_shown_once_and_renewal_revokes_the_old_one()
    {
        var owner = await _api.SignedInAs("titulaire@test");
        async Task<HttpStatusCode> Heartbeat(string key)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/ingest/heartbeat");
            request.Headers.Add("X-Api-Key", key);
            return (await _api.Anonymous().SendAsync(request)).StatusCode;
        }

        var created = await (await owner.PostAsJsonAsync("/api/devices", new { pharmacyId = 1, name = "Boîtier vidéo" }))
            .Content.ReadFromJsonAsync<DeviceKey>();
        Assert.Equal(HttpStatusCode.NoContent, await Heartbeat(created!.Key));
        Assert.DoesNotContain(created.Key, await owner.GetStringAsync("/api/devices?pharmacyId=1"));

        var renewed = await (await owner.PostAsync($"/api/devices/{created.DeviceId}/key", null))
            .Content.ReadFromJsonAsync<DeviceKey>();

        Assert.Equal(HttpStatusCode.Unauthorized, await Heartbeat(created.Key));
        Assert.Equal(HttpStatusCode.NoContent, await Heartbeat(renewed!.Key));
        Assert.Equal(HttpStatusCode.Conflict, (await owner.DeleteAsync("/api/devices/1")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/devices/{created.DeviceId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.DeleteAsync("/api/devices/2")).StatusCode);
    }
}
