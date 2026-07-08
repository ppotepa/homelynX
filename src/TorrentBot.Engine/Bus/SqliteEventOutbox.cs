using Microsoft.Data.Sqlite;
using TorrentBot.Contracts.Bus;
using TorrentBot.Contracts.Context;

namespace TorrentBot.Engine.Bus;

public sealed class SqliteEventOutbox : IEventOutbox, IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly object _gate = new();

    public SqliteEventOutbox(string connectionString)
    {
        _connection = new SqliteConnection(connectionString);
        _connection.Open();
        EnsureSchema();
    }

    public static SqliteEventOutbox CreateInMemory()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        return new SqliteEventOutbox(connection);
    }

    private SqliteEventOutbox(SqliteConnection connection)
    {
        _connection = connection;
        EnsureSchema();
    }

    public void Append(string eventType, IRequestContext context, string payloadJson)
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                INSERT INTO event_outbox (created_at, event_type, trace_id, user_id, payload_json)
                VALUES ($created_at, $event_type, $trace_id, $user_id, $payload_json)
                """;
            command.Parameters.AddWithValue("$created_at", DateTimeOffset.UtcNow.ToString("O"));
            command.Parameters.AddWithValue("$event_type", eventType);
            command.Parameters.AddWithValue("$trace_id", context.TraceId);
            command.Parameters.AddWithValue("$user_id", context.UserId);
            command.Parameters.AddWithValue("$payload_json", payloadJson);
            command.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<OutboxEntry> ReadRecent(int limit = 100)
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                SELECT created_at, event_type, trace_id, user_id, payload_json
                FROM event_outbox
                ORDER BY id DESC
                LIMIT $limit
                """;
            command.Parameters.AddWithValue("$limit", limit);

            var entries = new List<OutboxEntry>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                entries.Add(new OutboxEntry(
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    DateTimeOffset.Parse(reader.GetString(0))));
            }

            return entries;
        }
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    private void EnsureSchema()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS event_outbox (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                created_at TEXT NOT NULL,
                event_type TEXT NOT NULL,
                trace_id TEXT NOT NULL,
                user_id TEXT NOT NULL,
                payload_json TEXT NOT NULL
            )
            """;
        command.ExecuteNonQuery();
    }
}