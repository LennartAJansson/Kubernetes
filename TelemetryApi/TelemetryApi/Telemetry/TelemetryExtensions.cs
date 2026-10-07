using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace TelemetryApi.Telemetry;

// ============================================================================
// All OpenTelemetry-konfiguration på ett ställe.
//
//   Traces  - ett span per inkommande anrop, per EF Core-fråga och per eget
//             span i CustomerTelemetry.ActivitySource
//   Metrics - HTTP-anrop (antal, tid), runtime (GC, trådar) och egna räknare
//   Logs    - vanlig ILogger, men skickad som OTLP med trace_id på varje rad
//
// Allt exporteras med OTLP till adressen i OTEL_EXPORTER_OTLP_ENDPOINT. I
// klustret sätts den av Helm till otel-collector.monitoring:4317. Är den inte
// satt alls exporteras ingenting - appen fungerar ändå.
// ============================================================================
public static class TelemetryExtensions
{
    public static WebApplicationBuilder AddTelemetry(this WebApplicationBuilder builder)
    {
        var config = builder.Configuration;
        var serviceName = config["OTEL_SERVICE_NAME"] ?? "telemetry-api";
        var serviceVersion = typeof(TelemetryExtensions).Assembly.GetName().Version?.ToString() ?? "0.0.0";

        var otel = builder.Services.AddOpenTelemetry()
            // Resource = "vem skickar?". Följer med varje span, metric och loggrad
            // och blir service_name i Grafana.
            .ConfigureResource(resource => resource
                // autoGenerateServiceInstanceId: false - poddnamnet kommer istället
                // från OTEL_RESOURCE_ATTRIBUTES (sätts i Helm-charten).
                .AddService(serviceName, serviceVersion: serviceVersion, autoGenerateServiceInstanceId: false)
                .AddAttributes(new Dictionary<string, object>
                {
                    ["deployment.environment.name"] = builder.Environment.EnvironmentName,
                }))

            .WithTracing(tracing => tracing
                .AddSource(CustomerTelemetry.Name)
                .AddAspNetCoreInstrumentation(o =>
                {
                    // Kubernetes probes anropar /healthz var tionde sekund - brus i Tempo.
                    o.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/healthz");
                    o.RecordException = true;
                })
                .AddHttpClientInstrumentation()
                .AddEntityFrameworkCoreInstrumentation())

            .WithMetrics(metrics => metrics
                .AddMeter(CustomerTelemetry.Name)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                // Exponerar /metrics i Prometheus-format, s\u00E5 man kan skrapa appen
                // direkt \u00E4ven utan otel-collectorn.
                .AddPrometheusExporter())

            .WithLogging(logging => { }, options =>
            {
                options.IncludeFormattedMessage = true;
                options.IncludeScopes = true;
            });

        // En rad för alla tre signalerna. Läser OTEL_EXPORTER_OTLP_ENDPOINT och
        // OTEL_EXPORTER_OTLP_PROTOCOL (grpc eller http/protobuf) från konfigurationen.
        if (!string.IsNullOrWhiteSpace(config["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            otel.UseOtlpExporter();
        }

        return builder;
    }
}
