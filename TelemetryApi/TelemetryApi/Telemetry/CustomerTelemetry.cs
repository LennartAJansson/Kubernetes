using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace TelemetryApi.Telemetry;

/// <summary>
/// Egen telemetri för kunddomänen - utöver den automatiska instrumenteringen.
///
///   ActivitySource -> egna spans (System.Diagnostics, ingen OpenTelemetry-typ!)
///   Meter          -> egna metrics, i Prometheus som otel_customers_*
///
/// Namnet måste registreras med AddSource/AddMeter i TelemetryExtensions,
/// annars plockar OpenTelemetry inte upp dem.
/// </summary>
public sealed class CustomerTelemetry
{
    public const string Name = "TelemetryApi.Customers";

    public static readonly ActivitySource ActivitySource = new(Name);

    public Counter<long> Created { get; }
    public Counter<long> Updated { get; }
    public Counter<long> Deleted { get; }
    public Counter<long> ValidationFailed { get; }
    public Histogram<long> RequestDuration { get; }

    public CustomerTelemetry(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(Name);

        Created = meter.CreateCounter<long>("customers.created", unit: "{customer}", description: "Antal skapade kunder");
        Updated = meter.CreateCounter<long>("customers.updated", unit: "{customer}", description: "Antal uppdaterade kunder");
        Deleted = meter.CreateCounter<long>("customers.deleted", unit: "{customer}", description: "Antal raderade kunder");
        ValidationFailed = meter.CreateCounter<long>("customers.validation_failed", unit: "{request}", description: "Anrop som stoppades av valideringen");
        // Histogram ger bucket-/sum-/count-serier i Prometheus, s\u00E5 du kan
        // r\u00E4kna ut percentiler (p50/p95/p99) ist\u00E4llet f\u00F6r bara senaste v\u00E4rdet.
        RequestDuration = meter.CreateHistogram<long>("customers.requestduration", unit: "ms", description: "Tid för request i millisekunder");
    }
}
