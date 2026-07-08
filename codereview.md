# TorrentBot2 — Solid Architecture & Agentic Conversation Implementation Guide (żyjący dokument + instrukcje)

**Cel:** Spójny dokument instrukcji. 
- Dokładnie odzwierciedla stan kodu w repo (sprawdzone: CapabilityMetadata, IPluginRegistrationContext, LlmSystemPromptBuilder, InvocationPipeline, LlmPipeline, InMemoryBus, PendingInvocationStore, ConfirmationArtifact/CallbackHandler, QuerySpec+DuckDb, Artifacts/Presenters, EngineHost, TorrentSearchSessionStore, CapabilityContext).
- Zero zgrzytów z kodem.
- Forma **instrukcji "co gdzie zrobić"**.
- Pełny cleanup (use it or lose it — żadnej V2, żadnego legacy).
- Baza z poprzednich iteracji + wymagania: pipeline dla wszystkiego, wewnętrzna kolejka + eventy, per-turn meta prompty, **dokładna wiedza bota o narzędziach/capabilities**, **konstruowanie odpowiedzi**, **oczekiwanie na N rekursywnych odpowiedzi użytkownika** i co z nimi zrobić.

**Zasady (zawsze):**
- Use it or lose it + pełny cleanup.
- Pipeline dla wszystkiego (query, capability, odpowiedź użytkownika, konstrukcja odpowiedzi).
- Eventy + queue jako podstawa.
- Dokładna wiedza przez kontrakty (nie stringi).
- Rekursywna konwersacja (N odpowiedzi, gałęzie).
- Per-turn meta prompty z kontraktami + stanem.
- Spójność: instrukcje odnoszą się do rzeczywistych nazw z repo.

**Data:** 2026-07-08  
**Status:** Implemented (2026-07-08) — wszystkie 7 faz wdrożone. Torrent search state w `TorrentSearchConversationState` (ConversationContext snapshots). Response construction przez `ResponseArtifactBuilders` registry. QueuedEventBus z dispose w `EngineHost.StopAsync`. Legacy usunięte (PendingInvocationStore, ConfirmationCallbackHandler, TorrentSearchSessionStore, InMemoryBus, confirm: callbacks).

---

## 1. Aktualny Stan Kodu (sprawdzony)

- Capabilities: `CapabilityContract` + `CapabilityMetadata` rejestrowane w `PluginRegistrationContext.RegisterCapability`; `CapabilityRegistry.GetContract()`.
- LLM: `LlmSystemPromptBuilder` (kontrakty + pending actions + conversation state), `LlmPipeline`, Ollama* (bez Console.Error debug).
- Pipeline: `InvocationPipeline` + behaviors (`ToolKnowledge`, `ConversationState`, `ResponseConstruction`, `ConversationPending`, `PerTurnPrompt`), `ConversationResponseHandler`, `EngineHost`.
- Bus: `IInternalBus` + `QueuedEventBus` (channel queue, async dispatch, dispose w `StopAsync`).
- Query: `QuerySpec`, `ISnapshotSource` + `QuerySourceMeta`, `DuckDbQueryEngine`.
- User responses: `ConversationResponseHandler` (parse only) → `ConversationPipeline.ProcessUserResponseAsync` (resolve + execute); callback prefix `pending:yes/no:`; `pending:no` maps to cancel.
- Torrent search state: `TorrentSearchConversationState` + `TorrentSearchDisplay` (jedna projekcja 1-based indexów); `TorrentSearchPromptFormatting` dla LLM; `TorrentSearchSnapshotService` jako cienka fasada + `ISnapshotSource`.
- Response construction: `ResponseArtifactBuilders` registry (`list`, `search_results`, `confirmation`, `download_started`, `text`); `ContractResponseConstructor` enriches `artifactKind` from `ResponseSpec`; `InferKind` fallback uses `artifactKind` only (no key-sniffing).
- Test endpoint: `/test/inject-update` requires `TORRENTBOT_ENABLE_TEST_ENDPOINT=true` + `TORRENTBOT_TEST_ENDPOINT_SECRET` header `X-TorrentBot-Test-Secret`.
- Presentation: `IArtifactPresenter` deleguje do shared formatting (SearchResults, Confirmation, DownloadsList, DownloadStarted).
- Context: `CapabilityContext`, `IEngineContext`, `ConversationContextStore`.

---

## 2. Docelowa Wizja (spójna z istniejącym kodem)

