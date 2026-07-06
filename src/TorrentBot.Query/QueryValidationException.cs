namespace TorrentBot.Query;

public sealed class QueryValidationException(string message) : Exception(message);