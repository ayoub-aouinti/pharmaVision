"""File d'attente sur disque : aucun événement n'est perdu si Internet coupe ou si le boîtier redémarre."""

import json
import logging
import os
import time
from pathlib import Path

from .client import ServerError

log = logging.getLogger(__name__)

BATCH = 1000  # le serveur accepte 5 000 lignes par lot


class Outbox:
    def __init__(self, directory: Path, max_files: int = 5000):
        self.directory, self.max_files = directory, max_files
        directory.mkdir(parents=True, exist_ok=True)
        self._counter = 0

    def pending(self) -> list[Path]:
        return sorted(self.directory.glob("*.json"))

    def add(self, events: list[dict]) -> None:
        for start in range(0, len(events), BATCH):
            self._counter += 1
            path = self.directory / f"{time.time_ns():020d}-{self._counter:06d}.json"
            # Écriture puis renommage : un fichier lisible est toujours complet.
            partial = path.with_suffix(".part")
            partial.write_text(json.dumps(events[start:start + BATCH]), encoding="utf-8")
            os.replace(partial, path)
        for oldest in self.pending()[: -self.max_files]:
            log.warning("File pleine : lot %s abandonné", oldest.name)
            oldest.unlink(missing_ok=True)  # peut-être envoyé à l’instant

    def flush(self, send) -> bool:
        """Envoie les lots du plus ancien au plus récent ; s'arrête au premier échec passager."""
        for path in self.pending():
            try:
                send(json.loads(path.read_text(encoding="utf-8")))
            except ServerError as error:
                # Un lot refusé sur le fond bloquerait tous les suivants : il est mis de côté.
                if error.status in (400, 413):
                    log.error("%s ; lot mis de côté : %s", error, path.name)
                    os.replace(path, path.with_suffix(".rejected"))
                    continue
                log.warning("%s", error)
                return False
            except json.JSONDecodeError:
                os.replace(path, path.with_suffix(".rejected"))
                continue
            except FileNotFoundError:
                continue  # abandonné entre-temps (file pleine)
            path.unlink(missing_ok=True)
        return True
