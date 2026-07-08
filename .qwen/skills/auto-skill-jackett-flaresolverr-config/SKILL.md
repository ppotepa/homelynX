---
name: jackett-flaresolverr-config
description: Debugging Jackett torrent search with FlareSolverr, indexer selection, and timeout configuration
source: auto-skill
extracted_at: '2026-07-06T19:30:00.000Z'
---

# Jackett + FlareSolverr Configuration & Debugging

When Jackett torrent search returns 0 results or times out, follow this debugging procedure.

## Common Issues

### 1. FlareSolverr Not Configured

**Symptom:** Jackett logs show:
```
Error Jackett.Common.IndexerException: Exception (1337x): Challenge detected but FlareSolverr is not configured
```

**Fix:** Set FlareSolverr URL in Jackett's ServerConfig.json:
```bash
docker exec jackett sh -c "jq '.FlareSolverrUrl = \"http://flaresolverr:8191\"' /config/Jackett/ServerConfig.json > /tmp/config.json && mv /tmp/config.json /config/Jackett/ServerConfig.json"
docker restart jackett
```

**Verify:** Check Jackett logs after restart:
```bash
docker logs jackett --tail 10 2>&1 | grep FlareSolverr
# Should show: Info Using FlareSolverr: http://flaresolverr:8191
```

### 2. Indexer Timeout (Searching All Indexers)

**Symptom:** Search takes 5+ minutes or times out. Jackett logs show:
```
Manual search in 1337x, ExtraTorrent.st, EZTV, ... for ubuntu => Found 709 releases [326518ms]
```

**Root Cause:** Searching `all` indexers includes slow/dead trackers that timeout.

**Fix:** Use selective indexers via `JACKETT_SEARCH_INDEXERS` env var:
```bash
# .env
JACKETT_SEARCH_INDEXERS=thepiratebay,1337x,yts,limetorrents,nyaasi
```

**Fast indexers (from logs):**
- `thepiratebay` - 5ms (cached), 100 results
- `1337x` - fast, reliable
- `yts` - fast, movie-focused
- `limetorrents` - moderate speed
- `nyaasi` - fast, anime-focused

**Slow/dead indexers to avoid:**
- `torrentdownload` - Error 522 (site down)
- `magnetz` - disabled
- `torrentgalaxyclone` - disabled

### 3. Jackett API Returns Empty Response

**Symptom:** `curl http://jackett:9117/api/v2.0/indexers/...` returns empty.

**Debug checklist:**
1. Check Jackett is running: `docker ps | grep jackett`
2. Check Jackett logs: `docker logs jackett --tail 50`
3. Test single indexer: `curl -s "http://jackett:9117/api/v2.0/indexers/thepiratebay/results?apikey=KEY&Query=ubuntu" | jq '.Results | length'`
4. If single indexer works but multiple don't → timeout issue, reduce indexer count
5. If even single indexer returns empty → Jackett may be overloaded, restart it

### 4. .env Values with Spaces

**Symptom:** `source .env` fails with errors like:
```
.env: line 107: Go: command not found
```

**Root Cause:** Unquoted values with spaces in `.env`:
```bash
# BAD
TTS_PLAYBACK_DEVICE=JBL Go 4

# GOOD
TTS_PLAYBACK_DEVICE="JBL Go 4"
```

**Fix for test scripts:** Don't `source .env` — parse specific variables:
```bash
ENV_FILE="/path/to/.env"
export TELEGRAM_BOT_TOKEN=$(grep '^TELEGRAM_BOT_TOKEN=' "$ENV_FILE" | cut -d'=' -f2-)
QBIT_HOST=$(grep '^QBIT_HOST=' "$ENV_FILE" | cut -d'=' -f2-)
QBIT_PORT=$(grep '^QBIT_PORT=' "$ENV_FILE" | cut -d'=' -f2-)
export QBITTORRENT_URL="http://${QBIT_HOST}:${QBIT_PORT}"
```

### 5. HttpClient Timeout in .NET Client

**Symptom:** Jackett search returns 0 results, no errors logged.

**Fix:** Set explicit timeout on HttpClient:
```csharp
IJackettClient jackett = new JackettClient(
    new HttpClient { Timeout = TimeSpan.FromSeconds(30) },
    jackettUrl, jackettKey, jackettIndexers);
```

