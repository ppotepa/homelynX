# TorrentBot2 (Homelynx) — Handoff

**Data:** 2026-07-08  
**Cel sesji:** Implementacja architektury z `codereview.md` (7 faz)  
**Status ogólny:** ✅ Zakończone — 101 testów .NET + 26/26 E2E; build OK; legacy grep clean.

**Dokumenty powiązane:**
- [`codereview.md`](codereview.md) — żywy przewodnik architektury (status: implemented)
- Plan implementacji: `.grok/sessions/.../goal/plan.md` (lokalnie na maszynie developera)
- Dowody testów (ostatnia sesja): `/tmp/grok-goal-0f533e202846/implementer/torrent-search-display-tests.log`

---

## Podsumowanie projektu

TorrentBot2 to bot Telegram + CLI do zarządzania pobieraniami torrentów:
- **Jackett** — wyszukiwanie
- **qBittorrent** — pobieranie
- **Ollama** — planowanie NL (LLM)
- **DuckDB** — query na snapshotach (`query.execute`)

Główna ścieżka requestu:

```
Adapter (Telegram/CLI)
  → ConversationResponseHandler (parse callback/text)
  → InvocationPipeline / ConversationPipeline
  → Capability handlers (plugins)
  → ResponseArtifactBuilders + Presenters
```

---

## Co zostało zrobione (7 faz z codereview.md)

### Faza 1 — Capability Contracts ✅
- `CapabilityContract` + `ExpectedResponseShape`, `ContinuationRule`, `ResponseConstructionSpec`
- Rejestracja w `PluginRegistrationContext.RegisterCapability(contract, handler)`
- Pełne kontrakty w pluginach: Torrent, Downloads, Query, System, BotControl

### Faza 2 — Rekursywna konwersacja (N odpowiedzi) ✅
- `ConversationContext` rozszerzony o `PendingUserAction[]`
- `ConversationResponseHandler` (parse only) → `ConversationPipeline.ProcessUserResponseAsync` (resolve + execute)
- Callbacki: `pending:yes:{token}` / `pending:no:{token}`, `select:{index}` (1-based)
- Torrent search state w `TorrentSearchConversationState` + snapshot `torrent_search_results`
- **Usunięte legacy:** `PendingInvocationStore`, `ConfirmationCallbackHandler`, `TorrentSearchSessionStore`, `confirm:` callbacks

### Faza 3 — Response Construction ✅
- `ResponseArtifactBuilders` registry (`list`, `search_results`, `confirmation`, `download_started`, `text`)
- `ContractResponseConstructor` + `ResponseConstructionBehavior` w pipeline
- Presentery delegują do wspólnego formatowania

### Faza 4 — Unified Pipeline ✅
- `InvocationPipeline` + behaviors: `ToolKnowledge`, `ConversationState`, `ResponseConstruction`, `ConversationPending`, `PerTurnPrompt`
- `PipelineBootstrap.Create()` → `PipelineServices(Invocation, Conversation)`
- Query i user-response idą przez pipeline

### Faza 5 — Per-turn LLM prompty ✅
- `LlmSystemPromptBuilder`: sekcje kontraktów, pending actions, conversation state, response construction
- Usunięte `CRITICAL SEARCH/FOLLOW-UP` string rules z promptów
- `LlmPipeline` follow-up repair (heurystyki dla słabych modeli)

### Faza 6 — Event Queue ✅
- `QueuedEventBus` (Channels) zamiast `InMemoryBus`
- Eventy: `ToolCallEvent`, `AwaitUserResponseEvent`, `UserResponseReceivedEvent`, `ResponseConstructedEvent`, `ConversationStateChangedEvent`
- Dispose w `EngineHost.StopAsync`

