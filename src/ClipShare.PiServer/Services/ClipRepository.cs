using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace ClipShare.PiServer.Services;

/// <summary>
/// Hand-written SQL over Microsoft.Data.Sqlite rather than EF Core — the query surface is
/// small (insert, get-by-id, get-expired, delete) and SQLite gives crash-safe atomic writes
/// without hand-rolling file locking between the upload endpoints and the cleanup timer.
/// </summary>
public sealed class ClipRepository
{
    private readonly string _connectionString;

    public ClipRepository(IOptions<PiServerOptions> options)
    {
        var dbPath = Path.Combine(options.Value.DataRoot, "metadata.db");
        _connectionString = $"Data Source={dbPath}";
    }

    public void Initialize()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS Clips (
              Id TEXT PRIMARY KEY, OriginalFileName TEXT NOT NULL, StoredFileName TEXT NOT NULL,
              SizeBytes INTEGER NOT NULL, UploadedAtUtc TEXT NOT NULL, ExpiresAtUtc TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    public void Insert(Clip clip)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Clips (Id, OriginalFileName, StoredFileName, SizeBytes, UploadedAtUtc, ExpiresAtUtc)
            VALUES ($id, $originalFileName, $storedFileName, $sizeBytes, $uploadedAtUtc, $expiresAtUtc);
            """;
        command.Parameters.AddWithValue("$id", clip.Id.ToString());
        command.Parameters.AddWithValue("$originalFileName", clip.OriginalFileName);
        command.Parameters.AddWithValue("$storedFileName", clip.StoredFileName);
        command.Parameters.AddWithValue("$sizeBytes", clip.SizeBytes);
        command.Parameters.AddWithValue("$uploadedAtUtc", clip.UploadedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$expiresAtUtc", clip.ExpiresAtUtc.ToString("O"));
        command.ExecuteNonQuery();
    }

    public Clip? GetById(Guid id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, OriginalFileName, StoredFileName, SizeBytes, UploadedAtUtc, ExpiresAtUtc FROM Clips WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString());

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadClip(reader) : null;
    }

    public List<Clip> GetExpired(DateTime nowUtc)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, OriginalFileName, StoredFileName, SizeBytes, UploadedAtUtc, ExpiresAtUtc FROM Clips WHERE ExpiresAtUtc <= $now;";
        command.Parameters.AddWithValue("$now", nowUtc.ToString("O"));

        using var reader = command.ExecuteReader();
        var results = new List<Clip>();
        while (reader.Read())
        {
            results.Add(ReadClip(reader));
        }
        return results;
    }

    public void Delete(Guid id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Clips WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString());
        command.ExecuteNonQuery();
    }

    private static Clip ReadClip(SqliteDataReader reader) => new(
        Id: Guid.Parse(reader.GetString(0)),
        OriginalFileName: reader.GetString(1),
        StoredFileName: reader.GetString(2),
        SizeBytes: reader.GetInt64(3),
        UploadedAtUtc: DateTime.Parse(reader.GetString(4)).ToUniversalTime(),
        ExpiresAtUtc: DateTime.Parse(reader.GetString(5)).ToUniversalTime());

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }
}