- **CapabilityContract** (rozszerza/augmentuje istniejące `CapabilityMetadata`): ExactSemantics + UserInteractionSpec + ResponseConstructionSpec + ContinuationRule. Rejestracja obok Metadata w pluginach.
- **Rozszerzenie ConversationContext**: Dodaj wsparcie dla `PendingUserAction[]` (token + CapabilityContract + expected shape + continuation). Używaj istniejącego `ConversationContext` (History + Snapshots) jako bazę zamiast tworzyć od zera. `ConversationContextStore` zostaje.
- Response Construction first-class (używa spec z kontraktu + aktualnego ConversationContext).
- Pipeline dla query + user responses + behaviors (rozszerz istniejący `InvocationPipeline` / `LlmPipeline`).
- Event Queue (wzmocnij istniejący `IInternalBus` / InMemoryBus na queue + outbox).
- Per-turn meta prompty: rozbuduj `LlmSystemPromptBuilder` (wykorzystaj istniejące `AppendContextSnapshots` / `AppendConversationHistory`) + injekcja kontraktów + pending actions + reguły odpowiedzi.
- Bot dokładnie wie: pełne kontrakty w promptach + stanie, precyzyjne konstruowanie odpowiedzi, obsługa rekursywna N odpowiedzi (jedna odpowiedź użytkownika może dodać nowe pending actions via continuation).

Wizja buduje na istniejącym `ConversationContext` / snapshotach / LLM pipeline, nie zastępuje ich.

---

## 3. Instrukcje — Fazy (Co gdzie zrobić + Cleanup)

**Zasada:** Sprawdź kod → zmień → testy → usuń stary kod → zaktualizuj doc.

### Faza 1: Capability Contracts

**1.1** W `src/TorrentBot.Contracts/Capabilities/` utwórz `CapabilityContract.cs` (Name, ExactSemantics, Parameters, Risk, UserInteractions, ResponseSpec, Continuations + supporting records: UserInteractionSpec, ResponseConstructionSpec, ContinuationRule, ExpectedResponseShape).

**1.2** W `src/TorrentBot.Contracts/Plugins/IPluginRegistrationContext.cs` dodaj:
```csharp
void RegisterCapability(CapabilityContract contract, ICapabilityHandler handler);
```

W `PluginRegistrationContext.cs` zaimplementuj (przechowuj mapę).

W pluginach (np. `TorrentPlugin.cs`, `DownloadsPlugin.cs`):
- Dla każdej capability zbuduj pełny Contract (semantics + interactions + response spec + continuations).
- Zarejestruj.

**1.3 Cleanup:** Po tym jak kontrakty są używane w LLM/pipeline — usuń duplikaty stringowych opisów z CapabilityMetadata.

### Faza 2: Rekursywna Obsługa N Odpowiedzi (buduj na istniejącym ConversationContext)

**2.1** Rozszerz istniejący `ConversationContext` (src/TorrentBot.Contracts/Context/ConversationContext.cs):
- Dodaj kolekcję `PendingUserAction` (z token, powiązany CapabilityContract, ExpectedResponseShape, ContinuationRule).
- Metody: `AddPendingAction(...)`, `ResolvePendingAction(token, userResponse)`, `GetPendingActions()`.

Utwórz wspierające rekordy w `src/TorrentBot.Contracts/Conversation/` lub bezpośrednio w Context:
- `PendingUserAction.cs`, `UserResponse.cs`, `ExpectedResponseShape.cs`, `ContinuationRule.cs`.

**2.2** W `src/TorrentBot.Adapters.Telegram/`:
- Zastąp logikę `PendingInvocationStore` + `ConfirmationCallbackHandler` na użycie rozszerzonego `ConversationContext` + nowego `ConversationResponseHandler`.
- W `TelegramBotHost.cs`: zastąp `RegisterConfirmationIfNeeded` + confirmed execution path na delegację do Conversation Pipeline / `ProcessUserResponse`.

W `src/TorrentBot.Plugins.Torrent/TorrentSearchSessionStore.cs`:
- Zamiast osobnego store, używaj `context.UpdateSnapshot("torrent_search_results", ...)` (już częściowo tak jest). Potem usuń dedykowany store.

**2.3** Utwórz / rozszerz `IConversationPipeline` (lub handler w Llm/Engine) z `ProcessUserResponseAsync(UserResponse response, ConversationContext context)`.

Zintegruj z istniejącym `EngineHost.HandleNaturalLanguageAsync` i `InvocationPipeline` — user response wchodzi jako specjalny krok.

**2.4 Rekursja**:
- Po przetworzeniu odpowiedzi użytkownika via `ContinuationRule` — jeśli wymaga, dodaj nowe `PendingUserAction` do ConversationContext (może być wiele).
- LLM w kolejnym turnie widzi aktualne pending actions przez snapshoty/historię i decyduje o kontynuacji.

