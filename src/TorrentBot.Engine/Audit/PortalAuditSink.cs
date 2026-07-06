using System.Text.Json;
using Microsoft.Data.Sqlite;
using TorrentBot.Contracts.Audit;
using TorrentBot.Contracts.Context;

namespace TorrentBot.Engine.Audit;

public sealed class PortalAuditSink : IAuditSink, IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly object _gate = new();

    public PortalAuditSink(string connectionString = "Data Source=portal-audit.db")
    {
        _connection = new SqliteConnection(connectionString);
        _connection.Open();
        EnsureSchema();
    }

    public static PortalAuditSink CreateInMemory()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var sink = new PortalAuditSink(connection);
        return sink;
    }

    private PortalAuditSink(SqliteConnection connection)
    {
        _connection = connection;
        EnsureSchema();
    }

    public void Write(string action, IRequestContext context, string capabilityName, bool success, string? detail = null)
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                INSERT INTO audit_events (created_at, action, capability_name, user_id, trace_id, success, detail_json)
                VALUES ($created_at, $action, $capability_name, $user_id, $trace_id, $success, $detail_json)
                """;
            command.Parameters.AddWithValue("$created_at", DateTimeOffset.UtcNow.ToString("O"));
            command.Parameters.AddWithValue("$action", action);
            command.Parameters.AddWithValue("$capability_name", capabilityName);
            command.Parameters.AddWithValue("$user_id", context.UserId);
            command.Parameters.AddWithValue("$trace_id", context.TraceId);
            command.Parameters.AddWithValue("$success", success ? 1 : 0);
            command.Parameters.AddWithValue("$detail_json", detail is null ? DBNull.Value : detail);
            command.ExecuteNonQuery();
        }
    }

    public int CountEvents()
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM audit_events";
            return Convert.ToInt32(command.ExecuteScalar());
        }
    }

    public int CountByAction(string action)
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM audit_events WHERE action = $action";
            command.Parameters.AddWithValue("$action", action);
            return Convert.ToInt32(command.ExecuteScalar());
        }
    }

    public IReadOnlyList<AuditRecord> ListEvents()
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT created_at, action, capability_name, user_id, trace_id, success, detail_json FROM audit_events ORDER BY id";
            using var reader = command.ExecuteReader();
            var records = new List<AuditRecord>();
            while (reader.Read())
            {
                records.Add(new AuditRecord(
                    DateTimeOffset.Parse(reader.GetString(0)),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetInt32(5) == 1,
                    reader.IsDBNull(6) ? null : reader.GetString(6)));
            }

            return records;
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
            CREATE TABLE IF NOT EXISTS audit_events (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                created_at TEXT NOT NULL,
                action TEXT NOT NULL,
                capability_name TEXT NOT NULL,
                user_id TEXT NOT NULL,
                trace_id TEXT NOT NULL,
                success INTEGER NOT NULL,
                detail_json TEXT NULL
            )
            """;
        command.ExecuteNonQuery();
    }
}