using System.Globalization;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using QRCoder;
using ZXing;
using ZXing.Common;
using FormatException = System.FormatException;
using TorrentBot.Contracts.Capabilities;
using TorrentBot.Contracts.Context;
using TorrentBot.Contracts.Plugins;
using TorrentBot.Engine;

namespace TorrentBot.Plugins.Tools;

public sealed class ToolsPlugin : IPlugin
{
    public string Name => "tools";
    public string Version => "1.0.0";

    public void Register(IPluginRegistrationContext context)
    {
        var store = new ToolsStore(Environment.GetEnvironmentVariable("TORRENTBOT_TOOLS_DB"));
        var featureStore = new FeatureStore(Environment.GetEnvironmentVariable("TORRENTBOT_TOOLS_DB"));
        context.RegisterService(store);
        context.RegisterService(featureStore);
        context.RegisterService(new TrackingMonitor(featureStore));
        context.RegisterService(new ShortLinkService(store));
        foreach (var command in ToolCommands.All)
        {
            context.RegisterCapability(command.Contract, new ToolHandler(command.Command.TrimStart('/'), store, featureStore), command.Command);
        }
    }
}

internal sealed record ToolCommand(string Name, string Command, CapabilityContract Contract);

internal static class ToolCommands
{
    private static CapabilityContract C(string name, string description, RiskLevel risk = RiskLevel.Safe, bool readOnly = false) =>
        new(name, description, [], risk, ResponseSpec: new ResponseConstructionSpec("text"), IsReadOnly: readOnly, Scope: "all");

    public static readonly IReadOnlyList<ToolCommand> All =
    [
        T("note", "tools.note", "Create and manage personal notes."), T("todo", "tools.todo", "Create and manage personal tasks."),
        T("remind", "tools.remind", "Create a reminder with a relative duration or ISO date."), T("reminders", "tools.reminders", "List active reminders.", true),
        T("timer", "tools.timer", "Create a countdown timer."), T("timers", "tools.timers", "List active timers.", true),
        T("poll", "tools.poll", "Create, list and close text polls."), T("choose", "tools.choose", "Choose one item randomly."),
        T("dice", "tools.dice", "Roll dice such as 2d6."), T("paste", "tools.paste", "Store and retrieve short private text snippets."),
        T("calc", "tools.calc", "Evaluate a safe arithmetic expression.", true), T("convert", "tools.convert", "Convert common units and temperatures.", true),
        T("password", "tools.password", "Generate a cryptographically secure password."), T("passphrase", "tools.passphrase", "Generate a memorable secure passphrase."),
        T("hash", "tools.hash", "Hash text with SHA-256 or SHA-512.", true), T("uuid", "tools.uuid", "Generate a UUID."),
        T("base64", "tools.base64", "Encode or decode Base64.", true), T("slug", "tools.slug", "Generate a URL slug.", true),
        T("date", "tools.date", "Show current date/time in a requested timezone.", true), T("time", "tools.time", "Show current time in a requested timezone.", true), T("timestamp", "tools.timestamp", "Convert a timestamp to ISO time.", true),
        T("weather", "tools.weather", "Show current weather for a city.", true), T("rate", "tools.rate", "Show a currency exchange rate.", true), T("qr", "tools.qr", "Create a local QR image or QR payload.", true), T("barcode", "tools.barcode", "Create a local barcode image.", true), T("shorten", "tools.shorten", "Create and manage short URLs.", false),
        T("url", "tools.url", "Inspect, clean and trace HTTP URLs.", true), T("json", "tools.json", "Format, minify and query JSON.", true), T("urlencode", "tools.urlencode", "Encode or decode URL text.", true), T("color", "tools.color", "Inspect a color and calculate contrast.", true), T("text_stats", "tools.text_stats", "Calculate text statistics.", true), T("base", "tools.base", "Convert numbers between bases.", true),
        T("mediainfo", "tools.mediainfo", "Inspect a local media file with ffprobe.", true), T("thumbnail", "tools.thumbnail", "Create a thumbnail from a local video.", true), T("extract_audio", "tools.extract_audio", "Extract an MP3 audio track from a local video.", true), T("gif", "tools.gif", "Create a short GIF clip from a local video.", true), T("compress", "tools.compress", "Compress a local video to H.264 MP4.", true),
        T("chiptune", "tools.chiptune", "Render MIDI or text notes as a chiptune audio file.", true), T("read", "tools.read", "Extract the readable article from a public URL.", true), T("screenshot", "tools.screenshot", "Capture a full-page screenshot of a public URL.", true), T("track", "tools.track", "Track parcels and notify on status changes."), T("home", "tools.home", "Set and inspect the private home location."), T("location", "tools.location", "Save and use named private locations."), T("distance", "tools.distance", "Calculate distance between locations.", true), T("map", "tools.map", "Generate a location map artifact.", true),
        T("translate", "tools.translate", "Translate text through an explicitly configured LibreTranslate-compatible service."), T("summarize", "tools.summarize", "Create a deterministic short extract without an LLM.", true), T("rewrite", "tools.rewrite", "Apply deterministic text transformations without an LLM.", true), T("extract_tasks", "tools.extract_tasks", "Extract task-like sentences without an LLM.", true),
        T("files", "tools.files", "Inspect and safely move media files.", true), T("trash", "tools.trash", "List and restore files moved to the bot trash.", true),
        T("service_logs", "tools.service_logs", "Read the tail of an allowlisted service log.", true), T("network", "tools.network", "Check DNS and HTTP connectivity.", true),
        T("services", "tools.services", "Check configured service endpoints.", true), T("webhook", "tools.webhook", "Manage outbound webhook definitions.")
    ];

    private static ToolCommand T(string command, string name, string description, bool readOnly = false) =>
        new(name, "/" + command, C(name, description, readOnly ? RiskLevel.Safe : RiskLevel.Safe, readOnly));
}

