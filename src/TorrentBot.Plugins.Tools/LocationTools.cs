using System.Globalization;
using System.Text;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using TorrentBot.Contracts.Capabilities;

namespace TorrentBot.Plugins.Tools;

internal static class LocationTools
{
    public static async Task<CapabilityResult> ExecuteAsync(string input, string userId, FeatureStore store)
    {
        var (command, rest) = Split(input);
        return command switch
        {
            "home" => await Home(rest, userId, store),
            "save" or "location" => await Save(rest, userId, store),
            "list" => await List(userId, store),
            "delete" or "remove" => await Delete(rest, userId, store),
            "distance" => await Distance(rest, userId, store),
            "map" => await Map(rest, userId, store),
            _ => await Home(input, userId, store)
        };
    }

    private static async Task<CapabilityResult> Home(string input, string userId, FeatureStore store)
    {
        var (command, rest) = Split(input);
        if (command is "show" or "get") return await Show("home", userId, store);
        if (command is "delete" or "remove") return await Delete("home", userId, store);
        if (command == "set") input = rest;
        if (TryCoordinates(input, out var lat, out var lon))
        {
            await store.SaveLocationAsync(userId, "home", lat, lon, "Home");
            return Ok($"Home saved: {lat.ToString("F6", CultureInfo.InvariantCulture)}, {lon.ToString("F6", CultureInfo.InvariantCulture)}");
        }
        if (string.IsNullOrWhiteSpace(input)) return await Show("home", userId, store);
        return Usage("/home set 52.2297 21.0122 | /home show | /home delete");
    }

