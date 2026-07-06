# LLM — planowanie, problemy i rozwiązanie

Ten dokument opisuje **jak TorrentBot używa LLM**, **co dokładnie było nie tak** (na podstawie realnych logów z Telegrama), **dlaczego** tak się działo i **jak się tego pozbyć** — zarówno architektonicznie, jak i operacyjnie.

Powiązane dokumenty:
- [PLUGINS_AND_CAPABILITIES.md](./PLUGINS_AND_CAPABILITIES.md) — rejestr capabilities (źródło prawdy)
- [QUERY.md](./QUERY.md) — Query DSL (`query.execute`)
- [ENGINE.md](./ENGINE.md) — silnik i pipeline

---

## 1. Po co w ogóle jest LLM?

TorrentBot ma dwa tryby wejścia:

| Wejście | Przykład | Kto planuje |
|---------|----------|-------------|
| **Explicit** (jawna komenda) | `/search acdc`, `/jobs`, `/help` | `DeterministicPlanner` — mapowanie `/slash` → capability z rejestru |
| **Natural language** (zwykły tekst) | `list all commands`, `pokaż aktywne pobrania` | **LLM planner** — model wybiera capability i parametry z manifestu |

**Slash komendy nie powinny przechodzić przez LLM** — użytkownik wybrał akcję wprost.

**Zwykły tekst powinien przechodzić przez LLM** — model interpretuje intencję i buduje plan wykonania (JSON) z dozwolonych capabilities.

---

## 2. Objawy z produkcji (co użytkownik widział)

Poniższe logi z Telegrama pokazują **symptomy**, nie jedną przyczynę:

```
Bravurak: /jobs
MEDIA: Capability was not resolved.

Bravurak: /list
MEDIA: Capability was not resolved.

Bravurak: /help
MEDIA: Listed 56 available command(s).        ← sukces, ale bez formatowania listy

Bravurak: list all commands
MEDIA: Capability 'Query sources' was not found. ← halucynacja LLM

Bravurak: test
MEDIA: Capability was not resolved.

Bravurak: /search acdc
MEDIA: Found 377 torrent result(s)              ← brak paginacji / przycisków (stary presenter)
MEDIA: Confirmation required.                 ← mylące, jeśli search nie wymaga confirm
```

### Mapowanie symptom → przyczyna

| Symptom | Prawdopodobna przyczyna |
|---------|-------------------------|
| `Capability was not resolved` na `/jobs`, `/list` | Komenda spoza mapy Telegram / brak `@bot` strip / stary deploy bez rejestru |
| `Listed 56 available command(s)` bez listy | Handler zwraca dane w `Data`, brak presentera help |
| `Capability 'Query sources' was not found` | **Słaby prompt LLM** — model wziął etykietę sekcji za nazwę capability |
| `Found 377 torrent result(s)` bez stron | Artifact search nie zbudowany → tylko `Message`, nie `SearchResultsArtifact` |
| `test` → not resolved | Brak LLM lub pusty plan — oczekiwane bez Ollamy |
| `Confirmation required` po search | Pending confirmation z poprzedniej akcji lub zły plan LLM |

---

## 3. Główny problem architektoniczny

### Problem A: Dwa konkurencyjne sposoby planowania NL

Historycznie NL był obsługiwany na **trzy sposoby naraz**:

1. **Twarda mapa** w `TelegramInvocationAdapter` (częściowa, ~22 komendy)
2. **`ContainsAny` / `RichStubLlmPlanner`** — sztywne dopasowanie fraz bez modelu
3. **Ollama** z ubogim promptem (`name:description` + linia `Query sources: ...`)

To prowadziło do:
- niespójności między Telegram a rejestrem capabilities
- fałszywego wrażenia „inteligencji” w trybie stub (bez Ollamy)
- halucynacji nazw capabilities w trybie Ollama

**Zasada docelowa:** jeden pipeline, jedno źródło prawdy (rejestr), **LLM jako planner tylko dla NL**.

### Problem B: Prompt nie był system promptem

Stary prompt (`OllamaLlmPlanner`) wyglądał mniej więcej tak:

```
Capability manifest:
system.help:Show available commands
torrent.search:Search torrent indexers
...
Query sources: downloads, jobs, media    ← TO NIE JEST CAPABILITY!
User request: list all commands
```

Model często zwracał:

```json
{ "steps": [{ "capability": "Query sources" }] }
```

Bo **„Query sources”** wyglądało jak nagłówek sekcji z listą narzędzi — klasyczna halucynacja struktury promptu.

### Problem C: Brak Ollamy = udawany NL

Gdy `OLLAMA_HOST` nie był ustawiony, `RichStubLlmPlanner` próbował zgadywać intencję przez `ContainsAny("search", "pobrania", ...)`. To:

- nie skaluje się na 56 capabilities
- nie obsługuje polskiego / wariantów językowych
- maskuje brak konfiguracji LLM

**Docelowo:** bez Ollamy NL zwraca jasny błąd (`UnconfiguredLlmPlanner`), nie udaje planowania.

### Problem D: Telegram ≠ rejestr

Telegram miał twardą mapę komend zamiast pełnego rejestru. Komendy jak `/ping`, `/diag`, `/job_cancel` działały tylko jeśli trafiły do mapy lub fallbacku.

Dodatkowo `/jobs@MEDIABOT` nie pasowało do `/jobs` — brak strip `@botname`.

---

## 4. Docelowa architektura (jak powinno działać)

```
┌─────────────┐     explicit?      ┌──────────────────────┐
│  Telegram   │ ─── /slash ───────►│ DeterministicPlanner │
│  CLI        │                    │ ResolveSlashCommand  │
└─────────────┘                    └──────────┬───────────┘
       │                                      │
       │ plain text                           │
       ▼                                      ▼
┌──────────────────────┐              ┌───────────────┐
│   LlmPlannerAdapter  │              │  EngineHost   │
│                      │              │ SubmitAsync   │
│  1. ACL filter caps  │              │ Execute cap.  │
│  2. Build prompt     │              └───────────────┘
│  3. Ollama plan      │
│  4. LlmPlanParser    │
│  5. StubLlmExecutor  │
└──────────────────────┘
```

### Źródło prawdy

Wszystkie capabilities rejestrują się w pluginach (`RegisterCapability` / metadane). Z tego buduje się:

- `/help`, `/capabilities` (refleksja + ACL)
- manifest w promptcie LLM
- mapowanie slash komend (`CapabilityRegistry.ResolveCommandFuzzy`)

LLM **nie ma** własnej listy komend — dostaje **snapshot** tego, co użytkownik może wykonać (po ACL).

---

## 5. Jak działa pipeline LLM (stan obecny)

### 5.1 Komponenty

| Plik | Rola |
|------|------|
| `src/TorrentBot.Llm/LlmSystemPromptBuilder.cs` | Buduje pełny system prompt z manifestu |
| `src/TorrentBot.Llm/OllamaLlmPlanner.cs` | Wysyła prompt do Ollamy |
| `src/TorrentBot.Llm/LlmPlanParser.cs` | Parsuje JSON; **odrzuca** nieznane capability |
| `src/TorrentBot.Llm/StubLlmExecutor.cs` | Waliduje plan vs ACL / rejestr |
| `src/TorrentBot.Llm/UnconfiguredLlmPlanner.cs` | Gdy brak Ollamy — pusty plan + komunikat |
| `src/TorrentBot.Engine/Pipeline/LlmPlannerAdapter.cs` | Adapter NL → `ExecutionPlan` |
| `src/TorrentBot.Engine/Pipeline/InvocationPipeline.cs` | Wybór plannera: explicit vs LLM |
| `src/TorrentBot.Bootstrap/EngineBootstrap.cs` | Tworzy `LlmPipeline` jeśli jest URL Ollamy |

### 5.2 Co trafia do promptu

`LlmSystemPromptBuilder` składa:

1. **Scope** (`media`, `surveillance`, …)
2. **Capability manifest** — dla każdej capability:
   - `name`, `command`, `permission`, `risk`, `readonly`
   - `description`, `llm_usage`, `intent_hints`
3. **Query DSL** — jak używać `query.execute`
4. **Query source manifests** — pola, operatory, przykłady JSON
5. **Planning rules** — m.in. „`steps[].capability` musi być dokładną nazwą z manifestu”
6. **User request**
7. **Format odpowiedzi** — JSON bez markdown

### 5.3 Format planu (kontrakt LLM → engine)

```json
{
  "intent": "list available commands",
  "steps": [
    {
      "capability": "system.help",
      "parameters": {},
      "why": "User asked for command list",
      "condition": null,
      "save_as": null
    }
  ],
  "confidence": 0.9
}
```

**Ważne:**
- `capability` = identyfikator z rejestru (`system.help`), **nie** `/help`, **nie** `Query sources`
- `parameters` = słownik zgodny z handlerem (np. `torrent.search` → `{ "query": "acdc" }`)
- multi-step: `save_as` + `condition` (patrz `PlanStepConditionEvaluator`)

### 5.4 Walidacja po stronie silnika

Nawet jeśli LLM zhallucynuje:

1. `LlmPlanParser` — odrzuca kroki z nieznanym `capability`
2. `StubLlmExecutor` — druga linia obrony przed wykonaniem
3. ACL — `Allows(user, metadata)` przed `ExecuteAsync`

Przykład: plan z `Query sources` + `system.help` → zostaje tylko `system.help`.

---

## 6. Query DSL a LLM