internal sealed class ToolHandler(string name, ToolsStore store, FeatureStore featureStore) : ICapabilityHandler
{
    public async Task<CapabilityResult> ExecuteAsync(CapabilityContext context, IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken)
    {
        var text = parameters.GetValueOrDefault("text")?.ToString()?.Trim() ?? string.Empty;
        var user = context.User.UserId;
        try
        {
            var result = name switch
            {
                "note" => await Notes(text, user), "todo" => await Todos(text, user),
                "remind" or "reminders" => await Reminders(text, user, name), "timer" or "timers" => await Timers(text, user, name),
                "poll" => await Polls(text, user), "choose" => Choose(text), "dice" => Dice(text), "paste" => await Pastes(text, user),
                "calc" => Calc(text), "convert" => Convert(text), "password" => Password(text, false), "passphrase" => Password(text, true),
                "hash" => Hash(text), "uuid" => new CapabilityResult(true, Guid.NewGuid().ToString(), Guid.NewGuid().ToString()),
                "base64" => Base64(text), "slug" => Slug(text), "date" => Date(text), "timestamp" => Timestamp(text),
                "time" => Date(text), "weather" => await Weather(text, cancellationToken), "rate" => await Rate(text, cancellationToken), "qr" => Qr(text), "barcode" => Barcode(text), "shorten" => await Shorten(text, user), "url" => await UrlTools(text, cancellationToken), "json" => JsonTools(text), "urlencode" => UrlEncode(text), "color" => Color(text), "text_stats" => TextStats(text), "base" => BaseConvert(text),
                "mediainfo" => await MediaInfo(text, cancellationToken), "thumbnail" => await Thumbnail(text, cancellationToken), "extract_audio" => await ExtractAudio(text, cancellationToken), "gif" => await Gif(text, cancellationToken), "compress" => await Compress(text, cancellationToken),
                "chiptune" => await ChiptuneTools.ExecuteAsync(text, cancellationToken), "read" => await WebReaderTools.ReadAsync(text, cancellationToken), "screenshot" => await WebReaderTools.ScreenshotAsync(text, cancellationToken), "track" => await TrackingTools.ExecuteAsync(text, user, featureStore, cancellationToken), "home" or "location" => await LocationTools.ExecuteAsync(name == "home" ? $"home {text}" : text, user, featureStore), "distance" or "map" => await LocationTools.ExecuteAsync($"{name} {text}", user, featureStore),
                "translate" => await Translate(text, cancellationToken), "summarize" => Summarize(text), "rewrite" => Rewrite(text), "extract_tasks" => ExtractTasks(text),
                "files" => Files(text), "trash" => Trash(text), "service_logs" => ServiceLogs(text), "network" => await Network(text, cancellationToken), "services" => await Services(text, cancellationToken),
                "webhook" => await Webhooks(text, user), _ => new(false, Message: "Unknown tool.")
            };
            return result with { IsDryRun = context.IsDryRun };
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or InvalidOperationException or IOException or HttpRequestException or JsonException or SocketException or TimeoutException or KeyNotFoundException)
        { return new CapabilityResult(false, Message: ex.Message, IsDryRun: context.IsDryRun); }
    }

