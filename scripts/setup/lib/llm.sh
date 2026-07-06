# shellcheck shell=bash

ensure_llm_model() {
  local model
  local keep_alive
  local payload

  if [ "$(get_env_value LLM_ENABLED)" = "false" ]; then
    log "LLM is disabled; skipping model pull."
    return
  fi

  model="$(get_env_value LLM_MODEL)"
  if [ -z "$model" ]; then
    warn "LLM_MODEL is empty; skipping model pull."
    return
  fi

  if docker exec llm ollama list 2>/dev/null | awk '{print $1}' | grep -qx "$model"; then
    log "LLM model already available: $model"
  else
    log "Pulling LLM model: $model"
    docker exec llm ollama pull "$model"
  fi

  keep_alive="$(get_env_value BOT_NATURAL_LANGUAGE_KEEP_ALIVE)"
  if [ -z "$keep_alive" ]; then
    keep_alive="$(get_env_value OLLAMA_KEEP_ALIVE)"
  fi
  if [ -z "$keep_alive" ]; then
    keep_alive="-1"
  fi

  log "Warming up LLM model: $model keep_alive=$keep_alive"
  if printf '%s' "$keep_alive" | grep -Eq '^-?[0-9]+$'; then
    payload="$(printf '{"model":"%s","prompt":"Reply with exactly: ok","stream":false,"keep_alive":%s,"options":{"temperature":0,"num_predict":8}}' "$model" "$keep_alive")"
  else
    payload="$(printf '{"model":"%s","prompt":"Reply with exactly: ok","stream":false,"keep_alive":"%s","options":{"temperature":0,"num_predict":8}}' "$model" "$keep_alive")"
  fi
  if ! curl -fsS --max-time 60 -H "Content-Type: application/json" -d "$payload" "http://127.0.0.1:11434/api/generate" >/dev/null; then
    warn "LLM warmup failed; model is pulled but may cold-start on first request."
  fi
}
