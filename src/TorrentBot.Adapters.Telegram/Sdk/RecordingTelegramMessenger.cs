namespace TorrentBot.Adapters.Telegram.Sdk;

public sealed class RecordingTelegramMessenger : ITelegramMessenger
{
    private long _nextMessageId = 1000;

    public List<TelegramOutboundMessage> Sent { get; } = [];
    public List<TelegramOutboundMessage> Edited { get; } = [];
    public List<(string CallbackQueryId, string? Text)> CallbackAnswers { get; } = [];

    public Task<long> SendTextAsync(long chatId, string text, IReadOnlyList<TelegramInlineButton>? buttons = null, CancellationToken ct = default)
    {
        var messageId = _nextMessageId++;
        Sent.Add(new TelegramOutboundMessage(chatId, text, messageId, buttons));
        return Task.FromResult(messageId);
    }

    public Task EditTextAsync(long chatId, long messageId, string text, CancellationToken ct = default)
    {
        Edited.Add(new TelegramOutboundMessage(chatId, text, messageId));
        return Task.CompletedTask;
    }

    public Task AnswerCallbackAsync(string callbackQueryId, string? text = null, CancellationToken ct = default)
    {
        CallbackAnswers.Add((callbackQueryId, text));
        return Task.CompletedTask;
    }

    public List<(long ChatId, string FileName, int Size)> Photos { get; } = [];
    public List<(long ChatId, string FileName, int Size)> Documents { get; } = [];
    public List<(long ChatId, string FileName, long Size)> Audios { get; } = [];
    public List<(long ChatId, string FileName, long Size)> Videos { get; } = [];
    public Dictionary<string, byte[]> Downloads { get; } = new(StringComparer.Ordinal);

    public Task SendPhotoAsync(long chatId, byte[] content, string fileName, CancellationToken ct = default)
    {
        Photos.Add((chatId, fileName, content.Length));
        return Task.CompletedTask;
    }

    public Task SendDocumentAsync(long chatId, byte[] content, string fileName, CancellationToken ct = default)
    {
        Documents.Add((chatId, fileName, content.Length));
        return Task.CompletedTask;
    }

    public async Task SendAudioAsync(long chatId, Stream content, string fileName, CancellationToken ct = default)
    {
        Audios.Add((chatId, fileName, content.Length));
        await Task.CompletedTask;
    }

    public async Task SendVideoAsync(long chatId, Stream content, string fileName, CancellationToken ct = default)
    {
        Videos.Add((chatId, fileName, content.Length));
        await Task.CompletedTask;
    }

    public async Task SendDocumentAsync(long chatId, Stream content, string fileName, CancellationToken ct = default)
    {
        Documents.Add((chatId, fileName, checked((int)content.Length)));
        await Task.CompletedTask;
    }

    public Task<byte[]> DownloadFileAsync(string fileId, CancellationToken ct = default) =>
        Task.FromResult(Downloads.GetValueOrDefault(fileId, Array.Empty<byte>()));
}
