using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using AngleSharp;
using AngleSharp.Dom;
using Microsoft.Playwright;
using TorrentBot.Contracts.Capabilities;

namespace TorrentBot.Plugins.Tools;

internal static class WebReaderTools
{
    public static async Task<CapabilityResult> ReadAsync(string input, CancellationToken ct)
    {
        var (url, options) = ParseInput(input);
        if (url is null) return Usage("/read https://example.com/article format=telegram|markdown|pdf");
        var pageResult = await RenderAsync(url, options, ct);
        var article = ExtractArticle(pageResult.Html, url);
        if (article is null || article.Text.Length < 80) return Ok("Could not identify a readable article on this page. Try /screenshot URL.");
        var format = options.GetValueOrDefault("format", "telegram").ToLowerInvariant();
        if (format == "pdf")
        {
            return FeatureArtifacts.Binary("article.pdf", "application/pdf", pageResult.Pdf, "Reader-mode PDF generated.");
        }
        var heading = string.IsNullOrWhiteSpace(article.Title) ? "" : $"# {article.Title}\n\n";
        var source = $"\n\nSource: {url}";
        var text = heading + article.Text.Trim() + source;
        return Ok(text.Length > 11000 ? text[..10950] + "\n\n[article truncated]" : text);
    }

    public static async Task<CapabilityResult> ScreenshotAsync(string input, CancellationToken ct)
    {
        var (url, options) = ParseInput(input);
        if (url is null) return Usage("/screenshot https://example.com [device=mobile] [format=png|jpg|pdf]");
        var pageResult = await RenderAsync(url, options, ct, screenshotOnly: true);
        var format = options.GetValueOrDefault("format", "png").ToLowerInvariant();
        return format switch
        {
            "pdf" => FeatureArtifacts.Binary("page.pdf", "application/pdf", pageResult.Pdf, "Full-page PDF generated."),
            "jpg" or "jpeg" => FeatureArtifacts.Binary("page.jpg", "image/jpeg", pageResult.Screenshot, "Full-page screenshot generated."),
            _ => FeatureArtifacts.Binary("page.png", "image/png", pageResult.Screenshot, "Full-page screenshot generated.")
        };
    }

