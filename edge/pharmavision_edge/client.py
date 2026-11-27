"""Échanges avec le serveur. Contrat : backend/src/PharmaVision.Domain/Contracts.cs."""

import json
import urllib.error
import urllib.request
from datetime import datetime

from . import __version__

TIMEOUT = 15


class ServerError(Exception):
    def __init__(self, message: str, status: int | None = None):
        super().__init__(message)
        self.status = status


def event_payload(ts: datetime, zone_id: int, type_: str, value: float, track_id: str) -> dict:
    return {"ts": ts.isoformat(), "zoneId": zone_id, "type": type_, "value": value, "trackId": track_id}


class _NoRedirect(urllib.request.HTTPRedirectHandler):
    """La clé du boîtier ne doit pas suivre une redirection vers une autre adresse."""

    def redirect_request(self, *args, **kwargs):
        return None


class Client:
    def __init__(self, server: str, key: str):
        if not server.startswith(("http://", "https://")):
            raise ValueError("L'adresse du serveur doit commencer par http:// ou https://")
        self.server, self.key = server.rstrip("/"), key
        self._opener = urllib.request.build_opener(_NoRedirect)

    def _send(self, method: str, path: str, body: bytes | None = None, content_type: str | None = None) -> bytes:
        headers = {"X-Api-Key": self.key, "X-Device-Version": f"{__version__}-python"}
        if content_type:
            headers["Content-Type"] = content_type
        request = urllib.request.Request(self.server + path, data=body, headers=headers, method=method)
        try:
            with self._opener.open(request, timeout=TIMEOUT) as response:
                return response.read()
        except urllib.error.HTTPError as error:
            raise ServerError(f"Le serveur répond {error.code} à {path}", error.code) from error
        except (urllib.error.URLError, TimeoutError, ConnectionError) as error:
            raise ServerError(f"Serveur injoignable : {error}") from error

    def config(self) -> dict:
        return json.loads(self._send("GET", "/api/ingest/config"))

    def heartbeat(self) -> None:
        self._send("POST", "/api/ingest/heartbeat", b"")

    def events(self, events: list[dict]) -> None:
        self._send("POST", "/api/ingest/events", json.dumps({"events": events}).encode(), "application/json")

    def image(self, camera_id: int, jpeg: bytes) -> str:
        """Retourne la date de l'image, telle que le serveur la redonnera dans la configuration."""
        return json.loads(self._send("PUT", f"/api/ingest/cameras/{camera_id}/image", jpeg, "image/jpeg"))["photoAt"]
