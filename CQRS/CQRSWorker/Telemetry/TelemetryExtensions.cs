using NATS.Client.Core;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace CQRSWorker.Telemetry;

// ============================================================================
// OpenTelemetry för CQRSWorker. Workern har ingen HTTP-yta, så här finns ingen
// AspNetCore-instrumentering - istället:
//
//   "NATS.Net"     - spans för mottagna meddelanden. Trace-context läses från
//                    headern traceparent som API:t skickade med kommandot.
//   EF Core        - ett span per SQL-sats mot skriv-databasen.
//   WorkerTelemetry- egna spans och metrics för varje utfört kommando.
// ============================================================================
public static class TelemetryExtensions
{
    public static HostApplicationBuilder AddTelemetry(this HostApplicationBuilder builder)
    {
        var config = builder.Configuration;
        var serviceName = config["OTEL_SERVICE_NAME"] ?? "cqrs-worker";
        var serviceVersion = typeof(TelemetryExtensions).Assembly.GetName().Version?.ToString() ?? "0.0.0";

        // Workern frågar JetStream efter nya meddelanden hela tiden ($JS.API.CONSUMER.MSG.NEXT).
        // Varje sådan förfrågan blev annars en egen liten trace i Tempo - brus som
        // tränger undan de intressanta. Själva kommandona spåras som vanligt.
        NatsInstrumentationOptions.Default.Filter = ctx =>
            !ctx.Subject.StartsWith("$JS.API.CONSUMER.MSG.NEXT", StringComparison.Ordinal);

        var otel = builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(serviceName, serviceNamespace: "cqrs", serviceVersion: serviceVersion, autoGenerateServiceInstanceId: false)
                .AddAttributes(new Dictionary<string, object>
                {
                    ["deployment.environment.name"] = builder.Environment.EnvironmentName,
                }))

            .WithTracing(tracing => tracing
                .AddSource(WorkerTelemetry.Name)
                .AddSource("NATS.Net")
                .AddEntityFrameworkCoreInstrumentation())

            .WithMetrics(metrics => metrics
                .AddMeter(WorkerTelemetry.Name)
                .AddMeter("NATS.Net")
                .SetExemplarFilter(ExemplarFilterType.TraceBased)
                .AddRuntimeInstrumentation())

            .WithLogging(logging => { }, options =>
            {
                options.IncludeFormattedMessage = true;
                options.IncludeScopes = true;
            });

        if (!string.IsNullOrWhiteSpace(config["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            otel.UseOtlpExporter();
        }

        return builder;
    }
}