Pytania o **stan** (nie akcje) powinny iść przez `query.execute`, nie przez wymyślone capability.

```
Użytkownik: "are there any active downloads?"
Plan LLM:
  capability: query.execute
  parameters:
    source: downloads
    where:
      - field: status
        op: =
        value: downloading
```

Źródła i schematy pochodzą z `ISnapshotSource.GetManifest()` — te same metadane trafiają do promptu w sekcji **Registered query sources**.

Szczegóły DSL: [QUERY.md](./QUERY.md).

---

## 7. Konfiguracja (jak uruchomić LLM w produkcji)

### Wymagane zmienne

| Zmienna | Opis |
|---------|------|
| `OLLAMA_HOST` lub `TORRENTBOT_OLLAMA_URL` | URL API Ollamy, np. `http://llm:11434` |
| `LLM_MODEL` lub `TORRENTBOT_OLLAMA_PLANNER_MODEL` | Model planera, np. `qwen3:0.6b`, `llama3` |

### Opcjonalne (multi-model)

| Zmienna | Domyślnie |
|---------|-----------|
| `TORRENTBOT_OLLAMA_PLANNER_MODEL` | `LLM_MODEL` |
| `TORRENTBOT_OLLAMA_EXECUTOR_MODEL` | planner model |
| `TORRENTBOT_OLLAMA_RESPONDER_MODEL` | planner model |

### Bootstrap w kodzie

```csharp
// EngineBootstrap.CreateLlmPipeline()
if (ollamaUrl is set)
    → OllamaLlmPlanner + OllamaLlmExecutor + OllamaLlmResponder
else
    → UnconfiguredLlmPlanner  // NL nie działa
```

### Weryfikacja

```bash
# Czy Ollama odpowiada
curl http://llm:11434/api/tags

# Plan NL przez CLI (wymaga działającej Ollamy w env)
dotnet run --project src/TorrentBot.Adapters.Cli -- \
  agent plan "list all commands" --json --user admin

# Jawna capability (bez LLM)
dotnet run --project src/TorrentBot.Adapters.Cli -- \
  capability call system.help --json --user admin
```

---

## 8. Jak pozbyć się konkretnych problemów

### 8.1 `Capability 'Query sources' was not found`

**Przyczyna:** stary prompt z etykietą `Query sources:` w manifestcie.

**Rozwiązanie (zaimplementowane):**
- `LlmSystemPromptBuilder` — osobne sekcje, explicit rule: „never a label, title, or query source name”
- `LlmPlanParser` — filtr unknown capabilities

**Weryfikacja:** `list all commands` → plan ze `system.help` lub `system.capabilities`.

### 8.2 `Capability was not resolved` na slash komendach

**Przyczyny:**
- komenda nie w rejestrze (`/list` — brak aliasu)
- `@BotName` w komendzie (`/jobs@MEDIA`)
- stary kontener bez rebuildu

**Rozwiązanie (zaimplementowane):**
- `TelegramInvocationAdapter` → `EngineHost.ResolveSlashCommand` (rejestr + fuzzy)
- aliasy: `/list`, `/commands` → `system.help`
- strip `@bot` w `CapabilityRegistry.NormalizeCommand`

**Weryfikacja:** `/jobs`, `/list`, `/jobs@TwojBot` w Telegramie.

### 8.3 NL nie działa (`test`, `pokaż pobrania`)

**Przyczyna:** brak `OLLAMA_HOST` → `UnconfiguredLlmPlanner` zwraca pusty plan.

**Rozwiązanie operacyjne:**
1. Ustaw `OLLAMA_HOST` / `LLM_MODEL` w `.env` / docker-compose
2. Rebuild `homelynx-bot`
3. Sprawdź logi — brak błędów HTTP do Ollamy

**To nie jest bug** — to jawna degradacja bez LLM.

### 8.4 `/help` zwraca tylko liczbę

**Przyczyna:** `SystemHelpHandler` zwraca `Message: "Listed 56..."` + `Data.capabilities[]`, ale Telegram pokazywał tylko message.

**Rozwiązanie (zaimplementowane):** `HelpPresenter` formatuje `Data.capabilities` na listę grupowaną po module.

### 8.5 Search bez paginacji

**Przyczyna:** `ArtifactAccumulator` nie budował `SearchResultsArtifact` (typ kolekcji `results`).

**Rozwiązanie (zaimplementowane):** iteracja po `System.Collections.IEnumerable` + `SearchResultsPresenter` dla Telegram.

### 8.6 Halucynacje i złe plany — ogólna strategia

| Warstwa | Co robi |
|---------|---------|
| Prompt | Pełny manifest, reguły, przykłady DSL |
| Parser | Whitelist capability names |
| Executor | Walidacja + dry-run |
| ACL | Odrzucenie niedozwolonych akcji |
| Confirm | `RiskLevel.Destructive` / `ConfirmationRequired` |

