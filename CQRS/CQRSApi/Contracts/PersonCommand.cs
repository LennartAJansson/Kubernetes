namespace CQRSApi.Contracts;

// ---------------------------------------------------------------------------
// Meddelandekontraktet mellan CQRSApi och CQRSWorker.
//
// Filen finns MEDVETET i båda projekten (samma innehåll) istället för i ett
// delat klassbibliotek. Kontraktet är JSON:en som skickas på NATS-subjektet -
// inte en C#-typ. Så länge båda sidor är överens om fälten kan de byggas,
// versioneras och deployas helt oberoende av varandra.
// ---------------------------------------------------------------------------

/// <summary>
/// Ett kommando som ändrar en person. VAD som ska göras avgörs av subjektet
/// meddelandet publiceras på (se <see cref="PersonSubjects"/>), inte av
/// innehållet.
/// </summary>
public sealed record PersonCommand
{
    /// <summary>Unikt id för just detta kommando. Används som Nats-Msg-Id
    /// så att JetStream kan slänga dubbletter (t.ex. vid omförsök).</summary>
    public Guid CommandId { get; init; } = Guid.NewGuid();

    /// <summary>Vilken person kommandot gäller. Vid Create skapar API:t id:t,
    /// så att klienten direkt vet vart den ska fråga efter resultatet.</summary>
    public Guid PersonId { get; init; }

    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? Email { get; init; }

    public DateTimeOffset IssuedAt { get; init; } = DateTimeOffset.UtcNow;
}

public static class PersonSubjects
{
    /// <summary>Alla person-kommandon. Strömmen fångar hela detta träd.</summary>
    public const string All = "persons.cmd.>";

    public const string Create = "persons.cmd.create";
    public const string Update = "persons.cmd.update";
    public const string Delete = "persons.cmd.delete";
}
