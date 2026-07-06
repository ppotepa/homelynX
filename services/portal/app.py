from __future__ import annotations

from fastapi import FastAPI

from portal_app.admin_routes import register_admin_routes
from portal_app.db import ensure_db, purge_old
from portal_app.llm_audit_routes import register_llm_audit_routes
from portal_app.routes_public import register_public_routes

app = FastAPI(title="Homelynx Portal", version="1.0.0")


@app.on_event("startup")
def startup() -> None:
    ensure_db()
    purge_old()


register_public_routes(app)
register_llm_audit_routes(app)
register_admin_routes(app)