    private static async Task<CapabilityResult> Save(string input, string userId, FeatureStore store)
    {
        var parts = input.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !TryCoordinates(parts[1], out var lat, out var lon)) return Usage("/location save work 52.2297 21.0122");
        await store.SaveLocationAsync(userId, parts[0].ToLowerInvariant(), lat, lon, parts[0]);
        return Ok($"Location '{parts[0]}' saved: {lat.ToString("F6", CultureInfo.InvariantCulture)}, {lon.ToString("F6", CultureInfo.InvariantCulture)}");
    }

    private static async Task<CapabilityResult> List(string userId, FeatureStore store)
    {
        var locations = await store.ListLocationsAsync(userId);
        return Ok(locations.Length == 0 ? "No saved locations." : string.Join('\n', locations.Select(x => $"{x.Name}: {x.Latitude:F6}, {x.Longitude:F6}")));
    }

    private static async Task<CapabilityResult> Show(string name, string userId, FeatureStore store)
    {
        var location = await store.GetLocationAsync(userId, name);
        return location is null ? Ok($"Location '{name}' is not configured.") : Ok($"{location.Name}: {location.Latitude:F6}, {location.Longitude:F6}\nhttps://www.openstreetmap.org/?mlat={location.Latitude.ToString(CultureInfo.InvariantCulture)}&mlon={location.Longitude.ToString(CultureInfo.InvariantCulture)}");
    }

    private static async Task<CapabilityResult> Delete(string input, string userId, FeatureStore store)
    {
        var name = string.IsNullOrWhiteSpace(input) ? "home" : input.Trim().ToLowerInvariant();
        await store.DeleteLocationAsync(userId, name); return Ok($"Location '{name}' deleted.");
    }

    private static async Task<CapabilityResult> Distance(string input, string userId, FeatureStore store)
    {
        var parts = input.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return Usage("/distance home 50.0614 19.9383 | /distance home work");
        var first = await Resolve(parts[0], userId, store); var second = await Resolve(parts[1], userId, store);
        if (first is null || second is null) return Ok("Could not resolve both locations. Use coordinates or /location save name lat lon.");
        var km = Haversine(first.Value.Latitude, first.Value.Longitude, second.Value.Latitude, second.Value.Longitude);
        var bearing = Bearing(first.Value.Latitude, first.Value.Longitude, second.Value.Latitude, second.Value.Longitude);
        return Ok($"{parts[0]} → {parts[1]}\nDistance: {km:F1} km\nBearing: {bearing:F0}°");
    }

    private static async Task<CapabilityResult> Map(string input, string userId, FeatureStore store)
    {
        var parts = input.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var first = await Resolve(parts.ElementAtOrDefault(0) ?? "home", userId, store);
        var second = parts.Length > 1 ? await Resolve(parts[1], userId, store) : null;
        if (first is null) return Ok("Location is not configured.");
        try
        {
            var points = second is null ? new[] { first.Value } : new[] { first.Value, second.Value };
            var png = await RenderMapAsync(points, ct: CancellationToken.None);
            return FeatureArtifacts.Binary("map.png", "image/png", png, "Map generated with OpenStreetMap. © OpenStreetMap contributors.");
        }
        catch (HttpRequestException)
        {
            return FeatureArtifacts.TextFile("map.svg", SchematicMap(first.Value, second), "image/svg+xml", "Map generated in fallback mode; map provider unavailable.");
        }
    }

    private static async Task<byte[]> RenderMapAsync((double Latitude, double Longitude, string Label)[] points, CancellationToken ct)
    {
        const int width = 800, height = 600, tileSize = 256;
        var zoom = 14;
        while (zoom > 3 && WorldSpan(points, zoom).Max() > 2.2) zoom--;
        var world = Math.Pow(2, zoom); var projected = points.Select(p => (X: WorldX(p.Longitude, world), Y: WorldY(p.Latitude, world))).ToArray();
        var minX = projected.Min(p => p.X); var maxX = projected.Max(p => p.X); var minY = projected.Min(p => p.Y); var maxY = projected.Max(p => p.Y);
        var centerX = (minX + maxX) / 2; var centerY = (minY + maxY) / 2;
        var topLeftX = centerX * tileSize - width / 2; var topLeftY = centerY * tileSize - height / 2;
        using var image = new Image<Rgba32>(width, height, new Rgba32(235, 235, 235));
        using var client = new HttpClient(); client.DefaultRequestHeaders.UserAgent.ParseAdd("Homelynx/1.0 (+https://github.com/homelynx)");
        var tileTemplate = Environment.GetEnvironmentVariable("TORRENTBOT_MAP_TILE_URL")?.Trim();
        if (string.IsNullOrWhiteSpace(tileTemplate)) tileTemplate = "https://tile.openstreetmap.org/{z}/{x}/{y}.png";
        var cacheRoot = Environment.GetEnvironmentVariable("TORRENTBOT_MAP_TILE_CACHE")?.Trim();
        if (string.IsNullOrWhiteSpace(cacheRoot)) cacheRoot = Path.Combine(Path.GetTempPath(), "homelynx-map-tiles");
        var firstTileX = (int)Math.Floor(topLeftX / tileSize); var firstTileY = (int)Math.Floor(topLeftY / tileSize);
        for (var ty = firstTileY; ty <= firstTileY + height / tileSize + 1; ty++)
        for (var tx = firstTileX; tx <= firstTileX + width / tileSize + 1; tx++)
        {
            var wrappedX = ((tx % (int)world) + (int)world) % (int)world;
            var safeY = Math.Clamp(ty, 0, (int)world - 1);
            var cachePath = Path.Combine(cacheRoot, zoom.ToString(CultureInfo.InvariantCulture), wrappedX.ToString(CultureInfo.InvariantCulture), safeY.ToString(CultureInfo.InvariantCulture) + ".png");
            byte[] tileBytes;
            if (File.Exists(cachePath)) tileBytes = await File.ReadAllBytesAsync(cachePath, ct);
            else
            {
                var url = tileTemplate.Replace("{z}", zoom.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
                    .Replace("{x}", wrappedX.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
                    .Replace("{y}", safeY.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
                using var response = await client.GetAsync(url, ct); response.EnsureSuccessStatusCode();
                tileBytes = await response.Content.ReadAsByteArrayAsync(ct);
                Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
                await File.WriteAllBytesAsync(cachePath, tileBytes, ct);
            }
            using var tileStream = new MemoryStream(tileBytes, writable: false);
            using var tile = await Image.LoadAsync<Rgba32>(tileStream, ct);
            image.Mutate(ctx => ctx.DrawImage(tile, new Point((int)(tx * tileSize - topLeftX), (int)(ty * tileSize - topLeftY)), 1f));
        }
        for (var i = 0; i < projected.Length; i++)
        {
            var x = (int)(projected[i].X * tileSize - topLeftX); var y = (int)(projected[i].Y * tileSize - topLeftY);
            DrawLine(image, 400, 300, x, y, new Rgba32(230, 57, 70)); DrawCircle(image, x, y, 9, new Rgba32(29, 53, 87));
        }
        await using var output = new MemoryStream(); await image.SaveAsPngAsync(output, ct); return output.ToArray();
    }

    private static double[] WorldSpan((double Latitude, double Longitude, string Label)[] points, int zoom)
    {
        var n = Math.Pow(2, zoom); var xs = points.Select(p => WorldX(p.Longitude, n)); var ys = points.Select(p => WorldY(p.Latitude, n)); return [xs.Max() - xs.Min(), ys.Max() - ys.Min()];
    }
    private static double WorldX(double longitude, double world) => (longitude + 180) / 360 * world;
    private static double WorldY(double latitude, double world) => (1 - Math.Log(Math.Tan(latitude * Math.PI / 180) + 1 / Math.Cos(latitude * Math.PI / 180)) / Math.PI) / 2 * world;
    private static void DrawCircle(Image<Rgba32> image, int cx, int cy, int radius, Rgba32 color) { for (var y = -radius; y <= radius; y++) for (var x = -radius; x <= radius; x++) if (x * x + y * y <= radius * radius && cx + x >= 0 && cx + x < image.Width && cy + y >= 0 && cy + y < image.Height) image[cx + x, cy + y] = color; }
    private static void DrawLine(Image<Rgba32> image, int x0, int y0, int x1, int y1, Rgba32 color) { var dx = Math.Abs(x1 - x0); var sx = x0 < x1 ? 1 : -1; var dy = -Math.Abs(y1 - y0); var sy = y0 < y1 ? 1 : -1; var error = dx + dy; while (true) { if (x0 >= 0 && x0 < image.Width && y0 >= 0 && y0 < image.Height) image[x0, y0] = color; if (x0 == x1 && y0 == y1) break; var e2 = 2 * error; if (e2 >= dy) { error += dy; x0 += sx; } if (e2 <= dx) { error += dx; y0 += sy; } } }
    private static string SchematicMap((double Latitude, double Longitude, string Label) first, (double Latitude, double Longitude, string Label)? second) => $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"800\" height=\"600\"><rect width=\"800\" height=\"600\" fill=\"#f1faee\"/><circle cx=\"400\" cy=\"300\" r=\"12\" fill=\"#e63946\"/><text x=\"20\" y=\"570\" font-size=\"14\">{first.Latitude:F6}, {first.Longitude:F6} © OpenStreetMap contributors</text></svg>";

    private static async Task<(double Latitude, double Longitude, string Label)?> Resolve(string value, string userId, FeatureStore store)
    {
        if (TryCoordinates(value, out var lat, out var lon)) return (lat, lon, value);
        var location = await store.GetLocationAsync(userId, value.Trim().ToLowerInvariant());
        return location is null ? null : (location.Latitude, location.Longitude, location.Label);
    }

    private static bool TryCoordinates(string input, out double latitude, out double longitude)
    {
        var parts = input.Replace(',', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries);
        latitude = longitude = 0;
        return parts.Length >= 2 && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out latitude) && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out longitude) && latitude is >= -90 and <= 90 && longitude is >= -180 and <= 180;
    }

    private static double Haversine(double lat1, double lon1, double lat2, double lon2)
    {
        const double radius = 6371; var dLat = Radians(lat2 - lat1); var dLon = Radians(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(Radians(lat1)) * Math.Cos(Radians(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return radius * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private static double Bearing(double lat1, double lon1, double lat2, double lon2)
    {
        var y = Math.Sin(Radians(lon2 - lon1)) * Math.Cos(Radians(lat2)); var x = Math.Cos(Radians(lat1)) * Math.Sin(Radians(lat2)) - Math.Sin(Radians(lat1)) * Math.Cos(Radians(lat2)) * Math.Cos(Radians(lon2 - lon1));
        return (Degrees(Math.Atan2(y, x)) + 360) % 360;
    }

    private static double Radians(double degrees) => degrees * Math.PI / 180;
    private static double Degrees(double radians) => radians * 180 / Math.PI;
    private static (string Command, string Remainder) Split(string input) { var p = input.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries); return p.Length == 0 ? ("", "") : (p[0].ToLowerInvariant(), p.ElementAtOrDefault(1) ?? ""); }
    private static CapabilityResult Ok(string message) => new(true, message, message);
    private static CapabilityResult Usage(string message) => new(true, null, message);
}
