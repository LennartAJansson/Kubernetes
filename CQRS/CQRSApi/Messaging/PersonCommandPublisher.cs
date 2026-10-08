using System.Diagnostics;
using CQRSApi.Contracts;
using CQRSApi.Telemetry;
using NATS.Client.JetStream;

namespace CQRSApi.Messaging;

/// <summary>
/// Skriv-sidan (Command) i API:t. API:t ändrar ALDRIG databasen själv - det
/// lägger bara ett kommando på JetStream och svarar 202 Accepted. Workern
/// utför sedan ändringen.
/// </summary>
public sealed class PersonCommandPublisher(INatsJSContext js, CommandTelemetry telemetry, ILogger<PersonCommandPublisher> logger)
{
    public async Task<PersonCommand> PublishAsync(string subject, PersonCommand command, CancellationToken ct)
    {
        var start = Stopwatch.GetTimestamp();

        // MsgId = CommandId gör att JetStream ignorerar en dubblett av samma
        // kommando inom dubblettfönstret (standard 2 minuter).
        // NATS-klienten lägger automatiskt till headern traceparent - den bär
        // trace_id vidare till workern.
        var ack = await js.PublishAsync(
            subject,
            command,
            opts: new NatsJSPubOpts { MsgId = command.CommandId.ToString() },
            cancellationToken: ct);

        // Kastar om strömmen inte tog emot meddelandet (t.ex. om strömmen saknas).
        ack.EnsureSuccess();

        var action = new KeyValuePair<string, object?>("action", subject[(subject.LastIndexOf('.') + 1)..]);
        telemetry.Published.Add(1, action);
        telemetry.PublishDuration.Record(Stopwatch.GetElapsedTime(start).TotalSeconds, action);

        // Koppla ihop spannet med kommandot - sökbart i Tempo med TraceQL:
        //   { span.cqrs.command_id = "<guid>" }
        Activity.Current?.SetTag("cqrs.command_id", command.CommandId.ToString());
        Activity.Current?.SetTag("cqrs.person_id", command.PersonId.ToString());

        logger.LogInformation(
            "Publicerade {Subject} för person {PersonId} (kommando {CommandId}, ström {Stream}, sekvens {Seq})",
            subject, command.PersonId, command.CommandId, ack.Stream, ack.Seq);

        return command;
    }
}
