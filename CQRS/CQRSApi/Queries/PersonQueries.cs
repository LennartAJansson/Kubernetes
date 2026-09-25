using Dapper;
using MySqlConnector;

namespace CQRSApi.Queries;

/// <summary>
/// Läs-sidan (Query) i API:t. Ren SQL via Dapper direkt mot tabellen som
/// workern äger. Inget EF Core, ingen ändringsspårning - bara snabba läsningar.
/// Anslutningen bör använda en databasanvändare som BARA har SELECT-rättighet.
/// </summary>
public sealed class PersonQueries(string connectionString)
{
    private const string Columns = "Id, FirstName, LastName, Email, CreatedAt, UpdatedAt";

    public async Task<IReadOnlyList<PersonDto>> GetAllAsync(CancellationToken ct)
    {
        await using var connection = new MySqlConnection(connectionString);
        var rows = await connection.QueryAsync<PersonDto>(new CommandDefinition(
            $"SELECT {Columns} FROM Persons ORDER BY LastName, FirstName",
            cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<PersonDto?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        await using var connection = new MySqlConnection(connectionString);
        return await connection.QuerySingleOrDefaultAsync<PersonDto>(new CommandDefinition(
            $"SELECT {Columns} FROM Persons WHERE Id = @Id",
            new { Id = id.ToString() },
            cancellationToken: ct));
    }
}

/// <summary>
/// Läsmodellen. Id är en sträng eftersom workern lagrar Guid:en som
/// varchar(36) - Dapper mappar kolumnerna rakt av på namn.
/// </summary>
public sealed class PersonDto
{
    public string Id { get; init; } = "";
    public string FirstName { get; init; } = "";
    public string LastName { get; init; } = "";
    public string? Email { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}
