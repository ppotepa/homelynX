namespace TorrentBot.Contracts.Context;

public sealed class RequestContext : IRequestContext
{
    public RequestContext(
        string traceId,
        string invocationId,
        string userId,
        string? jobId = null,
        string? capabilityName = null,
        string source = "unknown",
        string? chatId = null,
        string? messageId = null,
        IReadOnlyDictionary<string, object>? properties = null)
    {
        TraceId = traceId;
        InvocationId = invocationId;
        UserId = userId;
        JobId = jobId;
        CapabilityName = capabilityName;
        Source = source;
        ChatId = chatId;
        MessageId = messageId;
        Properties = properties ?? new Dictionary<string, object>();
    }

    public string TraceId { get; init; }
    public string InvocationId { get; init; }
    public string UserId { get; init; }
    public string? JobId { get; set; }
    public string? CapabilityName { get; init; }
    public string Source { get; init; }
    public string? ChatId { get; init; }
    public string? MessageId { get; init; }
    public IReadOnlyDictionary<string, object> Properties { get; init; }
}