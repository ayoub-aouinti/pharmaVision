"""
Échanges réseau dans un fil séparé : un serveur lent ou injoignable ne ralentit jamais l'analyse.
"""

import logging
import queue
import threading
import time

from .client import Client, ServerError
from .outbox import Outbox

log = logging.getLogger(__name__)

CONFIG_SECONDS = 10
SEND_SECONDS = 2


class Uplink:
    def __init__(self, client: Client, outbox: Outbox):
        self.client, self.outbox = client, outbox
        # Réponses du serveur, dans leur ordre d'arrivée : ("config", dict) ou ("photo", (caméra, date)).
        self.inbox: queue.Queue = queue.Queue()
        self._photos: dict[int, bytes] = {}
        self._lock = threading.Lock()
        self._stop = threading.Event()
        self._thread = threading.Thread(target=self._run, daemon=True)

    def start(self) -> None:
        self._thread.start()

    def send_photo(self, camera_id: int, jpeg: bytes) -> None:
        """Conservée en mémoire jusqu'à son acceptation ; une plus récente la remplace."""
        with self._lock:
            self._photos[camera_id] = jpeg

    def _run(self) -> None:
        config_at = float("-inf")
        while not self._stop.is_set():
            try:
                if time.monotonic() - config_at >= CONFIG_SECONDS:
                    config_at = time.monotonic()
                    self.inbox.put(("config", self.client.config()))
                    self.client.heartbeat()
                self.exchange()
            except ServerError as error:
                log.warning("%s", error)
            except Exception:  # le fil réseau ne doit pas mourir en silence
                log.exception("Échange avec le serveur impossible")
            self._stop.wait(SEND_SECONDS)

    def exchange(self) -> None:
        self.outbox.flush(self.client.events)
        with self._lock:
            photos = list(self._photos.items())
        for camera_id, jpeg in photos:
            at = self.client.image(camera_id, jpeg)
            with self._lock:
                if self._photos.get(camera_id) is jpeg:
                    del self._photos[camera_id]
            self.inbox.put(("photo", (camera_id, at)))
            log.info("Image de référence de la caméra %s envoyée", camera_id)

    def close(self) -> None:
        """Dernier envoi ; ce qui échoue reste dans la file sur disque."""
        self._stop.set()
        self._thread.join(timeout=60)
        try:
            self.exchange()
        except ServerError as error:
            log.warning("%s", error)
