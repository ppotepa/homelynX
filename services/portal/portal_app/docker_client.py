from __future__ import annotations

import json
import socket
from typing import Any

from fastapi import HTTPException

from .config import DOCKER_ENABLED, DOCKER_SOCKET

def docker_request(method: str, path: str, body: bytes | None = None) -> tuple[int, bytes]:
    if not DOCKER_ENABLED:
        return 403, b'{"message":"Docker access disabled"}'
    if not DOCKER_SOCKET.exists():
        return 503, b'{"message":"Docker socket is not mounted"}'
    sock = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
    try:
        sock.settimeout(4)
        sock.connect(str(DOCKER_SOCKET))
        headers = [f"{method} {path} HTTP/1.1", "Host: docker", "Connection: close"]
        if body is not None:
            headers.append("Content-Type: application/json")
            headers.append(f"Content-Length: {len(body)}")
        raw = ("\r\n".join(headers) + "\r\n\r\n").encode() + (body or b"")
        sock.sendall(raw)
        chunks = []
        while True:
            chunk = sock.recv(65536)
            if not chunk:
                break
            chunks.append(chunk)
        response = b"".join(chunks)
    finally:
        sock.close()
    head, _, payload = response.partition(b"\r\n\r\n")
    status_line = head.splitlines()[0].decode(errors="ignore") if head else "HTTP/1.1 500"
    try:
        status = int(status_line.split()[1])
    except Exception:
        status = 500
    if b"transfer-encoding: chunked" in head.lower():
        payload = decode_chunked(payload)
    return status, payload
def decode_chunked(payload: bytes) -> bytes:
    output = bytearray()
    rest = payload
    while rest:
        line, _, rest = rest.partition(b"\r\n")
        try:
            size = int(line.split(b";", 1)[0], 16)
        except Exception:
            return payload
        if size == 0:
            break
        output.extend(rest[:size])
        rest = rest[size + 2:]
    return bytes(output)
def docker_json(method: str, path: str, body: dict[str, Any] | None = None) -> Any:
    raw_body = json.dumps(body).encode() if body is not None else None
    status, payload = docker_request(method, path, raw_body)
    if status >= 400:
        try:
            detail = json.loads(payload.decode(errors="ignore"))
        except Exception:
            detail = payload.decode(errors="ignore")[:500]
        http_status = 403 if status == 403 else 502
        raise HTTPException(status_code=http_status, detail={"docker_status": status, "docker": detail})
    if not payload:
        return {}
    return json.loads(payload.decode(errors="ignore"))
