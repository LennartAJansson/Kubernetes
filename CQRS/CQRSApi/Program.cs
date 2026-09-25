using CQRSApi.Contracts;
using CQRSApi.Messaging;
using CQRSApi.Queries;
using Microsoft.AspNetCore.Http.HttpResults;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Net;
using Scalar.AspNetCore;

// ============================================================================
// CQRSApi - den utåtriktade delen av CQRS-mönstret.
//
//   Query  (GET)               -> Dapper -> MySQL (läser workerns tabell)
//   Command (POST/PUT/DELETE)  -> NATS JetStream -> CQRSWorker -> EF Core -> MySQL
//
// API:t skriver aldrig till databasen. Kommandon besvaras med 202 Accepted:
// "jag har tagit emot din begäran" - inte "det är klart". Ändringen syns i
// GET först när workern har hunnit utföra den (eventual consistency).
// ============================================================================

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

// --- NATS JetStream (skriv-sidan) -------------------------------------------
var natsSettings = builder.Configuration.GetSection(NatsSettings.SectionName).Get<NatsSettings>() ?? new NatsSettings();
builder.Services.AddSingleton(natsSettings);
builder.Services.AddSingleton(_ => new NatsClient(new NatsOpts { Url = natsSettings.Url, Name = "cqrs-api" }));
builder.Services.AddSingleton<INatsJSContext>(sp => sp.GetRequiredService<NatsClient>().CreateJetStreamContext());
builder.Services.AddSingleton<PersonCommandPublisher>();
builder.Services.AddHostedService<PersonStreamSetup>();

// --- Dapper (läs-sidan) -----------------------------------------------------
var readDb = builder.Configuration.GetConnectionString("ReadDb")
    ?? throw new InvalidOperationException("ConnectionStrings:ReadDb saknas (miljövariabel ConnectionStrings__ReadDb).");
builder.Services.AddSingleton(new PersonQueries(readDb));

// --- CORS (styrs från Helm-values: cors.mode / cors.origins) ----------------
var cors = builder.Configuration.GetSection(CorsSettings.SectionName).Get<CorsSettings>() ?? new CorsSettings();
var corsEnabled = cors.Configure(builder.Services);

var app = builder.Build();

app.Logger.LogInformation("CORS-läge: {Mode} {Origins}", cors.Mode, string.Join(", ", cors.Origins));

if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("OpenApi:Enabled"))
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

// Ingen UseHttpsRedirection: i klustret terminerar Traefik (ingressen) TLS och
// podden pratar vanlig HTTP på port 8081.
if (corsEnabled)
{
    app.UseCors();
}

app.MapGet("/healthz", () => TypedResults.Ok("ok")).ExcludeFromDescription();

var persons = app.MapGroup("/persons").WithTags("Persons");

// ---------------------------------------------------------------- Queries ---
persons.MapGet("/", async (PersonQueries queries, CancellationToken ct) =>
        TypedResults.Ok(await queries.GetAllAsync(ct)))
    .WithName("GetPersons");

persons.MapGet("/{id:guid}", async Task<Results<Ok<PersonDto>, NotFound>> (Guid id, PersonQueries queries, CancellationToken ct) =>
        await queries.GetByIdAsync(id, ct) is { } person
            ? TypedResults.Ok(person)
            : TypedResults.NotFound())
    .WithName("GetPerson");

// --------------------------------------------------------------- Commands ---
persons.MapPost("/", async Task<Results<Accepted<CommandAccepted>, ValidationProblem>> (
        PersonRequest request, PersonCommandPublisher publisher, CancellationToken ct) =>
    {
        if (request.Validate() is { } errors)
            return TypedResults.ValidationProblem(errors);

        // API:t bestämmer id:t, så att klienten direkt kan fråga efter resultatet.
        var command = await publisher.PublishAsync(PersonSubjects.Create, request.ToCommand(Guid.NewGuid()), ct);
        return TypedResults.Accepted($"/persons/{command.PersonId}", CommandAccepted.From(command, "create"));
    })
    .WithName("CreatePerson");

persons.MapPut("/{id:guid}", async Task<Results<Accepted<CommandAccepted>, ValidationProblem>> (
        Guid id, PersonRequest request, PersonCommandPublisher publisher, CancellationToken ct) =>
    {
        if (request.Validate() is { } errors)
            return TypedResults.ValidationProblem(errors);

        var command = await publisher.PublishAsync(PersonSubjects.Update, request.ToCommand(id), ct);
        return TypedResults.Accepted($"/persons/{id}", CommandAccepted.From(command, "update"));
    })
    .WithName("UpdatePerson");

persons.MapDelete("/{id:guid}", async (Guid id, PersonCommandPublisher publisher, CancellationToken ct) =>
    {
        var command = await publisher.PublishAsync(PersonSubjects.Delete, new PersonCommand { PersonId = id }, ct);
        return TypedResults.Accepted($"/persons/{id}", CommandAccepted.From(command, "delete"));
    })
    .WithName("DeletePerson");

app.Run();

// ============================================================================
// Request/response-typer
// ============================================================================

public sealed record PersonRequest(string? FirstName, string? LastName, string? Email)
{
    public Dictionary<string, string[]>? Validate()
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(FirstName)) errors[nameof(FirstName)] = ["Förnamn krävs."];
        if (string.IsNullOrWhiteSpace(LastName)) errors[nameof(LastName)] = ["Efternamn krävs."];
        return errors.Count == 0 ? null : errors;
    }

    public PersonCommand ToCommand(Guid personId) => new()
    {
        PersonId = personId,
        FirstName = FirstName?.Trim(),
        LastName = LastName?.Trim(),
        Email = string.IsNullOrWhiteSpace(Email) ? null : Email.Trim(),
    };
}

/// <summary>Svaret på ett kommando: "mottaget", inte "utfört".</summary>
public sealed record CommandAccepted(Guid PersonId, Guid CommandId, string Action, string Status)
{
    public static CommandAccepted From(PersonCommand command, string action) =>
        new(command.PersonId, command.CommandId, action, "accepted");
}

/// <summary>
/// Sektionen "Cors" (Cors__Mode, Cors__Origins__0, Cors__Origins__1 ...).
///   AllowAll     - alla origins, headers och metoder (bra i utveckling)
///   AllowOrigins - bara de origins som listas i Origins
///   Disabled     - ingen CORS alls (API:t anropas bara server-till-server)
/// </summary>
public sealed class CorsSettings
{
    public const string SectionName = "Cors";

    public string Mode { get; set; } = "Disabled";
    public string[] Origins { get; set; } = [];

    /// <returns>true om CORS-middleware ska aktiveras.</returns>
    public bool Configure(IServiceCollection services)
    {
        if (Mode.Equals("AllowAll", StringComparison.OrdinalIgnoreCase))
        {
            services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));
            return true;
        }

        if (Mode.Equals("AllowOrigins", StringComparison.OrdinalIgnoreCase))
        {
            if (Origins.Length == 0)
                throw new InvalidOperationException("Cors:Mode är AllowOrigins men Cors:Origins är tom.");

            services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(Origins).AllowAnyHeader().AllowAnyMethod()));
            return true;
        }

        if (Mode.Equals("Disabled", StringComparison.OrdinalIgnoreCase))
            return false;

        throw new InvalidOperationException($"Okänt Cors:Mode '{Mode}'. Giltiga värden: AllowAll, AllowOrigins, Disabled.");
    }
}