**Cleanup (po migracji głównych flow torrent + downloads):**
- Usuń `PendingInvocation.cs`, `PendingInvocationStore.cs`, `ConfirmationCallbackHandler.cs`.
- Usuń lub zdeprecjonuj `ConfirmationArtifact` (zastąpione przez PendingUserAction w stanie).
- Usuń `RegisterConfirmationIfNeeded` z TelegramBotHost.
- Usuń `TorrentSearchSessionStore.cs` (logika przez ConversationContext snapshots).

### Faza 3: Response Construction

**3.1** Utwórz `IResponseConstructor` (używa ResponseSpec z Contract + ConversationState + wynik).

**3.2** W `SearchResultsPresenter`, `DownloadsListPresenter`, `ArtifactAccumulator`:
- Deleguj budowanie do ResponseConstructor używającego kontraktu.

**3.3** W pipeline (po capability execution): wywołaj constructor używając CapabilityContract.

**Cleanup:**
- Usuń duplikaty HumanSize/Format* i string.Join w handlerach (przenieś do wspólnego).

### Faza 4: Pipeline dla Query + User Responses + Behaviors

**4.1** W `src/TorrentBot.Engine/Pipeline/` rozszerz `InvocationPipeline` lub utwórz kompozyt z sub-pipelines (Query, Conversation) + behaviors:
- `ToolKnowledgeBehavior`
- `ConversationStateBehavior`
- `ResponseConstructionBehavior`
- `PerTurnPromptBehavior`

**4.2** W `LlmPipeline.cs` i adapterach: przed planem inject CapabilityContracty + aktualny ConversationState.

**4.3** Query handlers i user response path idą przez pipeline.

### Faza 5: Per-Turn Meta Prompts z Dokładną Wiedzą

**5.1** W `src/TorrentBot.Llm/LlmSystemPromptBuilder.cs`:
- Zrefaktoryzuj na BuildPlanner + BuildResponseHandling (per-turn).
- Inject pełne CapabilityContract + ConversationState (pending + ostatnie tury).
- Dodaj sekcje promptu:
  - "## Tool & Capability Contracts (exact semantics, interactions, continuations)"
  - "## Current Conversation State & Pending Actions (N recursive)"
  - "## How to construct response (use ResponseSpec)"
  - "## What to expect and what to do with user response (recursion rules)"

**5.2** W `LlmPipeline.cs`:
- Pobierz kontrakty + stan.
- Używaj providerów (SearchFlowPromptProvider, MultiResponsePromptProvider...).

**Cleanup:**
- Usuń wszystkie `Console.Error.WriteLine` z LlmSystemPromptBuilder, Ollama*.
- Usuń stare stringowe CRITICAL reguły (zastąpione kontraktami).

### Faza 6: Wewnętrzna Kolejka + Eventy

**6.1** W `src/TorrentBot.Engine/Bus/`:
- Rozszerz `IInternalBus` lub dodaj `IEventQueue` (Channels + bounded + outbox używając istniejącego DB).

**6.2** Zastąp bezpośrednie Publish na queue + zarejestrowane handlery.

**6.3** Wprowadź eventy:
- `ToolCallEvent`
- `AwaitUserResponseEvent`
- `UserResponseReceivedEvent`
- `ResponseConstructedEvent`
- `ConversationStateChanged`

**Cleanup:** Po migracji usuń stary prosty InMemoryBus (lub zostaw jako impl).

### Faza 7: Pełny Cleanup + Spójność (dla każdej fazy)

- Po aktywacji nowej ścieżki w głównych flow → **usuń** stary kod (listy w Fazach 1-6).
- Grep na stare wzorce (Console, "confirm:", Dictionary w Data, PendingInvocation, TorrentSearchSessionStore).
- Zaktualizuj testy/e2e.
- Zaktualizuj docs jeśli potrzeba.

**Checklist spójności przed PR:**
- Dokument odwołuje się do rzeczywistych plików z repo.
- Stary kod usunięty (brak dual path).
- CapabilityContract + ConversationState używane w praktyce.
- Rekursja + N odpowiedzi działa.
- Per-turn prompty z kontraktami + stanem.
- Pipeline + eventy dla wszystkiego.

---

## 4. Podsumowanie

Dokument jest spójny, instrukcyjny, zakotwiczony w rzeczywistym kodzie repo i pokrywa całość wymagań (pipeline, queue/events, per-turn prompty, dokładna wiedza o narzędziach, response construction, rekursywna obsługa N odpowiedzi użytkownika + pełny cleanup).

**Zacznij:** Faza 1 (Contracts) → Faza 2 (ConversationState).

Po każdej fazie: implementuj → testuj → usuń stary → zaktualizuj ten dokument.

*Żyjący dokument instrukcji. Zawsze weryfikuj z repo. Pełny cleanup. Spójność.*