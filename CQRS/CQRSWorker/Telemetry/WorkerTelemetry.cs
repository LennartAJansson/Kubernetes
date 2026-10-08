using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace CQRSWorker.Telemetry;

/// <summary>
/// Egna spans och metrics för kommando-hanteringen.
///
///   cqrs.commands.processed  - utförda kommandon per action och outcome
///   cqrs.command.duration    - hur lång tid själva utförandet tog
///   cqrs.command.queue_time  - hur länge kommandot låg i kön, från att API:t
///                              skapade det (IssuedAt) till att workern tog det.
///                              Det är "eventual" i eventual consistency - i siffror.
/// </summary>
public sealed class WorkerTelemetry
{
    public const string Name = "CQRSWorker.Commands";

    public static readonly ActivitySource ActivitySource = new(Name);

    private static readonly double[] SecondsBuckets = [0.001, 0.0025, 0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10, 30];

    public Counter<long> Processed { get; }
    public Histogram<double> Duration { get; }
    public Histogram<double> QueueTime { get; }

    public WorkerTelemetry(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(Name);
        Processed = meter.CreateCounter<long>("cqrs.commands.processed", unit: "{command}",
            description: "Kommandon som workern har hanterat");
        Duration = meter.CreateHistogram<double>("cqrs.command.duration", unit: "s",
            description: "Tid för att utföra ett kommando mot databasen",
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = SecondsBuckets });
        QueueTime = meter.CreateHistogram<double>("cqrs.command.queue_time", unit: "s",
            description: "Tid från att API:t skapade kommandot till att workern började utföra det",
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = SecondsBuckets });
    }
}
