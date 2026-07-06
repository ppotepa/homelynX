# What the Media Server Bot Can Do (User-Facing Functionality)

This document describes the **full intended behavior** of the system from the user's point of view. It is the "what" — independent of the current implementation status.

Use this together with [ARCHITECTURE.md](ARCHITECTURE.md) (the "how").

## Primary Interfaces

1. **Telegram Bot** (multi-user, ACL protected)
2. **CLI** — especially powerful for power users and developers (supports both general `download.*` and provider-specific commands)

Both interfaces support the same capabilities. The Download Manager presents a unified interface regardless of the underlying provider.

---

## Core User Workflows

### 1. Download Management (General + Multiple Providers)

The bot supports downloading from different sources through a unified **Download Manager**.

**Deterministic examples**
```
/search ubuntu 24.04
/download_url https://example.com/file.iso
/list downloads
/cancel 42
```

**Natural language**
- "znajdź ubuntu 24.04"
- "pobierz najnowszego debian netinstall"
- "pobierz ten plik https://..."
- "szukaj linux mint i pobierz pierwszy wynik"
- "pobierz ten film z linku"

**How it works**

- Search can be provider-specific (mainly torrents via Jackett today).
- Starting a download can target different backends:
  - **TorrentDownloader** (Jackett + qBittorrent) — the classic path with rich metadata (seeds, size, hash).
  - **UrlDownloader** — direct HTTP links, magnets, or tools like yt-dlp.
- All downloads (regardless of provider) appear in a single unified list and are queryable.
- After completion the Download Manager can automatically route files into the correct media library location.
- Progress, completion and failures are visible via `query.execute` and live updates.

This design makes it easy to add new download methods later (Usenet, etc.) without changing the user-facing commands or the LLM planning logic.

### 2. Download Monitoring & Control

Because all downloads (torrent, URL, future providers) feed the same `downloads` repository, monitoring works uniformly.

Examples:
- "jakie mam aktywne pobrania?"
- "pokaż pobrania większe niż 10GB"
- "wstrzymaj pobieranie X"
- "ile zostało do końca filmu Y?"
- "pokaż pobrania z URL-i"
- "które torrenty się seedują?"

Uses the query system (`query.execute` on the `downloads` source) heavily. The LLM can combine download state with media state in one question.

### 3. Media Library Awareness

- "czy mam już ten film?"
- "znajdź największe pliki w bibliotece"
- "pokaż co się dodało w tym tygodniu"
- "ile miejsca zajmują seriale?"

Backed by media snapshot sources + the safe query DSL.

### 4. Natural Language Questions About State (the killer feature)

The LLM + `query.execute` combination lets users ask almost anything that can be expressed over the aggregated repositories:

- Status of the system
- Downloads
- Completed vs active
- Large / old / stuck items
- Job history
- (later) surveillance events, coordinates, etc.

Important guarantee: the LLM can only request things through registered capabilities and the safe query DSL.

### 5. System & Diagnostics

- `/health`, `/status`, `/ping`
- "czy wszystko działa?"
- "jakie mam dostępne komendy?"
- "pokaż co jest w tej chwili w kolejce"

### 6. Confirmations & Safety

Risky actions (big downloads, deletions, admin commands) require explicit confirmation:
- Bot asks "Are you sure?"
- User replies with button or "tak" / confirmation token
- Only then the action proceeds

This applies to both slash and LLM-planned paths.

### 7. Verbosity / Live Feedback

User can configure per-chat:
- `verbosity off`
- `verbosity low/medium/full`

The bot edits one message in place showing stages: parse → plan → execute → respond.

Extremely useful for understanding what the LLM decided to do.

### 8. Audit

Everything (especially LLM plans and capability executions) is audited to a persistent store (used by the Portal in the old system).

---

## Capability Categories (Planned)

| Category     | Examples                                                      | Typical Risk    |
|--------------|---------------------------------------------------------------|-----------------|
| system       | health, status, capabilities, config, find_large_files        | Safe / Admin    |
| query        | query.execute                                                 | Safe            |
| downloads    | search, start, start_url, list, cancel, control               | Safe / Confirm  |
| torrent      | search (provider-specific), list (torrent view)               | Safe / Confirm  |
| media        | list, find, route, organize                                   | Safe            |
| tts          | say, generate                                                 | Safe            |
| jobs         | list_jobs, cancel_job                                         | Safe / Admin    |
| surveillance | (later)                                                       | Admin           |

---

## LLM Planning in Practice

When you send plain text:

1. The planner receives:
   - Your text
   - Current user permissions
   - List of available capabilities (filtered by ACL)
   - Compact summaries of all queryable repositories

2. It produces a JSON plan, e.g.:
   ```json
   {
     "intent": "search and start download",
     "steps": [
       { "capability": "torrent.search", "params": { "query": "ubuntu 24.04" } },
       { "capability": "torrent.download_candidate", "params": { "index": 0 } }
     ]
   }
   ```

3. The executor validates every step.
4. Steps are executed (some may spawn jobs).
5. Final response is generated.

The user sees the plan (in full verbosity) and can understand what happened.

---

## CLI as a First-Class Experience

The CLI is not an afterthought. It must support:

- Direct calls: `torrentbot capability torrent.search --query "debian"`
- Planning: `torrentbot agent plan "pobierz najnowszego fedora"`
- Dry-run execution: `torrentbot agent run "..." --dry-run`
- Query: `torrentbot query downloads --where 'status=downloading'`
- LLM raw access for debugging prompts
- JSON output for scripting and tests

This makes development and testing of the Engine extremely fast without needing Telegram.

---

## What Is Explicitly Out of Scope for the Bot Itself

- Running qBittorrent, Jackett, Jellyfin, Ollama (they are separate services)
- The actual media organizer script (runs outside)
- Surveillance recording
- Android coordinate app
- Web portal UI (it consumes audit + status from the bot/services)

The bot is the **orchestration and user interface layer**.

---

## Success Criteria (for the rewrite)

- Slash commands behave exactly as before (or better)
- Natural language works reliably because the planner gets clean manifests + repo summaries
- Adding a new feature is "write a plugin with a few capabilities + optional snapshot source"
- You can test almost everything through the CLI + dry-run
- The query system remains one of the strongest parts of the product

This document will be updated as new capabilities are designed and implemented.
