using System.Diagnostics;
using TelemetryApi.Telemetry;

namespace TelemetryApi.Demo;

// ============================================================================
// Endpoints som bara finns för att ha något att titta på i Grafana:
// ett långsamt anrop (syns i latens-grafer och som långt span i Tempo) och
// ett anrop som kastar ett undantag (rött span, exception i loggen).
// ============================================================================
public static class DemoEndpoints
{
    public static IEndpointRouteBuilder MapDemo(this IEndpointRouteBuilder app)
    {
        var demo = app.MapGroup("/demo").WithTags("Demo");

        demo.MapGet("/slow", async (int? ms, ILoggerFactory loggerFactory, CancellationToken ct, CustomerTelemetry telemetry) =>
        {
          Stopwatch stopwatch = Stopwatch.StartNew();
          var delay = Math.Clamp(ms ?? 1500, 0, 10_000);
            var logger = loggerFactory.CreateLogger("TelemetryApi.Demo");

            using (var activity = CustomerTelemetry.ActivitySource.StartActivity("demo.slow-work"))
            {
                activity?.SetTag("demo.delay_ms", delay);
                logger.LogInformation("Simulerar långsamt arbete i {Delay} ms", delay);
                await Task.Delay(delay, ct);
            }
            
            telemetry.RequestDuration.Record(stopwatch.ElapsedMilliseconds);

            return TypedResults.Ok(new { delayMs = delay });
        });

        demo.MapGet("/error", (ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("TelemetryApi.Demo");
            logger.LogWarning("Strax kastas ett undantag - titta efter det röda spannet i Tempo (trace {TraceId})",
                Activity.Current?.TraceId.ToString());
            throw new InvalidOperationException("Avsiktligt fel från /demo/error");
        });

        return app;
    }
}