**Nie dodawać** nowych `ContainsAny` w produkcji — każda nowa intencja = lepszy manifest (`LlmUsage`, `IntentHints`) + lepszy model.

---

## 9. Czego NIE robić (antywzorce)

1. **`ContainsAny("search", "pobierz")` w plannerze** — nie skaluje się, omija LLM
2. **Twarda mapa 22 komend w Telegram** zamiast rejestru — dryf względem pluginów
3. **Mieszanie etykiet promptu z nazwami capability** — prowokuje halucynacje
4. **Udawanie NL w trybie stub** — ukrywa brak Ollamy
5. **Wysyłanie całego JSON capabilities bez ACL** — model planuje akcje, których user nie może wykonać

---

## 10. Rozszerzanie systemu (nowa capability / plugin)

Checklist dla developera:

1. Zarejestruj capability z metadanymi:
   - `Name`, `Command`, `Description`
   - `LlmUsage` — **kiedy LLM ma tego użyć**
   - `IntentHints` — synonimy intencji
   - `Risk`, `Permission`, `Scope`
2. Jeśli to źródło danych — dodaj `ISnapshotSource` z `QuerySourceMeta`
3. Nie dodawaj komendy do Telegram ręcznie — wystarczy `Command` w metadanych
4. Przetestuj:
   ```bash
   dotnet test src/TorrentBot.Engine.Tests --filter LlmSystemPromptBuilder
   dotnet run --project src/TorrentBot.Adapters.Cli -- capabilities list --json
   ```
5. Przetestuj NL (z Ollamą): `agent plan "<intencja>" --json`

---

## 11. Testy

| Test | Co sprawdza |
|------|-------------|
| `LlmSystemPromptBuilderTests` | Prompt zawiera capabilities, DSL, user request; nie ma `Query sources:` |
| `LlmPlanParserTests` | Odrzucanie `Query sources`, zostawienie `system.help` |
| `PipelineIntegrationTests` | Search → `SearchResultsArtifact` + Telegram format |
| `FixedPlanLlmPlanner` (test support) | Testy integracyjne bez prawdziwej Ollamy |

Testy produkcyjne NL **wymagają** Ollamy lub inject `ILlmPlanner` w testach — domyślny bootstrap bez URL nie planuje NL.

---

## 12. Roadmap (co jeszcze warto zrobić)

| Priorytet | Zadanie |
|-----------|---------|
| Wysoki | Log pełnego promptu / planu przy `verbosity full` w Telegram |
| Wysoki | Eksport manifestu do pliku (`HOMELYNX_CAPABILITIES_FILE`) — już jest `CapabilityManifestExporter` |
| Średni | Osobny profil promptu (`HOMELYNX_LLM_SYSTEM_PROMPT_PROFILE`) — compact vs full |
| Średni | Retry + repair loop: jeśli parser odrzuci plan, poproś model o korektę |
| Niski | Fine-tuned model pod planowanie capability JSON |
| Niski | Streaming odpowiedzi planera do `verbosity full` |

---

## 13. Szybka ściąga operacyjna

```text
Slash komenda nie działa?
  → /help, sprawdź rebuild, sprawdź @bot strip, capabilities list --json

NL nie działa?
  → curl OLLAMA_HOST/api/tags, sprawdź LLM_MODEL, logi homelynx-bot

NL zwraca dziwny capability?
  → to powinno być odfiltrowane; jeśli nie — zgłoś nazwę, dodaj LlmUsage do manifestu

Chcę żeby bot „sam wiedział” co umie?
  → to robi rejestr + prompt; upewnij się że Ollama działa i ACL nie jest pusty dla usera
```

---

## 14. Pliki — mapa kodu

```
src/TorrentBot.Llm/
  LlmSystemPromptBuilder.cs   # system prompt
  OllamaLlmPlanner.cs         # wywołanie Ollamy
  LlmPlanParser.cs            # parse + whitelist
  LlmPipeline.cs              # plan → validate → reply
  UnconfiguredLlmPlanner.cs   # brak Ollamy
  StubLlmExecutor.cs          # walidacja planu

src/TorrentBot.Engine/Pipeline/
  InvocationPipeline.cs       # explicit vs LLM
  LlmPlannerAdapter.cs        # NL entry
  DeterministicPlanner.cs     # slash commands

src/TorrentBot.Adapters.Telegram/
  TelegramInvocationAdapter.cs  # slash → ResolveSlashCommand
  Sdk/TelegramProductionAdapter.cs

src/TorrentBot.Bootstrap/
  EngineBootstrap.cs          # LlmPipeline factory
  PipelineBootstrap.cs
  CapabilityManifestExporter.cs
```

---

*Ostatnia aktualizacja: lipiec 2026 — po refaktorze NL na manifest-driven LLM prompt.*