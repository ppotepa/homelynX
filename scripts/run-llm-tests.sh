#!/usr/bin/env bash
# Run 100 live LLM planner scenarios against Ollama.
# Usage:
#   ./scripts/run-llm-tests.sh              # all 100
#   ./scripts/run-llm-tests.sh LLM-001      # single scenario id
#   ./scripts/run-llm-tests.sh search_en    # category filter (via grep in dotnet filter)

set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

if [[ -f .env ]]; then set -a; source .env; set +a; fi

export TORRENTBOT_RUN_LLM_TESTS=true

FILTER="${1:-}"
if [[ -n "$FILTER" ]]; then
  export TORRENTBOT_LLM_SCENARIO_FILTER="$FILTER"
  echo "Scenario filter: $FILTER"
fi

echo "LLM_URL/host: ${LLM_URL:-${LLM_HOST:-not set}}"
echo "Planner model: ${LLM_PLANNER_MODEL:-${LLM_MODEL:-default}}"
echo "Running live LLM planner tests..."
echo "---"

dotnet test src/TorrentBot.Engine.Tests/TorrentBot.Engine.Tests.csproj \
  --filter "FullyQualifiedName~LlmPlannerScenarioTests.Live_planner_scenario" \
  --logger "console;verbosity=normal"