    private async Task<CapabilityResult> Notes(string s, string u)
    {
        var (sub, body) = Split(s); if (sub is "list" or "search") return ListResult(await store.ListNotes(u, body), "Notes");
        if (sub == "show" && long.TryParse(body, out var showId)) return Ok(await store.GetNote(u, showId), "Note");
        if (sub == "tag") { var p=body.Split(' ',2,StringSplitOptions.RemoveEmptyEntries); if(p.Length<2||!long.TryParse(p[0],out var tid)) return Ok(null,"Usage: /note tag ID tag1,tag2"); await store.SetNoteTags(u,tid,p[1]); return Ok(null,$"Tagged note #{tid}."); }
        if (sub == "edit") { var p=body.Split(' ',2,StringSplitOptions.RemoveEmptyEntries); if(p.Length<2||!long.TryParse(p[0],out var eid)) return Ok(null,"Usage: /note edit ID new text"); await store.UpdateNote(u,eid,p[1]); return Ok(null,$"Updated note #{eid}."); }
        if (sub is "delete" && int.TryParse(body, out var id)) { await store.DeleteNote(u, id); return Ok(null, $"Deleted note {id}."); }
        if (string.IsNullOrWhiteSpace(body)) return ListResult(await store.ListNotes(u, ""), "Usage: /note add text | /note list | /note delete ID");
        var id2 = await store.AddNote(u, body); return Ok(null, $"Saved note #{id2}.");
    }
    private async Task<CapabilityResult> Todos(string s, string u)
    {
        var (sub, body) = Split(s); if (sub is "list" or "show") return ListResult(await store.ListTodos(u, false), "Tasks");
        if (sub is "done" && int.TryParse(body, out var id)) { await store.SetTodo(u, id, true); return Ok(null, $"Completed task #{id}."); }
        if (sub is "undo" && int.TryParse(body, out id)) { await store.SetTodo(u, id, false); return Ok(null, $"Reopened task #{id}."); }
        if (sub is "delete" && int.TryParse(body, out id)) { await store.DeleteTodo(u, id); return Ok(null, $"Deleted task #{id}."); }
        if (sub == "edit") { var p=body.Split(' ',2,StringSplitOptions.RemoveEmptyEntries); if(p.Length<2||!long.TryParse(p[0],out var eid)) return Ok(null,"Usage: /todo edit ID new text"); await store.UpdateTodo(u,eid,p[1]); return Ok(null,$"Updated task #{eid}."); }
        if (sub == "clear") { await store.ClearTodos(u); return Ok(null,"Completed tasks cleared."); }
        if (string.IsNullOrWhiteSpace(body)) return ListResult(await store.ListTodos(u, false), "Usage: /todo add text | /todo list | /todo done ID");
        var id2 = await store.AddTodo(u, body); return Ok(null, $"Added task #{id2}.");
    }
    private async Task<CapabilityResult> Reminders(string s, string u, string kind)
    {
        if (kind == "reminders" || s.Equals("list", StringComparison.OrdinalIgnoreCase)) return ListResult(await store.ListReminders(u), "Reminders");
        var (action, actionBody) = Split(s); if (action == "cancel" && long.TryParse(actionBody, out var cancelId)) { await store.DeleteReminder(u,cancelId); return Ok(null,$"Reminder #{cancelId} cancelled."); }
        var parts = s.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries); if (parts.Length < 2) return Ok(null, "Usage: /remind 20m text or /remind 2026-08-20T12:00 text");
        var due = ParseDuration(parts[0]);
        if (!due.HasValue && DateTimeOffset.TryParse(parts[0], out var date)) due = date;
        if (!due.HasValue) throw new FormatException("Invalid reminder time.");
        var id = await store.AddReminder(u, parts[1], due.Value); return Ok(null, $"Reminder #{id} set for {due:yyyy-MM-dd HH:mm} UTC.");
    }
    private async Task<CapabilityResult> Timers(string s, string u, string kind)
    {
        if (kind == "timers" || s.Equals("list", StringComparison.OrdinalIgnoreCase)) return ListResult(await store.ListTimers(u), "Timers");
        var (action, actionBody) = Split(s); if (action == "cancel" && long.TryParse(actionBody, out var cancelId)) { await store.DeleteReminder(u,cancelId); return Ok(null,$"Timer #{cancelId} cancelled."); }
        var parts = s.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries); if (parts.Length < 2) return Ok(null, "Usage: /timer 10m text");
        var due = ParseDuration(parts[0]) ?? throw new FormatException("Timer needs duration such as 10m or 1h.");
        var id = await store.AddReminder(u, "TIMER: " + parts[1], due); return Ok(null, $"Timer #{id} set for {due:yyyy-MM-dd HH:mm} UTC.");
    }
    private async Task<CapabilityResult> Polls(string s, string u)
    {
        var (sub, body) = Split(s); if (sub == "list") return ListResult(await store.ListPolls(u), "Polls");
        if (sub == "close" && int.TryParse(body, out var close)) { await store.ClosePoll(u, close); return Ok(null, $"Closed poll #{close}."); }
        if (sub == "results" && long.TryParse(body, out var resultId)) return ListResult(await store.PollResults(resultId), $"Poll #{resultId} results");
        if (sub == "vote") { var p=body.Split(' ',2,StringSplitOptions.RemoveEmptyEntries); if(p.Length<2||!long.TryParse(p[0],out var pollId)||!int.TryParse(p[1],out var option)||option<1) return Ok(null,"Usage: /poll vote ID option"); await store.VotePoll(u,pollId,option-1); return Ok(null,$"Vote recorded for poll #{pollId}, option {option}."); }
        var parts = s.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries); if (parts.Length < 3) return Ok(null, "Usage: /poll question | option A | option B");
        var id = await store.AddPoll(u, parts[0], parts[1..]); return Ok(null, $"Poll #{id}: {parts[0]}\n" + string.Join("\n", parts[1..].Select((x, i) => $"{i + 1}. {x}")));
    }
    private async Task<CapabilityResult> Pastes(string s, string u)
    {
        var (sub, body) = Split(s); if (sub == "list") return ListResult(await store.ListPastes(u), "Pastes");
        if (sub == "show" && int.TryParse(body, out var show)) return Ok(await store.GetPaste(u, show), "Paste");
        if (sub == "delete" && int.TryParse(body, out var del)) { await store.DeletePaste(u, del); return Ok(null, "Paste deleted."); }
        if (string.IsNullOrWhiteSpace(body)) return Ok(null, "Usage: /paste add text | /paste list | /paste show ID");
        var id = await store.AddPaste(u, body); return Ok(null, $"Paste #{id} saved.");
    }
    private static CapabilityResult Choose(string s) { var a = s.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries); return a.Length == 0 ? new(false, Message: "Usage: /choose red, blue, green") : Ok(a[RandomNumberGenerator.GetInt32(a.Length)], "Choice"); }
    private static CapabilityResult Dice(string s) { var m = Regex.Match(string.IsNullOrWhiteSpace(s) ? "1d6" : s, @"^(\d*)d(\d+)$", RegexOptions.IgnoreCase); if (!m.Success) throw new FormatException("Use NdM, for example 2d6."); var n = Math.Clamp(int.TryParse(m.Groups[1].Value, out var x) ? x : 1, 1, 100); var sides = Math.Clamp(int.Parse(m.Groups[2].Value), 2, 1000); var rolls = Enumerable.Range(0, n).Select(_ => RandomNumberGenerator.GetInt32(1, sides + 1)).ToArray(); return Ok(rolls, $"{n}d{sides}: {rolls.Sum()} ({string.Join(", ", rolls)})"); }
    private static CapabilityResult Calc(string s) { if (string.IsNullOrWhiteSpace(s)) return Ok(null, "Usage: /calc (12.5 * 4) + 2"); var value = new SafeCalculator().Evaluate(s); return Ok(value, $"{s} = {value.ToString(CultureInfo.InvariantCulture)}"); }
    private static CapabilityResult Convert(string s) { var m = Regex.Match(s, @"^(-?[\d.,]+)\s*([a-zA-Z°]+)\s+(?:to\s+)?([a-zA-Z°]+)$"); if (!m.Success) return Ok(null, "Usage: /convert 10 km mi or /convert 32 C F"); var v = double.Parse(m.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture); var from = m.Groups[2].Value.ToLowerInvariant(); var to = m.Groups[3].Value.ToLowerInvariant(); var result = UnitConverter(v, from, to); return Ok(result, $"{v} {from} = {result} {to}"); }
    private static double UnitConverter(double v, string f, string t) { if (f == t) return v; if (f is "c" or "°c") return t is "f" or "°f" ? v * 9 / 5 + 32 : t is "k" ? v + 273.15 : throw new FormatException("Unsupported conversion."); if (f is "f" or "°f") return t is "c" or "°c" ? (v - 32) * 5 / 9 : t is "k" ? (v - 32) * 5 / 9 + 273.15 : throw new FormatException("Unsupported conversion."); var factors = new Dictionary<string, double> { ["km"] = 1000, ["m"] = 1, ["mi"] = 1609.344, ["ft"] = .3048, ["cm"] = .01, ["mm"] = .001, ["kg"] = 1, ["g"] = .001, ["lb"] = .45359237 }; if (!factors.ContainsKey(f) || !factors.ContainsKey(t)) throw new FormatException("Supported units: km, m, mi, ft, cm, mm, kg, g, lb."); return v * factors[f] / factors[t]; }
    private static CapabilityResult Password(string s, bool phrase) { var n = int.TryParse(s, out var x) ? Math.Clamp(x, 8, 128) : phrase ? 4 : 24; if (phrase) { var words = new[] { "amber", "cedar", "orbit", "velvet", "pixel", "river", "cobalt", "lunar", "maple", "quiet", "rocket", "signal" }; return Ok(string.Join('-', Enumerable.Range(0, n).Select(_ => words[RandomNumberGenerator.GetInt32(words.Length)])), "Passphrase"); } var chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!@#$%"; return Ok(new string(Enumerable.Range(0, n).Select(_ => chars[RandomNumberGenerator.GetInt32(chars.Length)]).ToArray()), "Password"); }
    private static CapabilityResult Hash(string s) { var parts = s.Split(' ', 2); var alg = parts.Length == 2 && parts[0].ToLowerInvariant() is "sha512" or "sha256" ? parts[0].ToLowerInvariant() : "sha256"; var input = parts.Length == 2 ? parts[1] : s; var bytes = alg == "sha512" ? SHA512.HashData(Encoding.UTF8.GetBytes(input)) : SHA256.HashData(Encoding.UTF8.GetBytes(input)); var hex = System.Convert.ToHexString(bytes).ToLowerInvariant(); return Ok(hex, $"{alg}: {hex}"); }
    private static CapabilityResult Base64(string s) { var p = s.Split(' ', 2); var decode = p.Length > 1 && p[0].Equals("decode", StringComparison.OrdinalIgnoreCase); var value = decode ? p[1] : s; var output = decode ? Encoding.UTF8.GetString(global::System.Convert.FromBase64String(value)) : global::System.Convert.ToBase64String(Encoding.UTF8.GetBytes(value)); return Ok(output, output); }
    private static CapabilityResult Slug(string s) { var slug = Regex.Replace(Regex.Replace(s.Normalize(NormalizationForm.FormD), "\\p{Mn}+", ""), "[^a-zA-Z0-9]+", "-").Trim('-').ToLowerInvariant(); return Ok(slug, slug); }
    private static CapabilityResult Date(string s) { var zone = string.IsNullOrWhiteSpace(s) ? "UTC" : s; var tz = TimeZoneInfo.FindSystemTimeZoneById(zone); var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tz); return Ok(now.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture), now.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture)); }
    private static CapabilityResult Timestamp(string s) { if (!long.TryParse(s, out var unix)) return Ok(DateTimeOffset.UtcNow.ToUnixTimeSeconds(), "Current Unix timestamp"); var dt = DateTimeOffset.FromUnixTimeSeconds(unix); return Ok(dt, dt.ToString("O")); }
    private static CapabilityResult Qr(string s) { if (string.IsNullOrWhiteSpace(s)) return Ok(null, "Usage: /qr text|url|wifi|contact ..."); var payload=QrPayload(s);using var generator=new QRCodeGenerator();using var data=generator.CreateQrCode(payload,QRCodeGenerator.ECCLevel.Q);var png=new PngByteQRCode(data).GetGraphic(20);return Binary("qr.png","image/png",png,$"QR generated ({payload.Length} bytes payload)."); }
    private static string QrPayload(string s){var p=s.Split(' ',2,StringSplitOptions.RemoveEmptyEntries);if(p.Length==1)return s;if(p[0].Equals("wifi",StringComparison.OrdinalIgnoreCase)){var fields=ParseOptions(p[1]);var security=fields.GetValueOrDefault("security","WPA");var hidden=fields.GetValueOrDefault("hidden","false");return $"WIFI:T:{security};S:{EscapeQr(fields.GetValueOrDefault("ssid",""))};P:{EscapeQr(fields.GetValueOrDefault("password",""))};H:{hidden};;";}if(p[0].Equals("url",StringComparison.OrdinalIgnoreCase))return p[1];if(p[0].Equals("email",StringComparison.OrdinalIgnoreCase)){var f=ParseOptions(p[1]);return $"MATMSG:TO:{f.GetValueOrDefault("to","")};SUB:{f.GetValueOrDefault("subject","")};BODY:{f.GetValueOrDefault("body","")};;";}if(p[0].Equals("geo",StringComparison.OrdinalIgnoreCase)){var v=p[1].Split(' ',StringSplitOptions.RemoveEmptyEntries);return v.Length>=2?$"geo:{v[0]},{v[1]}":p[1];}return p[1];}
    private static CapabilityResult Barcode(string s){var p=s.Split(' ',2,StringSplitOptions.RemoveEmptyEntries);if(p.Length<2)return Ok(null,"Usage: /barcode code128 value");var format=p[0].ToLowerInvariant() switch{"code128"=>BarcodeFormat.CODE_128,"code39"=>BarcodeFormat.CODE_39,"ean13"=>BarcodeFormat.EAN_13,"ean8"=>BarcodeFormat.EAN_8,"upca"=>BarcodeFormat.UPC_A,"datamatrix"=>BarcodeFormat.DATA_MATRIX,"pdf417"=>BarcodeFormat.PDF_417,"aztec"=>BarcodeFormat.AZTEC,_=>throw new FormatException("Formats: code128, code39, ean13, ean8, upca, datamatrix, pdf417, aztec.")};var writer=new BarcodeWriterGeneric{Format=format,Options=new EncodingOptions{Width=900,Height=300,Margin=20}};var matrix=writer.Encode(p[1]);var svg=MatrixSvg(matrix);return Binary("barcode.svg","image/svg+xml",Encoding.UTF8.GetBytes(svg),$"{p[0].ToUpperInvariant()} barcode generated.");}
    private static string MatrixSvg(BitMatrix matrix){var sb=new StringBuilder($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {matrix.Width} {matrix.Height}\" shape-rendering=\"crispEdges\"><rect width=\"100%\" height=\"100%\" fill=\"white\"/><path fill=\"black\" d=\"");for(var y=0;y<matrix.Height;y++)for(var x=0;x<matrix.Width;x++)if(matrix[x,y])sb.Append($"M{x} {y}h1v1H{x}z");return sb.Append("\"/></svg>").ToString();}
    private async Task<CapabilityResult> Shorten(string s,string user){var p=Split(s);if(p.Item1 is "list" or "links")return ListResult(await store.ListShortLinks(user),"Short links");if(p.Item1=="disable"){await store.DisableShortLink(user,p.Item2);return Ok(null,$"Short link {p.Item2} disabled.");}if(p.Item1=="delete"){await store.DeleteShortLink(user,p.Item2);return Ok(null,$"Short link {p.Item2} deleted.");}var url=p.Item1=="create"?p.Item2:s; if(!Uri.TryCreate(url.Split(' ',2)[0],UriKind.Absolute,out var uri)||uri.Scheme is not ("http" or "https"))return Ok(null,"Usage: /shorten https://example.com [slug=name] [expires=7d] [max=10]");var options=ParseOptions(s);var code=options.GetValueOrDefault("slug",RandomCode(7));if(!Regex.IsMatch(code,"^[A-Za-z0-9_-]{3,64}$"))return Ok(null,"Slug must be 3-64 characters: letters, numbers, _ or -.");DateTimeOffset? expires=null;if(options.TryGetValue("expires",out var ex)){expires=ParseDuration(ex);if(!expires.HasValue)return Ok(null,"expires must look like 30s, 7d or 2h.");}int? max=null;if(options.TryGetValue("max",out var maxText)){if(!int.TryParse(maxText,out var maxValue)||maxValue<1)return Ok(null,"max must be a positive integer.");max=maxValue;}try{await store.CreateShortLink(user,code,uri.ToString(),options.GetValueOrDefault("title",uri.Host),options.GetValueOrDefault("tags",""),expires,max);}catch(SqliteException dbEx) when(dbEx.SqliteErrorCode==19){return Ok(null,$"Short-code '{code}' is already in use.");}var baseUrl=Environment.GetEnvironmentVariable("TORRENTBOT_SHORTENER_BASE_URL")??"http://localhost:8089";var shortUrl=baseUrl.TrimEnd('/')+"/s/"+code;return Ok(new Dictionary<string,object?>{{"code",code},{"url",shortUrl},{"target",uri.ToString()}},$"Short URL: {shortUrl}\nTarget: {uri}");}
    private static string RandomCode(int length){const string chars="abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";return new string(Enumerable.Range(0,length).Select(_=>chars[RandomNumberGenerator.GetInt32(chars.Length)]).ToArray());}
    private static CapabilityResult Binary(string fileName,string contentType,byte[] bytes,string message)=>new(true,new Dictionary<string,object?>{{"toolArtifact",new Dictionary<string,object?>{{"fileName",fileName},{"contentType",contentType},{"contentBase64",System.Convert.ToBase64String(bytes)}}}},message);
    private static Dictionary<string,string> ParseOptions(string s){var result=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);foreach(var token in Regex.Matches(s, "(?<key>[a-zA-Z][a-zA-Z0-9_]*)=(?<value>\\\"[^\\\"]*\\\"|'[^']*'|[^ ]+)").Cast<Match>()){var v=token.Groups["value"].Value.Trim('"','\'');result[token.Groups["key"].Value]=v;}return result;}
    private static string EscapeQr(string s)=>s.Replace("\\","\\\\").Replace(";","\\;").Replace(",","\\,").Replace(":","\\:");
    private static CapabilityResult UrlEncode(string s){var p=s.Split(' ',2,StringSplitOptions.RemoveEmptyEntries);if(p.Length<2)return Ok(Uri.EscapeDataString(s),Uri.EscapeDataString(s));var output=p[0].Equals("decode",StringComparison.OrdinalIgnoreCase)?Uri.UnescapeDataString(p[1]):Uri.EscapeDataString(p[1]);return Ok(output,output);}
    private static CapabilityResult JsonTools(string s)
    {
        var p = s.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var mode = p.ElementAtOrDefault(0)?.ToLowerInvariant() ?? "format";
        if (mode == "query")
        {
            if (p.Length < 2) return Ok(null, "Usage: /json query $.path {\"path\":{\"value\":1}}");
            var remainder = p[1].Trim();
            var separator = remainder.IndexOf('|');
            var jsonStart = remainder.IndexOfAny(['{', '[']);
            var path = separator >= 0 ? remainder[..separator].Trim() : jsonStart > 0 ? remainder[..jsonStart].Trim() : "";
            var json = separator >= 0 ? remainder[(separator + 1)..].Trim() : jsonStart >= 0 ? remainder[jsonStart..].Trim() : "";
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(json)) return Ok(null, "Usage: /json query $.path {\"path\":{\"value\":1}}");
            using var queryDoc = JsonDocument.Parse(json);
            var root = queryDoc.RootElement;
            foreach (var part in path.Trim('$', '.').Split('.', StringSplitOptions.RemoveEmptyEntries))
            {
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(part, out root)) return Ok(null, "JSON path not found.");
            }
            var output = root.GetRawText();
            return Ok(output, output);
        }

        var payload = p.Length > 1 ? p[1] : s;
        using var doc = JsonDocument.Parse(payload);
        var compact = JsonSerializer.Serialize(doc.RootElement);
        if (mode == "minify") return Ok(compact, compact);
        var formatted = JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
        return Ok(formatted, formatted);
    }
    private static CapabilityResult Color(string s){var hex=s.Trim().TrimStart('#');if(hex.Length==3)hex=string.Concat(hex.Select(c=>$"{c}{c}"));if(hex.Length!=6||!int.TryParse(hex,System.Globalization.NumberStyles.HexNumber,CultureInfo.InvariantCulture,out var rgb))throw new FormatException("Use a color such as #ff8800.");var r=(rgb>>16)&255;var g=(rgb>>8)&255;var b=rgb&255;var lum=RelativeLuminance(r,g,b);var white=(1.05)/(lum+0.05);var black=(lum+0.05)/0.05;var message=$"#{hex.ToUpperInvariant()} RGB({r},{g},{b}) contrast black={black:F2}, white={white:F2}";return Ok(new Dictionary<string,object?>{{"hex","#"+hex.ToUpperInvariant()},{"r",r},{"g",g},{"b",b},{"contrast_black",black},{"contrast_white",white}},message);}
    private static double RelativeLuminance(int r,int g,int b){static double C(int x){var v=x/255.0;return v<=.03928?v/12.92:Math.Pow((v+.055)/1.055,2.4);}return .2126*C(r)+.7152*C(g)+.0722*C(b);}
    private static CapabilityResult TextStats(string s){var chars=s.Length;var bytes=Encoding.UTF8.GetByteCount(s);var words=Regex.Matches(s,"\\S+").Count;var lines=s.Length==0?0:s.Split('\n').Length;return Ok(new Dictionary<string,object?>{{"characters",chars},{"bytes_utf8",bytes},{"words",words},{"lines",lines},{"paragraphs",Regex.Split(s.Trim(),"\\n\\s*\\n").Count(x=>!string.IsNullOrWhiteSpace(x))}},$"characters={chars}, bytes={bytes}, words={words}, lines={lines}");}
    private static CapabilityResult BaseConvert(string s){var p=s.Split(' ',StringSplitOptions.RemoveEmptyEntries);if(p.Length<3)return Ok(null,"Usage: /base 255 dec hex");try{var value=System.Convert.ToInt64(p[0],ParseBase(p[1]));var output=System.Convert.ToString(value,ParseBase(p[2])).ToUpperInvariant();return Ok(output,output);}catch(Exception ex) when(ex is FormatException or ArgumentException or OverflowException){return new CapabilityResult(false,null,$"Invalid number/base: {ex.Message}");}}
    private static int ParseBase(string value){return value.Trim().ToLowerInvariant() switch{"bin" or "binary"=>2,"oct" or "octal"=>8,"dec" or "decimal"=>10,"hex" or "hexadecimal"=>16,_ when int.TryParse(value,NumberStyles.Integer,CultureInfo.InvariantCulture,out var numeric) && numeric is >=2 and <=36=>numeric,_=>throw new ArgumentException($"Unsupported base '{value}'. Use bin, oct, dec, hex or 2-36.")};}
    private static async Task<CapabilityResult> MediaInfo(string s, CancellationToken ct)
    {
        var input = MediaInput(s, "Usage: /mediainfo relative/path.mp4");
        var result = await RunProcess("ffprobe", ["-v", "quiet", "-print_format", "json", "-show_format", "-show_streams", input], ct);
        if (result.ExitCode != 0) throw new InvalidOperationException(result.Error.Trim());
        var compact = JsonSerializer.Serialize(JsonDocument.Parse(result.Output).RootElement);
        return Ok(compact, compact.Length > 3500 ? compact[..3500] + "…" : compact);
    }
    private static async Task<CapabilityResult> Thumbnail(string s, CancellationToken ct)
    {
        var parts = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return Ok(null, "Usage: /thumbnail relative/path.mp4 [timestamp]");
        var input = MediaInput(parts[0], "Usage: /thumbnail relative/path.mp4 [timestamp]");
        var stamp = parts.ElementAtOrDefault(1) ?? "00:00:01";
        var result = await RunProcess("ffmpeg", ["-hide_banner", "-loglevel", "error", "-ss", stamp, "-i", input, "-frames:v", "1", "-f", "image2pipe", "-vcodec", "png", "pipe:1"], ct, captureBinary: true);
        if (result.ExitCode != 0) throw new InvalidOperationException(result.Error.Trim());
        return Binary("thumbnail.png", "image/png", result.BinaryOutput, $"Thumbnail generated at {stamp}.");
    }
    private static async Task<CapabilityResult> ExtractAudio(string s, CancellationToken ct)
    {
        var input = MediaInput(s, "Usage: /extract_audio relative/path.mp4 [bitrate]");
        var bitrate = s.Split(' ', StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(1) ?? "128k";
        var output = TempMediaPath(".mp3");
        try
        {
            var result = await RunProcess("ffmpeg", ["-y", "-hide_banner", "-loglevel", "error", "-i", input, "-vn", "-c:a", "libmp3lame", "-b:a", bitrate, output], ct);
            if (result.ExitCode != 0) throw new InvalidOperationException(result.Error.Trim());
            return await FileArtifact(output, "audio.mp3", "audio/mpeg", "Audio extracted.");
        }
        finally { TryDelete(output); }
    }
    private static async Task<CapabilityResult> Gif(string s, CancellationToken ct)
    {
        var p = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (p.Length < 3) return Ok(null, "Usage: /gif relative/path.mp4 start end [fps]");
        var input = MediaInput(p[0], "Usage: /gif relative/path.mp4 start end [fps]");
        var fps = p.ElementAtOrDefault(3) ?? "10";
        var output = TempMediaPath(".gif");
        try
        {
            var result = await RunProcess("ffmpeg", ["-y", "-hide_banner", "-loglevel", "error", "-ss", p[1], "-to", p[2], "-i", input, "-vf", $"fps={fps},scale=640:-1:flags=lanczos", "-loop", "0", output], ct);
            if (result.ExitCode != 0) throw new InvalidOperationException(result.Error.Trim());
            return await FileArtifact(output, "clip.gif", "image/gif", $"GIF created from {p[1]} to {p[2]}.");
        }
        finally { TryDelete(output); }
    }
    private static async Task<CapabilityResult> Compress(string s, CancellationToken ct)
    {
        var input = MediaInput(s, "Usage: /compress relative/path.mp4 [crf]");
        var crf = s.Split(' ', StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(1) ?? "28";
        var output = TempMediaPath(".mp4");
        try
        {
            var result = await RunProcess("ffmpeg", ["-y", "-hide_banner", "-loglevel", "error", "-i", input, "-map", "0:v:0", "-map", "0:a?", "-c:v", "libx264", "-crf", crf, "-preset", "veryfast", "-c:a", "aac", "-b:a", "128k", "-movflags", "+faststart", output], ct);
            if (result.ExitCode != 0) throw new InvalidOperationException(result.Error.Trim());
            return await FileArtifact(output, "compressed.mp4", "video/mp4", "Video compressed.");
        }
        finally { TryDelete(output); }
    }
    private static string MediaInput(string s, string usage)
    {
        var relative = s.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(relative)) throw new FormatException(usage);
        var path = SafePath(FileRoot(), relative);
        if (!File.Exists(path)) throw new FormatException($"Media file not found: {relative}");
        return path;
    }
    private static string TempMediaPath(string extension)
    {
        var dir = Path.Combine(FileRoot(), ".tools"); Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"{Guid.NewGuid():N}{extension}");
    }
    private static async Task<CapabilityResult> FileArtifact(string path, string name, string contentType, string message)
    {
        const long maxBytes = 45L * 1024 * 1024;
        var info = new FileInfo(path);
        if (info.Length > maxBytes)
        {
            var retainedDirectory = Path.Combine(FileRoot(), "converted");
            Directory.CreateDirectory(retainedDirectory);
            var retained = Path.Combine(retainedDirectory, $"{Path.GetFileNameWithoutExtension(name)}-{Guid.NewGuid():N}{Path.GetExtension(name)}");
            File.Move(path, retained);
            return Ok(new { path = retained, size = info.Length }, $"Output is {info.Length / 1048576.0:F1} MB; saved at {retained} (Telegram attachment limit reached).");
        }
        return Binary(name, contentType, await File.ReadAllBytesAsync(path), message);
    }
    private static async Task<ProcessResult> RunProcess(string executable, IEnumerable<string> arguments, CancellationToken ct, bool captureBinary = false)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        ct = timeout.Token;
        var start = new ProcessStartInfo(executable) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"{executable} is not installed.");
        try
        {
            var errorTask = process.StandardError.ReadToEndAsync(ct);
            var outputTask = captureBinary ? Task.FromResult(string.Empty) : process.StandardOutput.ReadToEndAsync(ct);
            byte[] binary = [];
            if (captureBinary) using (var buffer = new MemoryStream()) { await process.StandardOutput.BaseStream.CopyToAsync(buffer, ct); binary = buffer.ToArray(); }
            await process.WaitForExitAsync(ct);
            return new ProcessResult(process.ExitCode, await outputTask, await errorTask, binary);
        }
        catch
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            throw;
        }
    }
    private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }
    private sealed record ProcessResult(int ExitCode, string Output, string Error, byte[] BinaryOutput);
    private static async Task<CapabilityResult> UrlTools(string s, CancellationToken ct)
    {
        var parts = s.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var mode = parts.ElementAtOrDefault(0)?.ToLowerInvariant() ?? "inspect";
        var raw = parts.Length > 1 ? parts[1] : s;
        if (parts.Length == 1 && Uri.TryCreate(s, UriKind.Absolute, out _)) mode = "inspect";
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            return Ok(null, "Usage: /url inspect|clean|redirects https://example.com");

        if (mode == "clean")
        {
            var builder = new UriBuilder(uri);
            var kept = builder.Query.TrimStart('?')
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Where(part =>
                {
                    var key = part.Split('=', 2)[0];
                    return !key.StartsWith("utm_", StringComparison.OrdinalIgnoreCase)
                        && !key.Equals("fbclid", StringComparison.OrdinalIgnoreCase)
                        && !key.Equals("gclid", StringComparison.OrdinalIgnoreCase)
                        && !key.Equals("mc_cid", StringComparison.OrdinalIgnoreCase)
                        && !key.Equals("mc_eid", StringComparison.OrdinalIgnoreCase);
                });
            builder.Query = string.Join('&', kept);
            return Ok(builder.Uri.ToString(), builder.Uri.ToString());
        }

        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(8) };
        var current = uri;
        var chain = new List<string>();
        for (var hop = 0; hop < (mode == "redirects" ? 6 : 1); hop++)
        {
            await EnsurePublicHost(current, ct);
            using var response = await http.SendAsync(new HttpRequestMessage(HttpMethod.Head, current), HttpCompletionOption.ResponseHeadersRead, ct);
            var location = response.Headers.Location is null ? null : new Uri(current, response.Headers.Location).ToString();
            chain.Add($"{(int)response.StatusCode} {current}");
            if (mode != "redirects" || location is null || !((int)response.StatusCode is >= 300 and < 400))
            {
                var message = $"{(int)response.StatusCode} {response.ReasonPhrase}\nType: {response.Content.Headers.ContentType?.MediaType}\nSize: {response.Content.Headers.ContentLength?.ToString() ?? "unknown"}\nNext: {location ?? ""}";
                if (mode == "redirects") message = string.Join("\n", chain) + "\n" + message;
                return Ok(new Dictionary<string, object?> { ["status"] = (int)response.StatusCode, ["content_type"] = response.Content.Headers.ContentType?.MediaType, ["content_length"] = response.Content.Headers.ContentLength, ["location"] = location, ["chain"] = chain.ToArray() }, message);
            }
            if (!Uri.TryCreate(location, UriKind.Absolute, out current) || current.Scheme is not ("http" or "https")) throw new FormatException("Redirect target is not an HTTP(S) URL.");
        }
        return Ok(chain.ToArray(), string.Join("\n", chain));
    }
    private static async Task EnsurePublicHost(Uri uri,CancellationToken ct){foreach(var ip in await Dns.GetHostAddressesAsync(uri.Host,ct)){var bytes=ip.GetAddressBytes();if(System.Net.IPAddress.IsLoopback(ip)||ip.Equals(System.Net.IPAddress.Any)||ip.Equals(System.Net.IPAddress.IPv6Any)||ip.IsIPv6LinkLocal||ip.IsIPv6SiteLocal||(bytes.Length==4&&(bytes[0]==0||bytes[0]==10||(bytes[0]==100&&bytes[1] is >=64 and <=127)||(bytes[0]==127)||(bytes[0]==169&&bytes[1]==254)||(bytes[0]==192&&bytes[1]==168)||(bytes[0]==172&&bytes[1] is >=16 and <=31))))throw new FormatException("Private, link-local and loopback hosts are blocked for URL inspection.");}}
    private static async Task<CapabilityResult> Weather(string s, CancellationToken ct) { if (string.IsNullOrWhiteSpace(s)) return Ok(null, "Usage: /weather Warsaw"); using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) }; http.DefaultRequestHeaders.UserAgent.ParseAdd("HomelynxBot/1.0"); using var doc = await http.GetAsync("https://wttr.in/" + Uri.EscapeDataString(s) + "?format=j1", ct); doc.EnsureSuccessStatusCode(); using var json = await System.Text.Json.JsonDocument.ParseAsync(await doc.Content.ReadAsStreamAsync(ct), cancellationToken: ct); var current = json.RootElement.GetProperty("current_condition")[0]; var temp = current.GetProperty("temp_C").GetString(); var feels = current.GetProperty("FeelsLikeC").GetString(); var desc = current.GetProperty("weatherDesc")[0].GetProperty("value").GetString(); return Ok(new { city = s, temp_c = temp, feels_like_c = feels, description = desc }, $"{s}: {temp}°C, odczuwalna {feels}°C, {desc}"); }
    private static async Task<CapabilityResult> Rate(string s, CancellationToken ct) { var p = s.Split(' ', StringSplitOptions.RemoveEmptyEntries); var from = p.ElementAtOrDefault(0)?.ToUpperInvariant() ?? "EUR"; var to = p.ElementAtOrDefault(1)?.ToUpperInvariant() ?? "PLN"; using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) }; var json = await http.GetFromJsonAsync<System.Text.Json.JsonElement>($"https://api.frankfurter.app/latest?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}", ct); var rate = json.GetProperty("rates").GetProperty(to).GetDecimal(); return Ok(rate, $"1 {from} = {rate} {to}"); }
    private static async Task<CapabilityResult> Translate(string s, CancellationToken ct) { var p=s.Split(' ',3,StringSplitOptions.RemoveEmptyEntries);if(p.Length<3)return Ok(null,"Usage: /translate en pl text");var endpoint=Environment.GetEnvironmentVariable("LIBRETRANSLATE_URL");if(string.IsNullOrWhiteSpace(endpoint))return Ok(null,"Translation is disabled until LIBRETRANSLATE_URL is configured (no LLM is used).");using var http=new HttpClient{Timeout=TimeSpan.FromSeconds(15)};var response=await http.PostAsJsonAsync(endpoint.TrimEnd('/')+"/translate",new {q=p[2],source=p[0],target=p[1],format="text"},ct);response.EnsureSuccessStatusCode();var json=await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(ct);var translated=json.GetProperty("translatedText").GetString()??"";return Ok(translated,translated); }
    private static CapabilityResult Summarize(string s) { if(string.IsNullOrWhiteSpace(s))return Ok(null,"Usage: /summarize text");var sentences=Regex.Split(s.Trim(),"(?<=[.!?])\\s+").Where(x=>!string.IsNullOrWhiteSpace(x)).Take(3).ToArray();var output=string.Join(" ",sentences);if(output.Length<s.Length)output+=" …";return Ok(output,output); }
    private static CapabilityResult Rewrite(string s) { var p=s.Split(' ',2,StringSplitOptions.RemoveEmptyEntries);if(p.Length<2)return Ok(null,"Usage: /rewrite upper|lower|trim text");var output=p[0].ToLowerInvariant() switch {"upper"=>p[1].ToUpperInvariant(),"lower"=>p[1].ToLowerInvariant(),"trim"=>Regex.Replace(p[1],"\\s+"," ").Trim(),_=>throw new FormatException("Modes: upper, lower, trim.")};return Ok(output,output); }
    private static CapabilityResult ExtractTasks(string s) { var tasks=Regex.Split(s,"(?<=[.!?])\\s+").Where(x=>Regex.IsMatch(x,"\\b(todo|task|must|need to|should|trzeba|muszę|zrobić)\\b",RegexOptions.IgnoreCase)).Select(x=>x.Trim()).ToArray();return Ok(tasks,tasks.Length==0?"No task-like sentences found.":string.Join("\n",tasks)); }
    private static CapabilityResult Files(string s) { var root=FileRoot(); if(!Directory.Exists(root)) return Ok(null,$"Media root unavailable: {root}"); var (op,arg)=Split(s); var all=EnumerateFiles(root); if(op=="duplicates"){var duplicates=all.GroupBy(f=>f.Length).Where(g=>g.Count()>1).SelectMany(g=>g).Take(100).Where(f=>true).ToArray();var hashes=duplicates.GroupBy(f=>FileHash(f.FullName)).Where(g=>g.Count()>1).SelectMany(g=>g).ToArray();var duplicateLines=hashes.Select(f=>$"{f.Length/1048576.0:F1} MB  {f.FullName}").ToArray();return Ok(duplicateLines,duplicateLines.Length==0?"No exact duplicates found.":string.Join('\n',duplicateLines));} IEnumerable<FileInfo> files=op switch { "recent"=>all.OrderByDescending(f=>f.LastWriteTimeUtc).Take(20), "large"=>all.Where(f=>f.Length >= (long)(double.TryParse(arg,out var mb)?mb:1024)*1048576).OrderByDescending(f=>f.Length).Take(20), "find"=>all.Where(f=>f.Name.Contains(arg,StringComparison.OrdinalIgnoreCase)).Take(50), "move"=>MoveToTrash(arg,root), _=>all.Where(f=>string.IsNullOrWhiteSpace(s)||f.Name.Contains(s,StringComparison.OrdinalIgnoreCase)).OrderByDescending(f=>f.LastWriteTimeUtc).Take(20) }; var lines=files.Select(f=>$"{f.Length/1048576.0:F1} MB  {f.FullName}").ToArray(); return Ok(lines,string.IsNullOrWhiteSpace(string.Join('\n',lines))?"No matching files.":string.Join('\n',lines)); }
    private static string FileHash(string path){using var sha=SHA256.Create();using var stream=File.OpenRead(path);return System.Convert.ToHexString(sha.ComputeHash(stream));}
    private static IEnumerable<FileInfo> MoveToTrash(string arg,string root) { var p=arg.Split(' ',2,StringSplitOptions.RemoveEmptyEntries); if(p.Length<2||!p[0].Equals("confirm",StringComparison.OrdinalIgnoreCase)) throw new FormatException("Moving files requires: /files move confirm relative/path"); var source=SafePath(root,p[1]); if(!File.Exists(source)) throw new FormatException("File not found inside media root."); var trash=Path.Combine(root,".trash");Directory.CreateDirectory(trash);var target=Path.Combine(trash,Guid.NewGuid()+"_"+Path.GetFileName(source));File.Move(source,target);return [new FileInfo(target)]; }
    private static CapabilityResult Trash(string s) { var root=FileRoot(); var trash=Path.Combine(root,".trash");Directory.CreateDirectory(trash);var (op,arg)=Split(s);if(op=="restore"){var f=Directory.EnumerateFiles(trash).FirstOrDefault(x=>Path.GetFileName(x).StartsWith(arg+"_",StringComparison.OrdinalIgnoreCase)||Path.GetFileName(x).Equals(arg,StringComparison.OrdinalIgnoreCase));if(f is null)return Ok(null,"Trash item not found.");var target=Path.Combine(root,Path.GetFileName(f).Split('_',2).ElementAtOrDefault(1)??Path.GetFileName(f));File.Move(f,target);return Ok(null,$"Restored {target}.");}var lines=Directory.EnumerateFiles(trash).Select(x=>Path.GetFileName(x)).Take(100).ToArray();return Ok(lines,lines.Length==0?"Trash is empty.":string.Join('\n',lines)); }
    private static CapabilityResult ServiceLogs(string s) { var root=Environment.GetEnvironmentVariable("TORRENTBOT_LOG_ROOT")??"/app/logs";if(!Directory.Exists(root))return Ok(null,$"Log root unavailable: {root}");var name=string.IsNullOrWhiteSpace(s)?"":Path.GetFileName(s);var files=Directory.EnumerateFiles(root,"*",SearchOption.TopDirectoryOnly).Where(x=>string.IsNullOrWhiteSpace(name)||Path.GetFileName(x).Contains(name,StringComparison.OrdinalIgnoreCase)).Take(10).ToArray();var lines=files.SelectMany(f=>File.ReadLines(f).TakeLast(50).Select(line=>$"[{Path.GetFileName(f)}] {line}"));return Ok(lines.ToArray(),string.Join('\n',lines)); }
    private static string FileRoot()=>Environment.GetEnvironmentVariable("TORRENTBOT_MEDIA_ROOT")??"/media";
    private static IEnumerable<FileInfo> EnumerateFiles(string root)=>Directory.EnumerateFiles(root,"*",SearchOption.AllDirectories).Where(p=>!p.Contains(Path.DirectorySeparatorChar+".trash"+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)).Select(p=>new FileInfo(p));
    private static string SafePath(string root,string relative){var full=Path.GetFullPath(Path.Combine(root,relative));var baseRoot=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;if(!full.StartsWith(baseRoot,StringComparison.OrdinalIgnoreCase))throw new FormatException("Path must stay inside media root.");return full;}
    private static async Task<CapabilityResult> Network(string s, CancellationToken ct) { var host = string.IsNullOrWhiteSpace(s) ? "example.com" : s.Trim(); var addresses = await Dns.GetHostAddressesAsync(host, ct); return Ok(addresses.Select(x => x.ToString()).ToArray(), $"{host}: {string.Join(", ", addresses.Select(x => x.ToString()))}"); }
    private static async Task<CapabilityResult> Services(string text, CancellationToken ct) { if(text.StartsWith("logs",StringComparison.OrdinalIgnoreCase))return ServiceLogs(text.Length>4?text[4..].Trim():""); var names = new[] { "JACKETT_URL", "QBITTORRENT_URL", "JELLYFIN_URL" }; using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) }; var lines = new List<string>(); foreach (var n in names) { var url = Environment.GetEnvironmentVariable(n); if (string.IsNullOrWhiteSpace(url)) continue; try { using var r = await http.GetAsync(url, ct); lines.Add($"{n}: {(int)r.StatusCode} {r.ReasonPhrase}"); } catch (Exception ex) { lines.Add($"{n}: unavailable ({ex.Message})"); } } return Ok(lines.ToArray(), lines.Count == 0 ? "No service URLs configured." : string.Join('\n', lines)); }
    private async Task<CapabilityResult> Webhooks(string s, string u) { var (sub, body) = Split(s); if (sub == "list") return ListResult(await store.ListWebhooks(u), "Webhooks"); if (sub == "revoke" && int.TryParse(body, out var id)) { await store.RevokeWebhook(u, id); return Ok(null, "Webhook revoked."); } if (sub == "trigger" && long.TryParse(body, out var triggerId)) { var hook=await store.GetWebhook(u,triggerId); if(!hook.HasValue)return Ok(null,"Webhook not found or revoked."); using var http=new HttpClient{Timeout=TimeSpan.FromSeconds(8)};using var response=await http.PostAsJsonAsync(hook.Value.Url,new {event_name="homelynx.manual",user_id=u,occurred_at=DateTimeOffset.UtcNow},CancellationToken.None); return Ok(null,$"Webhook #{triggerId} {((int)response.StatusCode)} {response.ReasonPhrase}."); } var p = body.Split(' ', 2); if (p.Length != 2 || !Uri.TryCreate(p[0], UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return Ok(null, "Usage: /webhook create https://example/webhook label | /webhook trigger ID"); var newId = await store.AddWebhook(u, p[0], p[1]); return Ok(null, $"Webhook #{newId} created."); }
    private static (string, string) Split(string s) { var p = s.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries); return p.Length == 0 ? ("", "") : p.Length == 1 ? (p[0].ToLowerInvariant(), "") : (p[0].ToLowerInvariant(), p[1].Trim()); }
    private static DateTimeOffset? ParseDuration(string s) { var m = Regex.Match(s, "^(\\d+)(s|m|h|d)$", RegexOptions.IgnoreCase); if (!m.Success) return null; var n = int.Parse(m.Groups[1].Value); return DateTimeOffset.UtcNow.Add(m.Groups[2].Value.ToLowerInvariant() switch { "s" => TimeSpan.FromSeconds(n), "m" => TimeSpan.FromMinutes(n), "h" => TimeSpan.FromHours(n), _ => TimeSpan.FromDays(n) }); }
    private static CapabilityResult Ok(object? data, string message) => new(true, data, message);
    private static CapabilityResult ListResult(object[] items, string title) => Ok(items, items.Length == 0 ? $"{title}: empty." : title + ":\n" + string.Join("\n", items.Select(x => x?.ToString())));
}