### 6. Configurable Indexer List

**Pattern:** Pass indexer list from env to client:
```csharp
// JackettClient constructor
public JackettClient(HttpClient httpClient, string baseUrl, string? apiKey = null,
    string? indexers = null, ILogger<JackettClient>? logger = null)
{
    _indexers = indexers ?? "all";
}

// URL construction
var url = $"{_baseUrl}/api/v2.0/indexers/{_indexers}/results?apikey={_apiKey}&Query={encodedQuery}";
```

```csharp
// Bootstrap
var jackettIndexers = Environment.GetEnvironmentVariable("JACKETT_SEARCH_INDEXERS");
IJackettClient jackett = new JackettClient(
    new HttpClient { Timeout = TimeSpan.FromSeconds(30) },
    jackettUrl, jackettKey, jackettIndexers);
```

```yaml
# docker-compose.yaml
environment:
  JACKETT_SEARCH_INDEXERS: "${JACKETT_SEARCH_INDEXERS:-all}"
```

### 7. 🚨 CRITICAL: Multi-Indexer URL Returns 500

**Symptom:** Jackett search returns 0 results, logs show `InternalServerError`.

**Root Cause:** Jackett's API does NOT support comma-separated indexer lists in the URL path. When you request `/api/v2.0/indexers/thepiratebay,1337x,limetorrents/results`, Jackett returns HTTP 500.

**Test to verify:**
```bash
# ❌ FAILS - returns 500
curl "http://localhost:9117/api/v2.0/indexers/thepiratebay,1337x/results?apikey=KEY&Query=ubuntu"

# ✅ WORKS - single indexer
curl "http://localhost:9117/api/v2.0/indexers/thepiratebay/results?apikey=KEY&Query=ubuntu"
```

**Fix:** Iterate over each indexer individually and combine results:

```csharp
public async Task<IReadOnlyList<TorrentSearchResult>> SearchAsync(string query, CancellationToken ct = default)
{
    var allResults = new List<TorrentSearchResult>();
    var indexers = _indexers.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    foreach (var indexer in indexers)
    {
        try
        {
            var url = $"{_baseUrl}/api/v2.0/indexers/{indexer}/results?apikey={_apiKey}&Query={Uri.EscapeDataString(query)}";
            
            using var response = await _httpClient.GetAsync(url, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Search failed for {Indexer}: {StatusCode}", indexer, response.StatusCode);
                continue; // Skip failed indexer, continue with others
            }

            // Parse results and add to allResults
            var results = await ParseResultsAsync(response, ct).ConfigureAwait(false);
            allResults.AddRange(results);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error searching indexer {Indexer}", indexer);
            continue; // Don't let one indexer failure break the whole search
        }
    }

    return allResults;
}
```

**Key points:**
- Split the indexer list by comma
- Make one HTTP request per indexer
- Use `continue` on failure to skip bad indexers
- Combine all results into a single list
- Log which indexers succeeded/failed for debugging

**Working indexer list (tested):**
```bash
JACKETT_SEARCH_INDEXERS=thepiratebay,1337x,limetorrents,eztv
```

Results per indexer for "ubuntu":
- thepiratebay: 100 results
- 1337x: 80 results
- limetorrents: 40 results
- eztv: 1 result
- **Total: 221 results**

## Debugging Flowchart

```
Search returns 0 results?
├── Check Jackett logs for errors
│   ├── "Challenge detected but FlareSolverr not configured" → Configure FlareSolverr
│   ├── "Request failed (Error 522)" → Tracker is down, remove from indexers list
│   └── No errors → Continue debugging
├── Test single indexer via curl
│   ├── Single indexer works → Timeout issue, reduce indexer count
│   └── Single indexer fails → Jackett overloaded, restart it
├── Check HttpClient timeout
│   └── No timeout set → Add 30s timeout
└── Check indexer list
    └── Using "all" → Switch to JACKETT_SEARCH_INDEXERS with fast indexers only
```

## When to Use

- Jackett torrent search returns 0 results
- Search is slow (>30 seconds)
- FlareSolverr integration issues
- .env parsing errors in bash scripts
- HttpClient timeout issues in .NET clients
