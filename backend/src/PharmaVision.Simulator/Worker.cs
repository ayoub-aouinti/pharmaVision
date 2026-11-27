using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using PharmaVision.Domain;

namespace PharmaVision.Simulator;

/// <summary>Se comporte comme les boîtiers réels : configuration, battement de cœur, envoi par lots.</summary>
public class Worker(IOptions<SimulatorOptions> options, ILogger<Worker> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var o = options.Value;
        logger.LogInformation("Simulation de {Count} boîtier(s) vers {Url}, vitesse ×{Speed}",
            o.ApiKeys.Length, o.ApiBaseUrl, o.Speed);
        // Volumes alignés sur le seed : 390, 320 et 270 visiteurs par jour.
        double[] daily = [390, 320, 270];
        return Task.WhenAll(o.ApiKeys.Select((key, i) => RunDeviceAsync(key, daily[i % daily.Length], stoppingToken)));
    }

    private async Task RunDeviceAsync(string apiKey, double dailyVisitors, CancellationToken ct)
    {
        var o = options.Value;
        using var http = new HttpClient { BaseAddress = new Uri(o.ApiBaseUrl), Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.Add("X-Api-Key", apiKey);

        var device = new DeviceSimulator(new Random(), dailyVisitors, o);
        DeviceConfig? config = null;
        var configAt = DateTime.MinValue;
        var previous = DateTime.UtcNow;
        // File locale : les lots non envoyés sont conservés si le serveur est injoignable.
        var pending = new Queue<IngestBatch>();

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(o.TickSeconds));
        while (await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                var now = DateTime.UtcNow;
                if (config is null || now - configAt > TimeSpan.FromSeconds(10))
                {
                    config = await http.GetFromJsonAsync<DeviceConfig>("/api/ingest/config", Json, ct);
                    configAt = now;
                    using var beat = await http.PostAsync("/api/ingest/heartbeat", null, ct);
                    beat.EnsureSuccessStatusCode();
                }

                var batch = device.Tick(config!, now, now - previous);
                previous = now;
                if (batch.Events.Count > 0 || batch.Sales?.Count > 0) pending.Enqueue(batch);
                while (pending.Count > 200) pending.Dequeue();

                while (pending.TryPeek(out var next))
                {
                    using var response = await http.PostAsJsonAsync("/api/ingest/events", next, Json, ct);
                    response.EnsureSuccessStatusCode();
                    pending.Dequeue();
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException
                                       || (ex is TaskCanceledException && !ct.IsCancellationRequested))
            {
                logger.LogWarning("Serveur injoignable ({Message}), nouvelle tentative", ex.Message);
            }
        }
    }
}