internal sealed class SafeCalculator
{
    private string _s = string.Empty; private int _i;
    public double Evaluate(string s) { _s = s.Replace(" ", ""); _i = 0; var v = Expr(); if (_i != _s.Length) throw new FormatException("Invalid arithmetic expression."); return v; }
    private double Expr() { var v = Term(); while (_i < _s.Length && (_s[_i] == '+' || _s[_i] == '-')) { var op = _s[_i++]; var x = Term(); v = op == '+' ? v + x : v - x; } return v; }
    private double Term() { var v = Factor(); while (_i < _s.Length && (_s[_i] == '*' || _s[_i] == '/')) { var op = _s[_i++]; var x = Factor(); if (op == '/' && x == 0) throw new FormatException("Division by zero."); v = op == '*' ? v * x : v / x; } return v; }
    private double Factor() { if (_i < _s.Length && _s[_i] == '(') { _i++; var v = Expr(); if (_i >= _s.Length || _s[_i++] != ')') throw new FormatException("Missing closing parenthesis."); return v; } var start = _i; if (_i < _s.Length && (_s[_i] == '-' || _s[_i] == '+')) _i++; while (_i < _s.Length && (char.IsDigit(_s[_i]) || _s[_i] == '.')) _i++; if (start == _i) throw new FormatException("Expected a number."); return double.Parse(_s[start.._i], CultureInfo.InvariantCulture); }
}