    private static async Task<RenderedPage> RenderAsync(string url, Dictionary<string, string> options, CancellationToken ct, bool screenshotOnly = false)
    {
        await PublicUrlGuard.EnsureAsync(new Uri(url), ct);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
            ExecutablePath = ResolveBrowserPath(),
            Args = ["--no-sandbox"]
        });
        var device = options.GetValueOrDefault("device", "desktop").ToLowerInvariant();
        var (width, height) = device == "mobile" ? (390, 844) : device == "tablet" ? (768, 1024) : (1440, 900);
        if (int.TryParse(options.GetValueOrDefault("width"), out var configuredWidth)) width = Math.Clamp(configuredWidth, 320, 2400);
        if (int.TryParse(options.GetValueOrDefault("height"), out var configuredHeight)) height = Math.Clamp(configuredHeight, 320, 2400);
        var context = await browser.NewContextAsync(new BrowserNewContextOptions { ViewportSize = new ViewportSize { Width = width, Height = height }, Locale = options.GetValueOrDefault("lang", "en") });
        var page = await context.NewPageAsync();
        try
        {
            await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30000 });
            if (int.TryParse(options.GetValueOrDefault("delay"), out var delay)) await page.WaitForTimeoutAsync(Math.Clamp(delay, 0, 10) * 1000);
            var hide = options.GetValueOrDefault("hide");
            if (!string.IsNullOrWhiteSpace(hide))
            {
                var css = string.Join("", hide.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(selector => selector + "{display:none!important}"));
                await page.AddStyleTagAsync(new PageAddStyleTagOptions { Content = css });
            }
            var screenshot = await page.ScreenshotAsync(new PageScreenshotOptions
            {
                FullPage = true,
                Type = options.GetValueOrDefault("format", "png").Equals("jpg", StringComparison.OrdinalIgnoreCase) || options.GetValueOrDefault("format", "png").Equals("jpeg", StringComparison.OrdinalIgnoreCase) ? ScreenshotType.Jpeg : ScreenshotType.Png,
                Quality = options.GetValueOrDefault("format", "png").Equals("jpg", StringComparison.OrdinalIgnoreCase) ? Math.Clamp(int.TryParse(options.GetValueOrDefault("quality"), out var quality) ? quality : 85, 1, 100) : null
            });
            var pdf = await page.PdfAsync(new PagePdfOptions { Format = "A4", PrintBackground = true });
            var html = screenshotOnly ? "" : await page.ContentAsync();
            return new RenderedPage(html, screenshot, pdf);
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    private static Article? ExtractArticle(string html, string url)
    {
        var config = Configuration.Default;
        var context = BrowsingContext.New(config);
        var document = context.OpenAsync(request => request.Content(html).Address(url)).GetAwaiter().GetResult();
        foreach (var node in document.QuerySelectorAll("script,style,noscript,nav,header,footer,aside,form,iframe,svg,button")) node.Remove();
        var candidates = document.QuerySelectorAll("article,main,[role='main'],body").ToArray();
        var candidate = candidates.OrderByDescending(Score).FirstOrDefault();
        if (candidate is null) return null;
        var title = document.QuerySelector("h1")?.TextContent.Trim() ?? document.Title ?? string.Empty;
        var blocks = candidate.QuerySelectorAll("h1,h2,h3,p,li,blockquote,pre").Select(x => Regex.Replace(x.TextContent, "\\s+", " ").Trim()).Where(x => x.Length > 0);
        var text = string.Join("\n\n", blocks);
        if (text.Length < 80) text = Regex.Replace(candidate.TextContent, "\\s+", " ").Trim();
        return new Article(title, text);
    }

    private static int Score(IElement element)
    {
        var text = element.TextContent?.Length ?? 0;
        var paragraphs = element.QuerySelectorAll("p").Length;
        var links = element.QuerySelectorAll("a").Sum(x => x.TextContent?.Length ?? 0);
        return text + paragraphs * 200 - links / 2;
    }

    private static (string? Url, Dictionary<string, string> Options) ParseInput(string input)
    {
        var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var url = parts.FirstOrDefault(x => Uri.TryCreate(x, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https");
        var options = System.Text.RegularExpressions.Regex.Matches(input, "(?<key>[a-zA-Z][a-zA-Z0-9_]*)=(?<value>\\\"[^\\\"]*\\\"|'[^']*'|[^ ]+)").Cast<Match>().ToDictionary(x => x.Groups["key"].Value, x => x.Groups["value"].Value.Trim('"', '\''), StringComparer.OrdinalIgnoreCase);
        return (url, options);
    }

    private static CapabilityResult Ok(string message) => new(true, message, message);
    private static CapabilityResult Usage(string message) => new(true, null, message);
    private static string? ResolveBrowserPath() =>
        Environment.GetEnvironmentVariable("PLAYWRIGHT_EXECUTABLE_PATH")
        ?? (File.Exists("/usr/bin/chromium") ? "/usr/bin/chromium" : null);
    private sealed record RenderedPage(string Html, byte[] Screenshot, byte[] Pdf);
    private sealed record Article(string Title, string Text);
}

internal static class PublicUrlGuard
{
    public static async Task EnsureAsync(Uri uri, CancellationToken ct)
    {
        if (uri.Scheme is not ("http" or "https")) throw new FormatException("Only HTTP(S) URLs are supported.");
        foreach (var ip in await Dns.GetHostAddressesAsync(uri.Host, ct))
        {
            var bytes = ip.GetAddressBytes();
            if (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any) || ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || (bytes.Length == 4 && (bytes[0] == 0 || bytes[0] == 10 || bytes[0] == 127 || (bytes[0] == 169 && bytes[1] == 254) || (bytes[0] == 192 && bytes[1] == 168) || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31))))
                throw new FormatException("Private, loopback and link-local hosts are blocked.");
        }
    }
}
