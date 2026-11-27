"""Boucle principale : configuration, lecture des caméras, analyse et envoi."""

import logging
import secrets
import time
from collections import Counter
from datetime import datetime, timezone

import cv2

from .client import Client, ServerError, event_payload
from .detector import PersonDetector
from .outbox import Outbox
from .privacy import apply_masks, background, blur_people
from .source import Source
from .tracker import Tracker
from .uplink import Uplink
from .zones import Event, Person, Zone, ZoneAnalyzer

log = logging.getLogger(__name__)

BUFFER_SECONDS = 2  # regroupement des événements avant leur mise en file
PHOTO_SAMPLES = 25
PHOTO_INTERVAL = 0.6  # soit une quinzaine de secondes d'observation
PHOTO_WIDTH = 1280
COLORS = {"Shelf": (110, 160, 18), "Queue": (43, 121, 232), "CountingLine": (229, 197, 142),
          "Restricted": (30, 84, 201), "Passage": (209, 143, 61)}


def _points(polygon: list[dict]) -> list[tuple[float, float]]:
    return [(p["x"], p["y"]) for p in polygon]


class CameraWorker:
    def __init__(self, camera_id: int, source: Source, detector: PersonDetector, preview: str | None, fps: float):
        self.id, self.source, self.detector = camera_id, source, detector
        self.tracker = Tracker()
        self.analyzer = ZoneAnalyzer([])
        self.masks: list = []
        # Identifiants anonymes, renouvelés à chaque démarrage.
        self.session = secrets.token_hex(3)
        self.frames = 0
        self.totals: Counter = Counter()
        self.samples: list = []
        self.sampled_at = 0.0
        self.photo_wanted = True
        self.photo_at: str | None = None
        self.preview_path, self.preview, self.fps = preview, None, fps

    def configure(self, config: dict) -> None:
        camera = next(c for c in config["cameras"] if c["id"] == self.id)
        self.masks = [_points(m) for m in camera["masks"]]
        self.analyzer.set_zones([Zone(z["id"], z["type"], _points(z["polygon"]))
                                 for z in config["zones"] if z["cameraId"] == self.id])
        # Photo demandée depuis l'application après la dernière envoyée.
        if self.photo_at is not None and camera["photoAt"] != self.photo_at:
            self.photo_wanted, self.samples = True, []
            self.photo_at = camera["photoAt"]

    def step(self, now: float) -> list[dict]:
        frame = self.source.read()
        if frame is None:
            return []
        frame = apply_masks(frame, self.masks)
        boxes = self.detector.detect(frame)
        height, width = frame.shape[:2]
        tracks, lost = self.tracker.update(boxes, now)
        people = [Person(t.id, ((t.box[0] + t.box[2]) / 2 / width, t.box[3] / height),
                         ((t.box[0] + t.box[2]) / 2 / width, (t.box[1] + t.box[3]) / 2 / height)) for t in tracks]
        events = self.analyzer.update(people, now, lost)
        self.frames += 1

        if self.photo_wanted and now - self.sampled_at >= PHOTO_INTERVAL:
            self.samples.append(frame)
            self.sampled_at = now
        if self.preview_path:
            self._draw(frame, boxes, tracks)
        return self.payloads(events)

    def payloads(self, events: list[Event]) -> list[dict]:
        ts = datetime.now(timezone.utc)
        self.totals.update(e.type for e in events)
        return [event_payload(ts, e.zone_id, e.type, e.value, f"c{self.id}-{self.session}-{e.track}")
                for e in events]

    def photo(self, force: bool = False) -> bytes | None:
        """
        Image de référence prête à envoyer (une seule fois par demande) : fond médian,
        personnes restantes floutées, masques noircis.
        """
        if not self.photo_wanted or not self.samples or (len(self.samples) < PHOTO_SAMPLES and not force):
            return None
        image = background(self.samples)
        image = apply_masks(blur_people(image, self.detector.detect(image)), self.masks)
        if image.shape[1] > PHOTO_WIDTH:
            image = cv2.resize(image, (PHOTO_WIDTH, image.shape[0] * PHOTO_WIDTH // image.shape[1]))
        ok, jpeg = cv2.imencode(".jpg", image, [cv2.IMWRITE_JPEG_QUALITY, 85])
        if not ok:
            return None
        self.photo_wanted, self.samples = False, []
        return jpeg.tobytes()

    def _draw(self, frame, boxes, tracks) -> None:
        """Vidéo de contrôle : personnes floutées, zones et numéros temporaires."""
        view = blur_people(frame, boxes)
        height, width = view.shape[:2]
        for zone in self.analyzer.zones:
            points = [(int(x * width), int(y * height)) for x, y in zone.polygon]
            for a, b in zip(points, points[1:] + (points[:1] if zone.type != "CountingLine" else [])):
                cv2.line(view, a, b, COLORS.get(zone.type, (255, 255, 255)), 2)
        for track in tracks:
            x1, y1, x2, y2 = (int(v) for v in track.box)
            cv2.rectangle(view, (x1, y1), (x2, y2), (255, 255, 255), 1)
            cv2.putText(view, str(track.id), (x1, max(12, y1 - 4)), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (255, 255, 255), 1)
        summary = "  ".join(f"{name}: {count}" for name, count in sorted(self.totals.items()))
        cv2.putText(view, summary, (10, height - 12), cv2.FONT_HERSHEY_SIMPLEX, 0.55, (255, 255, 255), 2)
        if self.preview is None:
            self.preview = cv2.VideoWriter(
                self.preview_path, cv2.VideoWriter_fourcc(*"mp4v"), self.fps, (width, height))
        self.preview.write(view)

    def close(self) -> list[dict]:
        events = self.payloads(self.analyzer.close())
        self.source.close()
        if self.preview is not None:
            self.preview.release()
        return events


def wait_for_config(client: Client) -> dict:
    while True:
        try:
            return client.config()
        except ServerError as error:
            if error.status == 401:
                raise
            log.warning("%s ; nouvelle tentative", error)
            time.sleep(5)


def run(client: Client, workers: list[CameraWorker], outbox: Outbox, config: dict,
        fps: float = 5, duration: float | None = None, send_photos: bool = True) -> None:
    for worker in workers:
        worker.configure(config)
        worker.photo_wanted = send_photos
    uplink = Uplink(client, outbox)
    uplink.start()
    started = buffered_at = time.monotonic()
    buffer: list[dict] = []

    try:
        while not all(w.source.finished for w in workers):
            tick = time.monotonic()
            if duration and tick - started >= duration:
                break
            _apply(uplink, workers)
            for worker in workers:
                buffer += worker.step(time.monotonic())
                if (jpeg := worker.photo()) is not None:
                    uplink.send_photo(worker.id, jpeg)

            if buffer and tick - buffered_at >= BUFFER_SECONDS:
                outbox.add(buffer)
                buffer, buffered_at = [], tick

            time.sleep(max(0, 1 / fps - (time.monotonic() - tick)))
    except KeyboardInterrupt:
        log.info("Arrêt demandé")
    finally:
        for worker in workers:
            # Flux court : l'image est envoyée même avec peu d'échantillons.
            if (jpeg := worker.photo(force=True)) is not None:
                uplink.send_photo(worker.id, jpeg)
            buffer += worker.close()
        if buffer:
            outbox.add(buffer)
        uplink.close()
        for worker in workers:
            log.info("Caméra %s : %s images analysées, événements %s", worker.id, worker.frames,
                     dict(sorted(worker.totals.items())) or "aucun")
        if outbox.pending():
            log.warning("%s lot(s) en attente, envoyés au prochain démarrage", len(outbox.pending()))


def _apply(uplink: Uplink, workers: list[CameraWorker]) -> None:
    """Applique les réponses du serveur reçues depuis le dernier passage."""
    while not uplink.inbox.empty():
        kind, value = uplink.inbox.get()
        if kind == "photo":
            camera_id, photo_at = value
            for worker in workers:
                if worker.id == camera_id:
                    worker.photo_at = photo_at
            continue
        known = {c["id"] for c in value["cameras"]}
        for worker in workers:
            # Caméra désactivée ou retirée : plus aucune zone à analyser.
            if worker.id in known:
                worker.configure(value)
            else:
                worker.analyzer.set_zones([])
