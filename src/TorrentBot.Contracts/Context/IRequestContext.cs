namespace TorrentBot.Contracts.Context;

public interface IRequestContext
{
    string TraceId { get; }
    string InvocationId { get; }
    string UserId { get; }
    string? JobId { get; set; }
    string? CapabilityName { get; }
    string Source { get; }
    string? ChatId { get; }
    string? MessageId { get; }
    IReadOnlyDictionary<string, object> Properties { get; }
}