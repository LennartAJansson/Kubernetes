using System.Diagnostics;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using TelemetryApi.Data;
using TelemetryApi.Telemetry;

namespace TelemetryApi.Customers;

// ============================================================================
// Vanlig CRUD för Customer - ingen CQRS. Det som är värt att titta på är
// telemetrin: strukturerade loggar, egna metrics och ett eget span.
// ============================================================================
public static class CustomerEndpoints
{
    public static IEndpointRouteBuilder MapCustomers(this IEndpointRouteBuilder app)
    {
        var customers = app.MapGroup("/customers").WithTags("Customers");

        customers.MapGet("/", GetAll).WithName("GetCustomers");
        customers.MapGet("/{id:guid}", GetById).WithName("GetCustomer");
        customers.MapPost("/", Create).WithName("CreateCustomer");
        customers.MapPut("/{id:guid}", Update).WithName("UpdateCustomer");
        customers.MapDelete("/{id:guid}", Delete).WithName("DeleteCustomer");

        return app;
    }

    private static async Task<Ok<List<CustomerDto>>> GetAll(string? search, CustomersDbContext db, CancellationToken ct)
    {
        var query = db.Customers.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(c => c.Name.Contains(term) || (c.City != null && c.City.Contains(term)));
        }

        var result = await query.OrderBy(c => c.Name).Select(c => CustomerDto.From(c)).ToListAsync(ct);

        // Taggar på det span ASP.NET Core redan har startat för anropet.
        Activity.Current?.SetTag("customers.search", search ?? "");
        Activity.Current?.SetTag("customers.count", result.Count);
        return TypedResults.Ok(result);
    }

    private static async Task<Results<Ok<CustomerDto>, NotFound>> GetById(Guid id, CustomersDbContext db, CancellationToken ct)
    {
        Activity.Current?.SetTag("customer.id", id.ToString());
        var customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        return customer is null ? TypedResults.NotFound() : TypedResults.Ok(CustomerDto.From(customer));
    }

    private static async Task<Results<Created<CustomerDto>, ValidationProblem>> Create(
        CustomerRequest request, CustomersDbContext db, CustomerTelemetry telemetry, ILoggerFactory loggerFactory, CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("TelemetryApi.Customers");

        if (Validate(request, telemetry, logger) is { } errors)
            return TypedResults.ValidationProblem(errors);

        var customer = new Customer { Id = Guid.NewGuid(), CreatedAt = DateTime.UtcNow };
        request.ApplyTo(customer);
        db.Customers.Add(customer);
        await db.SaveChangesAsync(ct);

        telemetry.Created.Add(1);
        Activity.Current?.SetTag("customer.id", customer.Id.ToString());
        // Strukturerad loggning: {CustomerId} blir ett eget fält i Loki, inte bara text.
        logger.LogInformation("Kund {CustomerId} skapad: {CustomerName} ({City})", customer.Id, customer.Name, customer.City);

        return TypedResults.Created($"/customers/{customer.Id}", CustomerDto.From(customer));
    }

    private static async Task<Results<Ok<CustomerDto>, NotFound, ValidationProblem>> Update(
        Guid id, CustomerRequest request, CustomersDbContext db, CustomerTelemetry telemetry, ILoggerFactory loggerFactory, CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("TelemetryApi.Customers");
        Activity.Current?.SetTag("customer.id", id.ToString());

        if (Validate(request, telemetry, logger) is { } errors)
            return TypedResults.ValidationProblem(errors);

        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (customer is null)
        {
            logger.LogWarning("Kund {CustomerId} finns inte - kan inte uppdatera", id);
            return TypedResults.NotFound();
        }

        request.ApplyTo(customer);
        customer.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        telemetry.Updated.Add(1);
        logger.LogInformation("Kund {CustomerId} uppdaterad", id);
        return TypedResults.Ok(CustomerDto.From(customer));
    }

    private static async Task<Results<NoContent, NotFound>> Delete(
        Guid id, CustomersDbContext db, CustomerTelemetry telemetry, ILoggerFactory loggerFactory, CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("TelemetryApi.Customers");
        Activity.Current?.SetTag("customer.id", id.ToString());

        var deleted = await db.Customers.Where(c => c.Id == id).ExecuteDeleteAsync(ct);
        if (deleted == 0)
        {
            logger.LogWarning("Kund {CustomerId} finns inte - kan inte radera", id);
            return TypedResults.NotFound();
        }

        telemetry.Deleted.Add(1);
        logger.LogInformation("Kund {CustomerId} raderad", id);
        return TypedResults.NoContent();
    }

    /// <summary>
    /// Valideringen får ett EGET span, så att den syns som ett eget steg i Tempo.
    /// </summary>
    private static Dictionary<string, string[]>? Validate(CustomerRequest request, CustomerTelemetry telemetry, ILogger logger)
    {
        using var activity = CustomerTelemetry.ActivitySource.StartActivity("customers.validate");

        var errors = request.Validate();
        activity?.SetTag("validation.valid", errors is null);

        if (errors is not null)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Valideringen misslyckades");
            telemetry.ValidationFailed.Add(1);
            logger.LogWarning("Validering misslyckades för fälten {Fields}", string.Join(", ", errors.Keys));
        }

        return errors;
    }
}

public sealed record CustomerRequest(string? Name, string? Email, string? Phone, string? City)
{
    public Dictionary<string, string[]>? Validate()
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(Name)) errors[nameof(Name)] = ["Namn krävs."];
        if (!string.IsNullOrWhiteSpace(Email) && !Email.Contains('@')) errors[nameof(Email)] = ["Ogiltig e-postadress."];
        return errors.Count == 0 ? null : errors;
    }

    public void ApplyTo(Customer customer)
    {
        customer.Name = Name!.Trim();
        customer.Email = Clean(Email);
        customer.Phone = Clean(Phone);
        customer.City = Clean(City);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record CustomerDto(Guid Id, string Name, string? Email, string? Phone, string? City, DateTime CreatedAt, DateTime? UpdatedAt)
{
    public static CustomerDto From(Customer c) => new(c.Id, c.Name, c.Email, c.Phone, c.City, c.CreatedAt, c.UpdatedAt);
}
