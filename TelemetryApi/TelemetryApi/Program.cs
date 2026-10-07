using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Metrics;
using Scalar.AspNetCore;
using TelemetryApi.Customers;
using TelemetryApi.Data;
using TelemetryApi.Demo;
using TelemetryApi.Telemetry;

// ============================================================================
// TelemetryApi - ett enkelt CRUD-API för Customer, byggt för att visa
// OpenTelemetry: traces till Tempo, metrics till Prometheus och loggar till
// Loki - allt via otel-collectorn i namespace monitoring.
//
//   CustomerClient (nginx) -> TelemetryApi -> EF Core -> MySQL
//                                  |
//                                  +-> OTLP -> otel-collector -> Tempo / Prometheus / Loki
// ============================================================================

var builder = WebApplication.CreateBuilder(args);

// --- Telemetri: en rad här, resten i Telemetry/TelemetryExtensions.cs --------
builder.AddTelemetry();
builder.Services.AddSingleton<CustomerTelemetry>();

// --- Databas -----------------------------------------------------------------
var connectionString = builder.Configuration.GetConnectionString("CustomersDb")
    ?? throw new InvalidOperationException("ConnectionStrings:CustomersDb saknas (miljövariabel ConnectionStrings__CustomersDb).");
builder.Services.AddDbContext<CustomersDbContext>(o => o.UseMySQL(connectionString));

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

var app = builder.Build();

// Skapar tabellen Customers om den saknas. (Migreringar tar vi en annan kväll.)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CustomersDbContext>();
    await db.Database.EnsureCreatedAsync();
}

// Ohanterade undantag blir ett 500-svar med ProblemDetails - och en
// felloggning med stacktrace som hamnar i Loki, kopplad till spannet i Tempo.
app.UseExceptionHandler();

if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("OpenApi:Enabled"))
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapGet("/healthz", () => TypedResults.Ok("ok")).ExcludeFromDescription();

// Prometheus kan skrapa /metrics direkt p\u00E5 appen (utan collectorn) f\u00F6r att
// testa att metrics faktiskt skapas.
app.MapPrometheusScrapingEndpoint().ExcludeFromDescription();

app.MapCustomers();
app.MapDemo();

app.Run();
