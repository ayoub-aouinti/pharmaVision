using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using PharmaVision.Domain;
using PharmaVision.Infrastructure;

namespace PharmaVision.Tests;

/// <summary>Contrat utilisé par les boîtiers (simulateur et boîtier Python).</summary>
public class IngestTests : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3];

    private readonly ApiFactory _api = new();

    public void Dispose() => _api.Dispose();

    private HttpClient Device(int n)
    {
        var client = _api.Anonymous();
        client.DefaultRequestHeaders.Add("X-Api-Key", $"key-{n}");
        return client;
    }

    [Fact]
    public async Task Config_gives_the_zones_and_masks_of_the_device_only()
    {
        var client = Device(1);
        using (var scope = _api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PharmaDbContext>();
            db.Zones.Add(new Zone
            {
                Id = 50, CameraId = 1, Name = "Ordonnances", Type = ZoneType.PrivacyMask,
                Polygon = [new(0, 0), new(0.5, 0), new(0.5, 0.5)],
            });
            await db.SaveChangesAsync();
        }

        var config = await client.GetFromJsonAsync<DeviceConfig>("/api/ingest/config", Json);

        var zone = Assert.Single(config!.Zones);
        Assert.Equal((1, 1), (zone.Id, zone.CameraId));
        var camera = Assert.Single(config.Cameras!);
        Assert.Equal(("rtsp://secret@cam", 3), (camera.RtspUrl, Assert.Single(camera.Masks).Count));
    }

    [Fact]
    public async Task Reference_image_replaces_the_drawing()
    {
        var device = Device(1);

        var sent = await device.PutAsync("/api/ingest/cameras/1/image", new ByteArrayContent(Jpeg));
        var received = await sent.Content.ReadFromJsonAsync<ImageReceived>(Json);
        var config = await device.GetFromJsonAsync<DeviceConfig>("/api/ingest/config", Json);

        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
        Assert.Equal(received!.PhotoAt, config!.Cameras![0].PhotoAt);
        var staff = await _api.SignedInAs("equipe@test");
        var snapshot = await staff.GetAsync("/api/cameras/1/snapshot");
        Assert.Equal("image/jpeg", snapshot.Content.Headers.ContentType!.MediaType);
        Assert.Equal(Jpeg, await snapshot.Content.ReadAsByteArrayAsync());
        Assert.Contains("image de référence", await staff.GetStringAsync("/api/alerts/1/thumbnail"));
    }

    [Fact]
    public async Task Images_are_refused_from_the_wrong_device_or_format()
    {
        async Task<HttpStatusCode> Send(HttpClient client, int camera, byte[] body) =>
            (await client.PutAsync($"/api/ingest/cameras/{camera}/image", new ByteArrayContent(body))).StatusCode;
        var device = Device(1);

        Assert.Equal(HttpStatusCode.NotFound, await Send(device, 2, Jpeg));
        Assert.Equal(HttpStatusCode.BadRequest, await Send(device, 1, "<svg/>"u8.ToArray()));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, await Send(device, 1, [.. Jpeg, .. new byte[2_000_000]]));
        Assert.Equal(HttpStatusCode.Unauthorized, await Send(Device(9), 1, Jpeg));
    }

    [Fact]
    public async Task Python_event_format_is_accepted()
    {
        // Exactement ce qu'écrit edge/pharmavision_edge/client.py.
        var batch = JsonContent.Create(JsonSerializer.Deserialize<JsonElement>(
            $$"""
            {"events":[{"ts":"{{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss.ffffff}}+00:00","zoneId":1,"type":"Pass","value":1,"trackId":"c1-ab12-1"}]}
            """));

        var response = await Device(1).PostAsync("/api/ingest/events", batch);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Contains("\"accepted\":1", await response.Content.ReadAsStringAsync());
    }
}
