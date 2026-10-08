using Microsoft.EntityFrameworkCore;
using OpenTelemetry;
using TelemetryApi.Data;

namespace TelemetryApi.Telemetry;

/// <summary>
/// Läser antalet kunder i databasen var 15:e sekund och lämnar det till
/// gauge-mätaren customers.count. Ett affärsmått - "hur många kunder har vi?" -
/// som inte går att räkna fram ur HTTP-anropen.
/// </summary>
public sealed class CustomerCountRefresher(
    IServiceScopeFactory scopeFactory,
    CustomerTelemetry telemetry,
    ILogger<CustomerCountRefresher> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                // Bakgrundsjobbet ska inte skapa en ny trace i Tempo var 15:e
                // sekund - SuppressInstrumentationScope stänger av spans här.
                using (SuppressInstrumentationScope.Begin())
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var db = scope.ServiceProvider.GetRequiredService<CustomersDbContext>();
                    telemetry.SetCustomerCount(await db.Customers.LongCountAsync(stoppingToken));
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning("Kunde inte räkna kunderna: {Error}", ex.Message);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
