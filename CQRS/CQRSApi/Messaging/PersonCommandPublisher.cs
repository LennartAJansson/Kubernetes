using CQRSApi.Contracts;
using NATS.Client.JetStream;

namespace CQRSApi.Messaging;

/// <summary>
/// Skriv-sidan (Command) i API:t. API:t ändrar ALDRIG databasen själv - det
/// lägger bara ett kommando på JetStream och svarar 202 Accepted. Workern
/// utför sedan ändringen.
/// </summary>
public sealed class PersonCommandPublisher(INatsJSContext js, ILogger<PersonCommandPublisher> logger)
{
    public async Task<PersonCommand> PublishAsync(string subject, PersonCommand command, CancellationToken ct)
    {
        // MsgId = CommandId gör att JetStream ignorerar en dubblett av samma
        // kommando inom dubblettfönstret (standard 2 minuter).
        var ack = await js.PublishAsync(
            subject,
            command,
            opts: new NatsJSPubOpts { MsgId = command.CommandId.ToString() },
            cancellationToken: ct);

        // Kastar om strömmen inte tog emot meddelandet (t.ex. om strömmen saknas).
        ack.EnsureSuccess();

        logger.LogInformation(
            "Publicerade {Subject} för person {PersonId} (kommando {CommandId}, ström {Stream}, sekvens {Seq})",
            subject, command.PersonId, command.CommandId, ack.Stream, ack.Seq);

        return command;
    }
}
