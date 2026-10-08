using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace CQRSApi.Telemetry;

// ============================================================================
// OpenTelemetry för CQRSApi - samma upplägg som TelemetryApi, plus två källor
// som är specifika för CQRS:
//
//   "NATS.Net"       - NATS-klienten har inbyggda spans och metrics. Den lägger
//                      dessutom headern traceparent på varje meddelande, så att
//                      workern kan fortsätta SAMMA trace på andra sidan kön.
//   "MySqlConnector" - Dapper använder MySqlConnector, som skapar ett span per
//                      SQL-fråga på läs-sidan.
//
// Adressen till collectorn kommer från OTEL_EXPORTER_OTLP_ENDPOINT (Helm).
// ============================================================================
public static class TelemetryExtensions
{
    public static WebApplicationBuilder AddTelemetry(this WebApplicationBuilder builder)
    {
        var config = builder.Configuration;
        var serviceName = config["OTEL_SERVICE_NAME"] ?? "cqrs-api";
        var serviceVersion = typeof(TelemetryExtensions).Assembly.GetName().Version?.ToString() ?? "0.0.0";

        var otel = builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(serviceName, serviceNamespace: "cqrs", serviceVersion: serviceVersion, autoGenerateServiceInstanceId: false)
                .AddAttributes(new Dictionary<string, object>
                {
                    ["deployment.environment.name"] = builder.Environment.EnvironmentName,
                }))

            .WithTracing(tracing => tracing
                .AddSource(CommandTelemetry.Name)
                .AddSource("NATS.Net")
                .AddSource("MySqlConnector")
                .AddAspNetCoreInstrumentation(o =>
                {
                    o.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/healthz");
                    o.RecordException = true;
                }))

            .WithMetrics(metrics => metrics
                .AddMeter(CommandTelemetry.Name)
                .AddMeter("NATS.Net")
                .AddMeter("MySqlConnector")
                .SetExemplarFilter(ExemplarFilterType.TraceBased)
                .AddAspNetCoreInstrumentation()
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
