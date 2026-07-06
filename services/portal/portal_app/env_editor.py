from __future__ import annotations

from fastapi import HTTPException

from .config import ENV_CONFIG_PATH, ENV_WRITE_ENABLED, MODULE_FLAGS

def read_env_file() -> dict[str, str]:
    values: dict[str, str] = {}
    if not ENV_CONFIG_PATH.exists():
        return values
    for line in ENV_CONFIG_PATH.read_text(encoding="utf-8", errors="ignore").splitlines():
        if not line or line.lstrip().startswith("#") or "=" not in line:
            continue
        key, value = line.split("=", 1)
        values[key.strip()] = value.strip().strip('"').strip("'")
    return values
def write_env_value(key: str, value: str) -> None:
    if not ENV_WRITE_ENABLED:
        raise HTTPException(status_code=403, detail="Portal env writes are disabled")
    if key not in MODULE_FLAGS.values():
        raise HTTPException(status_code=403, detail="Unsupported config key")
    lines = ENV_CONFIG_PATH.read_text(encoding="utf-8", errors="ignore").splitlines() if ENV_CONFIG_PATH.exists() else []
    written = False
    out = []
    for line in lines:
        if line.startswith(f"{key}="):
            out.append(f"{key}={value}")
            written = True
        else:
            out.append(line)
    if not written:
        out.append(f"{key}={value}")
    ENV_CONFIG_PATH.parent.mkdir(parents=True, exist_ok=True)
    ENV_CONFIG_PATH.write_text("\n".join(out).rstrip() + "\n", encoding="utf-8")
