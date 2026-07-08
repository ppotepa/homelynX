using System.Text.Json;

namespace TorrentBot.Engine.Confirmations;

public sealed class FileBasedConfirmationStore : IConfirmationStore
{
    private readonly string _filePath;
    private readonly object _lock = new();

    public FileBasedConfirmationStore(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(
            Path.GetTempPath(),
            "homelynx-confirmations.json");
    }

    public string Issue(string capabilityName, string userId, TimeSpan? ttl = null)
    {
        lock (_lock)
        {
            var pending = LoadPending();
            var token = Guid.NewGuid().ToString("N")[..12];
            pending[token] = new PendingConfirmation(
                capabilityName,
                userId,
                DateTimeOffset.UtcNow.Add(ttl ?? TimeSpan.FromMinutes(10)));
            SavePending(pending);
            return token;
        }
    }

    public bool TryConsume(string token, string capabilityName, string userId)
    {
        lock (_lock)
        {
            var pending = LoadPending();
            if (!pending.TryGetValue(token, out var confirmation))
            {
                return false;
            }

            pending.Remove(token);
            SavePending(pending);

            if (confirmation.ExpiresAt < DateTimeOffset.UtcNow)
            {
                return false;
            }

            return string.Equals(confirmation.CapabilityName, capabilityName, StringComparison.Ordinal)
                   && string.Equals(confirmation.UserId, userId, StringComparison.Ordinal);
        }
    }

    private Dictionary<string, PendingConfirmation> LoadPending()
    {
        if (!File.Exists(_filePath))
        {
            return new Dictionary<string, PendingConfirmation>(StringComparer.Ordinal);
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            var data = JsonSerializer.Deserialize<FileData>(json);
            var result = new Dictionary<string, PendingConfirmation>(StringComparer.Ordinal);
            if (data?.Pending != null)
            {
                foreach (var kvp in data.Pending)
                {
                    result[kvp.Key] = new PendingConfirmation(
                        kvp.Value.CapabilityName,
                        kvp.Value.UserId,
                        kvp.Value.ExpiresAt);
                }
            }
            return result;
        }
        catch
        {
            return new Dictionary<string, PendingConfirmation>(StringComparer.Ordinal);
        }
    }

    private void SavePending(Dictionary<string, PendingConfirmation> pending)
    {
        var data = new FileData
        {
            Pending = pending.ToDictionary(
                kvp => kvp.Key,
                kvp => new FilePendingConfirmation
                {
                    CapabilityName = kvp.Value.CapabilityName,
                    UserId = kvp.Value.UserId,
                    ExpiresAt = kvp.Value.ExpiresAt
                })
        };
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_filePath, json);
    }

    private sealed record PendingConfirmation(string CapabilityName, string UserId, DateTimeOffset ExpiresAt);

    private sealed class FileData
    {
        public Dictionary<string, FilePendingConfirmation> Pending { get; set; } = new();
    }

    private sealed class FilePendingConfirmation
    {
        public string CapabilityName { get; set; } = "";
        public string UserId { get; set; } = "";
        public DateTimeOffset ExpiresAt { get; set; }
    }
}
