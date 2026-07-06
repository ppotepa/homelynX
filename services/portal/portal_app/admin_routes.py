from __future__ import annotations

import json
import re
import urllib.parse
from typing import Any

from fastapi import Depends, FastAPI, HTTPException, Query, Request
from fastapi.responses import HTMLResponse, PlainTextResponse

from .auth import require_csrf, require_portal_auth
from .homelynx_capabilities import load_homelynx_capabilities
from .config import MODULE_FLAGS, SERVICE_CONTAINERS
from .docker_client import docker_json, docker_request
from .env_editor import read_env_file, write_env_value
from .html import ADMIN_HTML
from .utils import redact_log_text


def register_admin_routes(app: FastAPI) -> None:
    @app.get("/admin", response_class=HTMLResponse)
    def admin_panel(_: None = Depends(require_portal_auth)) -> str:
        return ADMIN_HTML

    @app.get("/api/admin/status")
    def admin_status(_: None = Depends(require_portal_auth)) -> dict[str, Any]:
        containers = []
        try:
            raw = docker_json("GET", "/containers/json?all=1")
            by_name = {}
            for item in raw:
                for name in item.get("Names") or []:
                    by_name[name.lstrip("/")] = item
            for service, container in SERVICE_CONTAINERS.items():
                item = by_name.get(container)
                containers.append({
                    "service": service,
                    "container": container,
                    "state": item.get("State") if item else "missing",
                    "status": item.get("Status") if item else "not created",
                    "image": item.get("Image") if item else "",
                })
        except HTTPException as exc:
            containers.append({"service": "docker", "container": "docker", "state": "error", "status": str(exc.detail), "image": ""})
        env = read_env_file()
        modules = [{"name": name, "key": key, "enabled": env.get(key, "").lower() not in {"0", "false", "no", "off"}, "value": env.get(key, "")} for name, key in MODULE_FLAGS.items()]
        return {"success": True, "containers": containers, "modules": modules}

    @app.get("/api/admin/capabilities")
    def admin_capabilities(_: None = Depends(require_portal_auth)) -> dict[str, Any]:
        return load_homelynx_capabilities()

    @app.post("/api/admin/service/{service}/{action}")
    def admin_service_action(
        service: str,
        action: str,
        _: None = Depends(require_portal_auth),
        __: None = Depends(require_csrf),
    ) -> dict[str, Any]:
        if service not in SERVICE_CONTAINERS:
            raise HTTPException(status_code=404, detail="Unknown service")
        if action not in {"start", "stop", "restart"}:
            raise HTTPException(status_code=400, detail="Unsupported action")
        container = urllib.parse.quote(SERVICE_CONTAINERS[service], safe="")
        status, payload = docker_request("POST", f"/containers/{container}/{action}")
        if status >= 400:
            try:
                detail: Any = json.loads(payload.decode(errors="ignore"))
            except Exception:
                detail = payload.decode(errors="ignore")[:500]
            raise HTTPException(status_code=403 if status == 403 else 502, detail={"docker_status": status, "docker": detail})
        return {"success": True, "service": service, "action": action}

    @app.get("/api/admin/logs/{service}")
    def admin_logs(service: str, _: None = Depends(require_portal_auth), tail: int = Query(default=200, ge=20, le=1000)) -> PlainTextResponse:
        if service not in SERVICE_CONTAINERS:
            raise HTTPException(status_code=404, detail="Unknown service")
        container = urllib.parse.quote(SERVICE_CONTAINERS[service], safe="")
        status, payload = docker_request("GET", f"/containers/{container}/logs?stdout=1&stderr=1&timestamps=1&tail={tail}")
        if status >= 400:
            raise HTTPException(status_code=502, detail="Could not read logs")
        text = payload.decode("utf-8", errors="replace")
        text = re.sub(r"[\x00-\x08\x0b\x0c\x0e-\x1f]", "", text)
        return PlainTextResponse(redact_log_text(text))

    @app.post("/api/admin/module/{module}")
    async def admin_set_module(
        module: str,
        request: Request,
        _: None = Depends(require_portal_auth),
        __: None = Depends(require_csrf),
    ) -> dict[str, Any]:
        if module not in MODULE_FLAGS:
            raise HTTPException(status_code=404, detail="Unknown module")
        body = await request.json()
        enabled = bool(body.get("enabled"))
        key = MODULE_FLAGS[module]
        write_env_value(key, "true" if enabled else "false")
        return {"success": True, "module": module, "key": key, "enabled": enabled, "note": "Config saved. Restart affected services to apply runtime changes."}
