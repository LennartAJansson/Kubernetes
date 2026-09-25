using CQRSWorker.Contracts;
using CQRSWorker.Data;
using Microsoft.EntityFrameworkCore;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

namespace CQRSWorker;

/// <summary>
/// Kommando-hanteraren i CQRS-mönstret. Läser kommandon från JetStream och
/// utför dem mot databasen med EF Core. Varje meddelande kvitteras (ack) först
/// när ändringen är sparad - kraschar podden innan dess levererar JetStream
/// meddelandet igen till nästa pod som startar.
/// </summary>
public class Worker(
    INatsJSContext js,
    IDbContextFactory<PersonsDbContext> dbFactory,
    NatsSettings settings,
    ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RetryAsync("skapa databasschemat", EnsureDatabaseAsync, stoppingToken);
        await RetryAsync("skapa JetStream-strömmen", EnsureStreamAsync, stoppingToken);

        INatsJSConsumer? consumer = null;
        await RetryAsync("skapa konsumenten", async ct =>
        {
            consumer = await js.CreateOrUpdateConsumerAsync(
                settings.Stream,
                new ConsumerConfig(settings.Consumer) { FilterSubject = PersonSubjects.All },
                ct);
        }, stoppingToken);

        logger.LogInformation("Lyssnar på {Subjects} i strömmen {Stream} som konsument {Consumer}",
            PersonSubjects.All, settings.Stream, settings.Consumer);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await foreach (var msg in consumer!.ConsumeAsync<PersonCommand>(cancellationToken: stoppingToken))
                {
                    await HandleAsync(msg, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Konsumtionen avbröts - startar om om 5 s");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task HandleAsync(INatsJSMsg<PersonCommand> msg, CancellationToken ct)
    {
        if (msg.Data is not { } command)
        {
            // Går inte att tolka - kommer aldrig att lyckas, så sluta leverera det.
            logger.LogWarning("Ogiltigt meddelande på {Subject} - kastas", msg.Subject);
            await msg.AckTerminateAsync(cancellationToken: ct);
            return;
        }

        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);

            var result = msg.Subject switch
            {
                PersonSubjects.Create => await CreateAsync(db, command, ct),
                PersonSubjects.Update => await UpdateAsync(db, command, ct),
                PersonSubjects.Delete => await DeleteAsync(db, command, ct),
                _ => "okänt subjekt - ignoreras",
            };

            await msg.AckAsync(cancellationToken: ct);
            logger.LogInformation("{Subject} person {PersonId}: {Result} (kommando {CommandId})",
                msg.Subject, command.PersonId, result, command.CommandId);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // T.ex. databasen nere: be JetStream leverera igen om en stund.
            logger.LogError(ex, "Kunde inte utföra {Subject} för {PersonId} - försöker igen om 5 s",
                msg.Subject, command.PersonId);
            await msg.NakAsync(delay: TimeSpan.FromSeconds(5), cancellationToken: ct);
        }
    }

    // Alla hanterare är idempotenta: samma kommando två gånger ger samma
    // slutresultat. Det behövs eftersom JetStream garanterar "minst en gång".

    private static async Task<string> CreateAsync(PersonsDbContext db, PersonCommand cmd, CancellationToken ct)
    {
        if (await db.Persons.AnyAsync(p => p.Id == cmd.PersonId, ct))
            return "fanns redan (dubblett ignoreras)";

        db.Persons.Add(new Person
        {
            Id = cmd.PersonId,
            FirstName = cmd.FirstName ?? "",
            LastName = cmd.LastName ?? "",
            Email = cmd.Email,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);
        return "skapad";
    }

    private static async Task<string> UpdateAsync(PersonsDbContext db, PersonCommand cmd, CancellationToken ct)
    {
        var person = await db.Persons.FindAsync([cmd.PersonId], ct);
        if (person is null)
            return "finns inte - ignoreras";

        person.FirstName = cmd.FirstName ?? person.FirstName;
        person.LastName = cmd.LastName ?? person.LastName;
        person.Email = cmd.Email;
        person.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return "uppdaterad";
    }

    private static async Task<string> DeleteAsync(PersonsDbContext db, PersonCommand cmd, CancellationToken ct)
    {
        var person = await db.Persons.FindAsync([cmd.PersonId], ct);
        if (person is null)
            return "fanns inte - inget att ta bort";

        db.Persons.Remove(person);
        await db.SaveChangesAsync(ct);
        return "borttagen";
    }

    private async Task EnsureDatabaseAsync(CancellationToken ct)
    {
        // För en demo räcker EnsureCreated. I ett riktigt system: EF-migreringar.
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var created = await db.Database.EnsureCreatedAsync(ct);
        logger.LogInformation(created ? "Databasschemat skapades" : "Databasschemat fanns redan");
    }

    private async Task EnsureStreamAsync(CancellationToken ct)
    {
        // Exakt samma konfiguration som i CQRSApi (PersonStreamSetup).
        var config = new StreamConfig(settings.Stream, [PersonSubjects.All])
        {
            Retention = StreamConfigRetention.Workqueue,
            Storage = StreamConfigStorage.File,
        };
        await js.CreateOrUpdateStreamAsync(config, ct);
    }

    /// <summary>MySQL och NATS kan starta efter workern i klustret - vänta in dem.</summary>
    private async Task RetryAsync(string what, Func<CancellationToken, Task> action, CancellationToken ct)
    {
        while (true)
        {
            try
            {
                await action(ct);
                return;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning("Kunde inte {What} ({Error}) - försöker igen om 5 s", what, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
        }
    }
}
