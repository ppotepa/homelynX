using Microsoft.Data.Sqlite;

namespace TorrentBot.Plugins.Tools;

/// <summary>Small feature-specific persistence layer kept beside the existing tools database.</summary>
public sealed class FeatureStore
{
    private readonly string _connectionString;

    public FeatureStore(string? path)
    {
        path = string.IsNullOrWhiteSpace(path) ? Path.Combine("data", "homelynx-tools.db") : path;
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = path }.ToString();
        Initialize();
    }

    private void Initialize()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS user_locations(
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                user_id TEXT NOT NULL,
                name TEXT NOT NULL,
                latitude REAL NOT NULL,
                longitude REAL NOT NULL,
                label TEXT NOT NULL DEFAULT '',
                created TEXT NOT NULL,
                updated TEXT NOT NULL,
                UNIQUE(user_id, name));
            CREATE TABLE IF NOT EXISTS tracked_shipments(
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                user_id TEXT NOT NULL,
                tracking_number TEXT NOT NULL,
                carrier TEXT NOT NULL DEFAULT 'auto',
                provider TEXT NOT NULL DEFAULT 'manual',
                provider_id TEXT,
                label TEXT NOT NULL DEFAULT '',
                status TEXT NOT NULL DEFAULT 'registered',
                status_hash TEXT NOT NULL DEFAULT '',
                last_event TEXT NOT NULL DEFAULT '',
                last_checked TEXT,
                next_check TEXT,
                notify_mode TEXT NOT NULL DEFAULT 'important',
                active INTEGER NOT NULL DEFAULT 1,
                created TEXT NOT NULL,
                delivered TEXT,
                UNIQUE(user_id, tracking_number));
            CREATE TABLE IF NOT EXISTS tracking_events(
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                shipment_id INTEGER NOT NULL,
                status TEXT NOT NULL,
                description TEXT NOT NULL DEFAULT '',
                location TEXT NOT NULL DEFAULT '',
                event_at TEXT NOT NULL,
                received_at TEXT NOT NULL,
                event_hash TEXT NOT NULL,
                UNIQUE(shipment_id, event_hash));
            """;
        command.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private static void Add(SqliteCommand command, string name, object value) => command.Parameters.AddWithValue(name, value);

    public async Task SaveLocationAsync(string userId, string name, double latitude, double longitude, string label)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO user_locations(user_id,name,latitude,longitude,label,created,updated)
            VALUES($u,$n,$lat,$lon,$label,$now,$now)
            ON CONFLICT(user_id,name) DO UPDATE SET latitude=$lat,longitude=$lon,label=$label,updated=$now;
            """;
        Add(command, "$u", userId); Add(command, "$n", name); Add(command, "$lat", latitude); Add(command, "$lon", longitude);
        Add(command, "$label", label); Add(command, "$now", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync();
    }

    public async Task<LocationRecord?> GetLocationAsync(string userId, string name)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id,user_id,name,latitude,longitude,label,created,updated FROM user_locations WHERE user_id=$u AND name=$n";
        Add(command, "$u", userId); Add(command, "$n", name);
        using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync()
            ? new LocationRecord(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetDouble(3), reader.GetDouble(4), reader.GetString(5), reader.GetString(6), reader.GetString(7))
            : null;
    }

    public async Task<LocationRecord[]> ListLocationsAsync(string userId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id,user_id,name,latitude,longitude,label,created,updated FROM user_locations WHERE user_id=$u ORDER BY name";
        Add(command, "$u", userId);
        using var reader = await command.ExecuteReaderAsync();
        var result = new List<LocationRecord>();
        while (await reader.ReadAsync()) result.Add(new LocationRecord(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetDouble(3), reader.GetDouble(4), reader.GetString(5), reader.GetString(6), reader.GetString(7)));
        return result.ToArray();
    }

    public async Task DeleteLocationAsync(string userId, string name)
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM user_locations WHERE user_id=$u AND name=$n";
        Add(command, "$u", userId); Add(command, "$n", name); await command.ExecuteNonQueryAsync();
    }

    public async Task<long> AddShipmentAsync(string userId, string number, string carrier, string provider, string label, string notifyMode)
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO tracked_shipments(user_id,tracking_number,carrier,provider,label,notify_mode,created,next_check)
            VALUES($u,$n,$c,$p,$label,$notify,$now,$next)
            ON CONFLICT(user_id,tracking_number) DO UPDATE SET carrier=$c,label=$label,notify_mode=$notify,active=1;
            SELECT id FROM tracked_shipments WHERE user_id=$u AND tracking_number=$n;
            """;
        var now = DateTimeOffset.UtcNow; Add(command, "$u", userId); Add(command, "$n", number); Add(command, "$c", carrier); Add(command, "$p", provider);
        Add(command, "$label", label); Add(command, "$notify", notifyMode); Add(command, "$now", now.ToString("O")); Add(command, "$next", now.ToString("O"));
        return (long)(await command.ExecuteScalarAsync() ?? 0);
    }

    public async Task<ShipmentRecord?> GetShipmentAsync(string userId, long id)
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = ShipmentSelect("WHERE user_id=$u AND id=$id"); Add(command, "$u", userId); Add(command, "$id", id);
        using var reader = await command.ExecuteReaderAsync(); return await reader.ReadAsync() ? ReadShipment(reader) : null;
    }

    public async Task<ShipmentRecord?> GetShipmentByNumberAsync(string userId, string number)
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = ShipmentSelect("WHERE user_id=$u AND tracking_number=$n"); Add(command, "$u", userId); Add(command, "$n", number);
        using var reader = await command.ExecuteReaderAsync(); return await reader.ReadAsync() ? ReadShipment(reader) : null;
    }

    public async Task<ShipmentRecord[]> ListShipmentsAsync(string userId)
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = ShipmentSelect("WHERE user_id=$u ORDER BY active DESC,id DESC"); Add(command, "$u", userId);
        using var reader = await command.ExecuteReaderAsync(); var result = new List<ShipmentRecord>();
        while (await reader.ReadAsync()) result.Add(ReadShipment(reader)); return result.ToArray();
    }

    public async Task<ShipmentRecord[]> DueShipmentsAsync(DateTimeOffset now)
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = ShipmentSelect("WHERE active=1 AND (next_check IS NULL OR next_check<=$now) ORDER BY id"); Add(command, "$now", now.ToString("O"));
        using var reader = await command.ExecuteReaderAsync(); var result = new List<ShipmentRecord>();
        while (await reader.ReadAsync()) result.Add(ReadShipment(reader)); return result.ToArray();
    }

    public async Task UpdateShipmentAsync(long id, string status, string hash, string eventText, bool delivered, bool notify, DateTimeOffset nextCheck)
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "UPDATE tracked_shipments SET status=$s,status_hash=$h,last_event=$e,last_checked=$now,next_check=$next,active=$active,delivered=$delivered WHERE id=$id";
        Add(command, "$s", status); Add(command, "$h", hash); Add(command, "$e", eventText); Add(command, "$now", DateTimeOffset.UtcNow.ToString("O")); Add(command, "$next", nextCheck.ToString("O"));
        Add(command, "$active", delivered ? 0 : 1); Add(command, "$delivered", delivered ? DateTimeOffset.UtcNow.ToString("O") : (object)DBNull.Value); Add(command, "$id", id); await command.ExecuteNonQueryAsync();
    }

    public async Task SetShipmentActiveAsync(string userId, long id, bool active)
    {
        using var connection = Open(); using var command = connection.CreateCommand(); command.CommandText = "UPDATE tracked_shipments SET active=$a WHERE user_id=$u AND id=$id";
        Add(command, "$a", active ? 1 : 0); Add(command, "$u", userId); Add(command, "$id", id); await command.ExecuteNonQueryAsync();
    }

    public async Task DeleteShipmentAsync(string userId, long id)
    {
        using var connection = Open(); using var command = connection.CreateCommand(); command.CommandText = "DELETE FROM tracked_shipments WHERE user_id=$u AND id=$id";
        Add(command, "$u", userId); Add(command, "$id", id); await command.ExecuteNonQueryAsync();
    }

    private static string ShipmentSelect(string where) => $"SELECT id,user_id,tracking_number,carrier,provider,label,status,status_hash,last_event,last_checked,next_check,notify_mode,active,created,delivered FROM tracked_shipments {where}";
    private static ShipmentRecord ReadShipment(SqliteDataReader reader) => new(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7), reader.GetString(8), reader.IsDBNull(9) ? null : DateTimeOffset.Parse(reader.GetString(9)), reader.IsDBNull(10) ? null : DateTimeOffset.Parse(reader.GetString(10)), reader.GetString(11), reader.GetInt32(12) != 0, DateTimeOffset.Parse(reader.GetString(13)), reader.IsDBNull(14) ? null : DateTimeOffset.Parse(reader.GetString(14)));
}

public sealed record LocationRecord(long Id, string UserId, string Name, double Latitude, double Longitude, string Label, string Created, string Updated);
public sealed record ShipmentRecord(long Id, string UserId, string Number, string Carrier, string Provider, string Label, string Status, string StatusHash, string LastEvent, DateTimeOffset? LastChecked, DateTimeOffset? NextCheck, string NotifyMode, bool Active, DateTimeOffset Created, DateTimeOffset? Delivered);
