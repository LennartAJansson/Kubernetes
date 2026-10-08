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
///
/// De fyra instrumenttyperna, och vad de blir i Prometheus:
///   Counter        - räknar bara uppåt          -> otel_customers_created_total
///   Histogram      - fördelning av värden       -> ..._bucket / ..._sum / ..._count
///   ObservableGauge- ett värde "just nu"        -> otel_customers_count
///   UpDownCounter  - kan gå upp och ner         -> otel_customers_requests_in_progress
/// </summary>
public sealed class CustomerTelemetry
{
    public const string Name = "TelemetryApi.Customers";

    public static readonly ActivitySource ActivitySource = new(Name);

    // Senaste antalet kunder i databasen. Sätts av CustomerCountRefresher och
    // läses av ObservableGauge varje gång metrics exporteras.
    private long _customerCount;

    public Counter<long> Created { get; }
    public Counter<long> Updated { get; }
    public Counter<long> Deleted { get; }
    public Counter<long> ValidationFailed { get; }

    /// <summary>
    /// Tid per operation i millisekunder. Taggarna operation (list, get, create,
    /// update, delete, search, demo.slow) och outcome (ok, not_found, invalid)
    /// gör att samma histogram kan delas upp per operation och utfall i Grafana.
    /// </summary>
    public Histogram<long> RequestDuration { get; }

    /// <summary>Hur många kunder en sökning/listning returnerade.</summary>
    public Histogram<int> ResultCount { get; }

    /// <summary>Hur många kund-anrop som pågår just nu (upp vid start, ner vid slut).</summary>
    public UpDownCounter<long> InProgress { get; }

    public CustomerTelemetry(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(Name);

        Created = meter.CreateCounter<long>("customers.created", unit: "{customer}", description: "Antal skapade kunder");
        Updated = meter.CreateCounter<long>("customers.updated", unit: "{customer}", description: "Antal uppdaterade kunder");
        Deleted = meter.CreateCounter<long>("customers.deleted", unit: "{customer}", description: "Antal raderade kunder");
        ValidationFailed = meter.CreateCounter<long>("customers.validation_failed", unit: "{request}", description: "Anrop som stoppades av valideringen");

        // Histogram ger bucket-, sum- och count-serier i Prometheus, så att man
        // kan räkna ut percentiler (p50/p95/p99) istället för bara ett medelvärde.
        // Egna bucket-gränser i millisekunder passar våra svarstider bättre än
        // standardgränserna.
        RequestDuration = meter.CreateHistogram<long>("customers.requestduration", unit: "ms",
            description: "Tid per kund-operation i millisekunder",
            advice: new InstrumentAdvice<long> { HistogramBucketBoundaries = [5, 10, 25, 50, 100, 250, 500, 1000, 2500, 5000, 10000] });

        ResultCount = meter.CreateHistogram<int>("customers.result_count", unit: "{customer}",
            description: "Antal kunder i svaret på en listning/sökning",
            advice: new InstrumentAdvice<int> { HistogramBucketBoundaries = [0, 1, 5, 10, 25, 50, 100, 250] });

        InProgress = meter.CreateUpDownCounter<long>("customers.requests.in_progress", unit: "{request}",
            description: "Kund-anrop som pågår just nu");

        // Gauge: värdet hämtas när metrics exporteras - inget Add/Record behövs.
        meter.CreateObservableGauge("customers.count", () => Interlocked.Read(ref _customerCount),
            unit: "{customer}", description: "Antal kunder i databasen");
    }

    public void SetCustomerCount(long count) => Interlocked.Exchange(ref _customerCount, count);

    /// <summary>
    /// Mäter en operation: in_progress upp/ner och tiden i RequestDuration.
    /// Används med using: <c>using var op = telemetry.Measure("create");</c>
    /// och sätt <c>op.Outcome</c> om det inte gick bra.
    /// </summary>
    public Operation Measure(string operation) => new(this, operation);

    public sealed class Operation : IDisposable
    {
        private readonly CustomerTelemetry _telemetry;
        private readonly string _operation;
        private readonly long _start = Stopwatch.GetTimestamp();

        public string Outcome { get; set; } = "ok";

        internal Operation(CustomerTelemetry telemetry, string operation)
        {
            _telemetry = telemetry;
            _operation = operation;
            _telemetry.InProgress.Add(1, new KeyValuePair<string, object?>("operation", operation));
        }

        public void Dispose()
        {
            var operationTag = new KeyValuePair<string, object?>("operation", _operation);
            _telemetry.InProgress.Add(-1, operationTag);
            _telemetry.RequestDuration.Record(
                (long)Stopwatch.GetElapsedTime(_start).TotalMilliseconds,
                operationTag,
                new KeyValuePair<string, object?>("outcome", Outcome));
        }
    }
}
