# shellcheck shell=bash

# Collects unique model names from the main LLM_MODEL and the dedicated
# planner/responder/executor models used by the NL bot pipeline.
_collect_llm_models() {
  local models=""
  local m

  for key in LLM_MODEL LLM_PLANNER_MODEL LLM_RESPONDER_MODEL LLM_EXECUTOR_MODEL; do
    m="$(get_env_value "$key")"
    if [ -n "$m" ]; then
      # Avoid duplicates
      if ! echo "$models" | tr ' ' '\n' | grep -qx "$m"; then
        models="${models:+$models }$m"
      fi
    fi
  done

  # Fallbacks used by other services if not explicitly set
  for key in MEDIA_ORGANIZER_LLM_MODEL COORD_LLM_MODEL; do
    m="$(get_env_value "$key")"
    if [ -n "$m" ]; then
      if ! echo "$models" | tr ' ' '\n' | grep -qx "$m"; then
        models="${models:+$models }$m"
      fi
    fi
  done

  printf '%s\n' "$models"
}

ensure_llm_model() {
  local model
  local keep_alive
  local payload
  local models_to_ensure

  if [ "$(get_env_value LLM_ENABLED)" = "false" ]; then
    log "LLM is disabled; skipping model pull."
    return
  fi

  models_to_ensure="$(_collect_llm_models)"

  if [ -z "$models_to_ensure" ]; then
    warn "No LLM models configured (LLM_MODEL and planner/responder etc. are empty)."
    return
  fi

  for model in $models_to_ensure; do
    if docker exec llm ollama list 2>/dev/null | awk '{print $1}' | grep -qx "$model"; then
      log "LLM model already available: $model"
    else
      log "Pulling LLM model: $model"
      if ! docker exec llm ollama pull "$model"; then
        warn "Failed to pull $model. The bot may fail or pull on first use."
      fi
    fi
  done

  # Warm up the primary models used by the bot NL pipeline (planner + responder)
  # This helps with OLLAMA_MAX_LOADED_MODELS and KEEP_ALIVE
  keep_alive="$(get_env_value BOT_NATURAL_LANGUAGE_KEEP_ALIVE)"
  if [ -z "$keep_alive" ]; then
    keep_alive="$(get_env_value OLLAMA_KEEP_ALIVE)"
  fi
  if [ -z "$keep_alive" ]; then
    keep_alive="-1"
  fi

  local last_warmup=""
  for warmup_model in \
      "$(get_env_value LLM_PLANNER_MODEL)" \
      "$(get_env_value LLM_RESPONDER_MODEL)" \
      "$(get_env_value LLM_MODEL)"; do
    [ -z "$warmup_model" ] && continue
    # dedup in case they are same
    if [ -n "$last_warmup" ] && [ "$warmup_model" = "$last_warmup" ]; then continue; fi
    last_warmup="$warmup_model"

    log "Warming up LLM model: $warmup_model keep_alive=$keep_alive"
    if printf '%s' "$keep_alive" | grep -Eq '^-?[0-9]+$'; then
      payload="$(printf '{"model":"%s","prompt":"Reply with exactly: ok","stream":false,"keep_alive":%s,"options":{"temperature":0,"num_predict":8}}' "$warmup_model" "$keep_alive")"
    else
      payload="$(printf '{"model":"%s","prompt":"Reply with exactly: ok","stream":false,"keep_alive":"%s","options":{"temperature":0,"num_predict":8}}' "$warmup_model" "$keep_alive")"
    fi
    if ! curl -fsS --max-time 90 -H "Content-Type: application/json" -d "$payload" "http://127.0.0.1:11434/api/generate" >/dev/null; then
      warn "LLM warmup failed for $warmup_model; model is pulled but may cold-start on first request."
    fi
  done

  log "LLM models ensured: $(echo "$models_to_ensure" | tr ' ' ', ')"
}