### Faza 7 — Cleanup ✅ (w `src/`)
- Grep legacy w `src/`: **0 trafień** na `PendingInvocationStore`, `ConfirmationCallbackHandler`, `TorrentSearchSessionStore`, `InMemoryBus`, `confirm:`
- Usunięte pluginy: Surveillance, Coord-Input (Python services + C# integracje)
- Test endpoint zabezpieczony: `TORRENTBOT_ENABLE_TEST_ENDPOINT` + `TORRENTBOT_TEST_ENDPOINT_SECRET`

---

## Ostatnia sesja — poprawki NL selection (krytyczne dla UX)

Te zmiany domykają ścieżkę „wybierz drugi” bez callbacka:

| Obszar | Plik | Co zrobiono |
|--------|------|-------------|
| Parsing indeksów | `src/TorrentBot.Contracts/Conversation/IndexSelectionParsing.cs` | `wybierz drugi`, `/select 2`, ordinals, bare numbers (1-based) |
| Yes/No z tekstu | `src/TorrentBot.Contracts/Conversation/YesNoResponseParsing.cs` | `tak`/`nie`/`yes`/`no` |
| Rozwiązywanie pending | `ConversationContext.cs` | `index`+`text`, `yes_no`+`text`; buduje `parameters["index"]` jako int |
| Handler tekstu | `ConversationResponseHandler.cs` | Parsuje index/yes_no; `NotHandled` dla nieparsowalnego tekstu → LLM path |
| LLM repair | `LlmPipeline.cs` | `TryParseDisplayIndex` zamiast hardcoded `index=1` |
| Rejestracja pending | `ConversationPendingRegistrar.cs` | `Continuation: null` (reguła już „zużyta” przy tworzeniu pending) |
| Usunięte | `ConversationContext.ApplyContinuation` | Nie duplikuje pending przy resolve (bug: 2 pending po select) |

### Torrent search display (1-based wszędzie user-facing)
- `TorrentSearchDisplay` — kanoniczna projekcja wyników
- `TorrentSearchPromptFormatting` — linie dla LLM (`[1] Title`, nie `[0]`)
- `TorrentSearchIndex` / `TrySelectGlobalIndex` — display index → global index
- Telegram buttons: `select:{index}` (1-based)

### Testy dodane/zmienione
- `IndexSelectionParsingTests`
- `ConversationPipelineTests.Integrated_nl_wybierz_drugi_resolves_pending_and_selects_second_torrent`
- `ConversationPipelineTests.ResolveText_with_index_pending_returns_NotHandled_for_unrelated_text`
- `TorrentSearchDisplayContractTests.Llm_follow_up_repair_maps_wybierz_drugi_to_index_2`
- `ArchitectureContractTests` — zaktualizowany pod nowy model pending (bez `NewPendingActions` przy resolve)

---

## Stan testów (2026-07-08)

```bash
dotnet build src/TorrentBot2.sln          # OK
dotnet test src/TorrentBot.Engine.Tests   # 101 passed, 1 skipped, 0 failed
dotnet run --project src/TorrentBot.Adapters.Cli -- capability call system.health --json  # OK
```

Skipped: `Surveillance_http_client_fetches_media_when_url_configured` (plugin usunięty — oczekiwane).

**E2E (`e2e-tests/`):** ✅ 26/26 (100%) po naprawie `common.sh` (ścieżka `config.env`) i opcjonalnego `TEST_ENDPOINT_SECRET`.

---

## Kluczowe pliki (mapa dla następnej osoby)

| Obszar | Ścieżki |
|--------|---------|
| Kontrakty | `src/TorrentBot.Contracts/Capabilities/CapabilityContract.cs` |
| Konwersacja | `src/TorrentBot.Contracts/Context/ConversationContext.cs` |
| Parsing NL | `src/TorrentBot.Contracts/Conversation/IndexSelectionParsing.cs`, `YesNoResponseParsing.cs` |
| Pipeline | `src/TorrentBot.Engine/Pipeline/InvocationPipeline.cs`, `PipelineBootstrap.cs` |
| User response | `src/TorrentBot.Engine/Conversation/ConversationResponseHandler.cs`, `ConversationPipeline.cs`, `ConversationPendingRegistrar.cs` |
| Event bus | `src/TorrentBot.Engine/Bus/QueuedEventBus.cs` |
| LLM | `src/TorrentBot.Llm/LlmSystemPromptBuilder.cs`, `LlmPipeline.cs` |
| Torrent search | `src/TorrentBot.Plugins.Torrent/TorrentSearchDisplay.cs`, `TorrentSearchConversationState.cs` |
| Telegram | `src/TorrentBot.Adapters.Telegram/TelegramBotHost.cs` |
| Response build | `src/TorrentBot.Engine/Pipeline/ResponseArtifacts/ResponseArtifactBuilders.cs` |
| Testy | `src/TorrentBot.Engine.Tests/Unit/ConversationPipelineTests.cs`, `TorrentSearchDisplayContractTests.cs` |

---

## Co trzeba jeszcze zrobić (priorytety)

### P0 — Przed merge / PR
1. ~~Commit + review~~ — do zrobienia przez właściciela repo (duży diff).
2. ~~E2E~~ — ✅ 26/26
3. ~~Dokumentacja~~ — `ARCHITECTURE.md`, `README.md` zaktualizowane
4. ~~`.env.example`~~ — ma `TORRENTBOT_ENABLE_TEST_ENDPOINT`, `TORRENTBOT_TEST_ENDPOINT_SECRET`

### P1 — Jakość / UX
1. **Test E2E NL path:** search → pending → tekst `wybierz drugi` przez Telegram test endpoint (obecnie pokryte testem jednostkowym przez pipeline, nie pełny host E2E).
2. **LLM planning** — słabe modele (qwen3:0.6b) nadal mogą zwracać puste plany; `LlmPipeline` ma heurystyki repair, ale to nie zastępuje lepszego modelu.
3. **Shared state CLI vs Bot** — osobne instancje `EngineHost`; confirmation/pending działa per adapter (patrz stary HANDOFF — nadal aktualne).

### P2 — Opcjonalne usprawnienia
1. **Outbox/persistence dla event queue** — `QueuedEventBus` jest in-memory; codereview wspomina outbox z DB — nie zaimplementowane.
2. **`ConversationPipeline.NewPendingActions`** — kod obsługi zostawiony, ale `ApplyContinuation` usunięte; można wyczyścić martwy kod.
3. **Ponowna ocena modelu LLM** (gemma:1b itd.) — osobny wątek od architektury.

---

## Jak zweryfikować (szybki checklist)

```bash
# Build + testy
dotnet build src/TorrentBot2.sln
dotnet test src/TorrentBot.Engine.Tests/TorrentBot.Engine.Tests.csproj

# Testy konwersacji / NL select
dotnet test src/TorrentBot.Engine.Tests --filter "ConversationPipeline|IndexSelection|TorrentSearchDisplay"

# Legacy grep (musi być pusto w src/, poza codereview.md jako dokumentacja historyczna)
rg "PendingInvocationStore|ConfirmationCallbackHandler|TorrentSearchSessionStore|InMemoryBus" src/ --glob '!*.md'

# CLI health
dotnet run --project src/TorrentBot.Adapters.Cli -- capability call system.health --json

# E2E (wymaga działającego stacka + secret)
cd e2e-tests && bash run-tests.sh
```

### Test endpoint (E2E / Telegram harness)

```bash
# Wymaga w .env:
# TORRENTBOT_ENABLE_TEST_ENDPOINT=true
# TORRENTBOT_TEST_ENDPOINT_SECRET=<secret>

curl -X POST http://localhost:5000/test/inject-update \
  -H "Content-Type: application/json" \
  -H "X-TorrentBot-Test-Secret: $TORRENTBOT_TEST_ENDPOINT_SECRET" \
  -d '{"update_id":1,"message":{"message_id":1,"chat":{"id":123},"from":{"id":1},"text":"/download_search ubuntu"}}'
```

---

## Znane pułapki / semantyka

1. **Indeksy 1-based** — display, `/select N`, przyciski Telegram, LLM prompt. Handler konwertuje przez `TorrentSearchDisplay.TrySelectGlobalIndex`.
2. **Pending rejestrowane tylko przez pipeline** — `ConversationPendingBehavior` po `InvocationPipeline.RunAsync`. `Engine.SubmitAsync` bez behaviors **nie** doda pending.
3. **Tekst przy index-pending** — parsowany tylko gdy wygląda jak wybór (`IndexSelectionParsing`). Inny tekst → `NotHandled` → LLM (poprawne zachowanie).
4. **Po `torrent.select_result`** — zostaje pending `yes_no` (confirmation download); to oczekiwane, nie bug.
5. **`TorrentSearchResult` ctor** — pierwszy argument to `Title`, nie id. Testy muszą używać: `new TorrentSearchResult("Beta ISO", "magnet:...", ...)`.

---

## Usunięte komponenty (nie przywracać bez decyzji produktowej)

- `services/surveillance/` (Python)
- `services/coord-input/` (Python)
- `src/TorrentBot.Plugins.Surveillance/`
- `PendingInvocationStore`, `ConfirmationCallbackHandler`, `TorrentSearchSessionStore`, `InMemoryBus`

---

## Dla następnego agenta / developera

1. Przeczytaj `codereview.md` (sekcja 1 = aktualny stan kodu).
2. Uruchom checklist weryfikacji powyżej.
3. Jeśli coś pada — zacznij od `ConversationPipelineTests` i `FullStackIntegrationTests`.
4. Przed produkcją: E2E + ręczny test Telegram (search → select button → select NL text).
5. Nie claimuj „done” bez E2E — goal harness chciał pełny verification plan z logami w scratch dir.

**Pytania do właściciela repo:**
- Czy surveillance/coord-input mają zostać usunięte na stałe?
- Czy merge jednym PR czy stack (contracts → conversation → cleanup)?
- Czy priorytetem jest E2E zielone czy poprawa LLM?

---

## Historia (skrót)

| Data | Co |
|------|-----|
| 2026-07-07 | Bugfixy ACL/provider, E2E dual-path, verbosity:full, test endpoint HTTP |
| 2026-07-08 | Pełna implementacja codereview.md (7 faz), NL index parsing, 101 testów OK |

---

*Koniec handoffu. Powodzenia.*