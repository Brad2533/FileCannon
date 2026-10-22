using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace ClipShare.PiServer.Services;

/// <summary>
/// Hand-written SQL over the same <c>metadata.db</c> file <see cref="ClipRepository"/> uses —
/// same rationale (tiny query surface, crash-safe atomic writes, no EF Core). Every
/// authenticated request does one <see cref="GetByApiKeyHash"/> against the UNIQUE-indexed
/// hash column, so the hot path is a single indexed point lookup.
/// </summary>
public sealed class AccountRepository
{
    private readonly string _connectionString;

    public AccountRepository(IOptions<PiServerOptions> options)
    {
        var dbPath = Path.Combine(options.Value.DataRoot, "metadata.db");
        _connectionString = $"Data Source={dbPath}";
    }

    public void Initialize()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        // UNIQUE on ApiKeyHash both enforces one-account-per-key and gives the index the
        // per-request lookup rides on — no separate CREATE INDEX needed.
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS Accounts (
              Id TEXT PRIMARY KEY,
              ApiKeyHash TEXT NOT NULL UNIQUE,
              Tier TEXT NOT NULL,
              IsAdmin INTEGER NOT NULL DEFAULT 0,
              StripeCustomerId TEXT NULL,
              Label TEXT NULL,
              CreatedAtUtc TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    public void Insert(Account account)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Accounts (Id, ApiKeyHash, Tier, IsAdmin, StripeCustomerId, Label, CreatedAtUtc)
            VALUES ($id, $apiKeyHash, $tier, $isAdmin, $stripeCustomerId, $label, $createdAtUtc);
            """;
        command.Parameters.AddWithValue("$id", account.Id.ToString());
        command.Parameters.AddWithValue("$apiKeyHash", account.ApiKeyHash);
        command.Parameters.AddWithValue("$tier", account.Tier);
        command.Parameters.AddWithValue("$isAdmin", account.IsAdmin ? 1 : 0);
        command.Parameters.AddWithValue("$stripeCustomerId", (object?)account.StripeCustomerId ?? DBNull.Value);
        command.Parameters.AddWithValue("$label", (object?)account.Label ?? DBNull.Value);
        command.Parameters.AddWithValue("$createdAtUtc", account.CreatedAtUtc.ToString("O"));
        command.ExecuteNonQuery();
    }

    public Account? GetByApiKeyHash(string apiKeyHash)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = SelectColumns + " WHERE ApiKeyHash = $apiKeyHash;";
        command.Parameters.AddWithValue("$apiKeyHash", apiKeyHash);

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadAccount(reader) : null;
    }

    public Account? GetById(Guid id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = SelectColumns + " WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString());

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadAccount(reader) : null;
    }

    public List<Account> GetAll()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = SelectColumns + " ORDER BY CreatedAtUtc;";

        using var reader = command.ExecuteReader();
        var results = new List<Account>();
        while (reader.Read())
        {
            results.Add(ReadAccount(reader));
        }
        return results;
    }

    public int Count()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Accounts;";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    /// <returns><c>true</c> if a row was deleted, <c>false</c> if no account had that id.</returns>
    public bool Delete(Guid id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Accounts WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString());
        return command.ExecuteNonQuery() > 0;
    }

    private const string SelectColumns =
        "SELECT Id, ApiKeyHash, Tier, IsAdmin, StripeCustomerId, Label, CreatedAtUtc FROM Accounts";

    private static Account ReadAccount(SqliteDataReader reader) => new(
        Id: Guid.Parse(reader.GetString(0)),
        ApiKeyHash: reader.GetString(1),
        Tier: reader.GetString(2),
        IsAdmin: reader.GetInt64(3) != 0,
        StripeCustomerId: reader.IsDBNull(4) ? null : reader.GetString(4),
        Label: reader.IsDBNull(5) ? null : reader.GetString(5),
        CreatedAtUtc: DateTime.Parse(reader.GetString(6)).ToUniversalTime());

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }
}
