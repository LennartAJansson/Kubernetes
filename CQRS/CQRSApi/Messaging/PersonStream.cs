using CQRSApi.Contracts;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

namespace CQRSApi.Messaging;

/// <summary>
/// Säkerställer att JetStream-strömmen finns. Både API:t och workern gör detta
/// vid start med exakt samma konfiguration, så det spelar ingen roll vilken
/// av dem som startar först i klustret.
/// </summary>
public sealed class PersonStreamSetup(INatsJSContext js, NatsSettings settings, ILogger<PersonStreamSetup> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var config = new StreamConfig(settings.Stream, [PersonSubjects.All])
                {
                    // WorkQueue: ett meddelande tas bort när workern har kvitterat
                    // (ack) det. Kommandon ska utföras en gång - inte sparas för evigt.
                    Retention = StreamConfigRetention.Workqueue,
                    Storage = StreamConfigStorage.File,
                };

                await js.CreateOrUpdateStreamAsync(config, stoppingToken);
                logger.LogInformation("JetStream-strömmen {Stream} ({Subjects}) är redo", settings.Stream, PersonSubjects.All);
                return;
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Kunde inte skapa/uppdatera strömmen {Stream} - försöker igen om 5 s", settings.Stream);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }
}
