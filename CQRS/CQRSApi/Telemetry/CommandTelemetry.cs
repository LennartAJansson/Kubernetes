using System.Diagnostics.Metrics;

namespace CQRSApi.Telemetry;

/// <summary>
/// Egna metrics för skriv-sidan i API:t. Jämför med CQRSWorker:s
/// cqrs.commands.processed - skillnaden mellan publicerade och utförda
/// kommandon är det som ligger i kön just nu.
/// </summary>
public sealed class CommandTelemetry
{
    public const string Name = "CQRSApi.Commands";

    /// <summary>Kommandon som lagts på JetStream, taggade med action (create/update/delete).</summary>
    public Counter<long> Published { get; }

    /// <summary>Hur lång tid publiceringen till JetStream tog (inklusive kvittot från strömmen).</summary>
    public Histogram<double> PublishDuration { get; }

    public CommandTelemetry(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(Name);
        Published = meter.CreateCounter<long>("cqrs.commands.published", unit: "{command}",
            description: "Kommandon publicerade på JetStream");
        PublishDuration = meter.CreateHistogram<double>("cqrs.command.publish.duration", unit: "s",
            description: "Tid för att publicera ett kommando och få kvitto från JetStream",
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = [0.001, 0.0025, 0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1] });
    }
}
