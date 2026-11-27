"""Suivi simple par recouvrement : donne un numéro temporaire à chaque personne visible."""

from dataclasses import dataclass

Box = tuple[float, float, float, float]  # x1, y1, x2, y2


def iou(a: Box, b: Box) -> float:
    width = min(a[2], b[2]) - max(a[0], b[0])
    height = min(a[3], b[3]) - max(a[1], b[1])
    if width <= 0 or height <= 0:
        return 0.0
    shared = width * height
    return shared / ((a[2] - a[0]) * (a[3] - a[1]) + (b[2] - b[0]) * (b[3] - b[1]) - shared)


@dataclass
class Track:
    id: int
    box: Box
    seen_at: float
    hits: int = 1
    # Déplacement par seconde du centre, pour prévoir la position suivante.
    velocity: tuple[float, float] = (0.0, 0.0)

    def predicted(self, now: float) -> Box:
        dt = now - self.seen_at
        dx, dy = self.velocity[0] * dt, self.velocity[1] * dt
        return (self.box[0] + dx, self.box[1] + dy, self.box[2] + dx, self.box[3] + dy)


class Tracker:
    def __init__(self, min_iou: float = 0.2, max_age: float = 1.5, min_hits: int = 3):
        self.min_iou, self.max_age, self.min_hits = min_iou, max_age, min_hits
        self._tracks: list[Track] = []
        self._next = 1

    def update(self, boxes: list[Box], now: float) -> tuple[list[Track], list[int]]:
        """Retourne les pistes confirmées vues à cet instant et les numéros des pistes abandonnées."""
        # Association gloutonne, meilleurs recouvrements d'abord.
        pairs = sorted(
            ((iou(track.predicted(now), box), t, b)
             for t, track in enumerate(self._tracks) for b, box in enumerate(boxes)),
            reverse=True,
        )
        used_tracks: set[int] = set()
        used_boxes: set[int] = set()
        for score, t, b in pairs:
            if score < self.min_iou:
                break
            if t in used_tracks or b in used_boxes:
                continue
            used_tracks.add(t)
            used_boxes.add(b)
            self._move(self._tracks[t], boxes[b], now)

        for b, box in enumerate(boxes):
            if b not in used_boxes:
                self._tracks.append(Track(self._next, box, now))
                self._next += 1

        lost = [t.id for t in self._tracks if now - t.seen_at > self.max_age]
        self._tracks = [t for t in self._tracks if t.id not in lost]
        return [t for t in self._tracks if t.seen_at == now and t.hits >= self.min_hits], lost

    @staticmethod
    def _move(track: Track, box: Box, now: float) -> None:
        dt = now - track.seen_at
        if dt > 0:
            moved = ((box[0] + box[2] - track.box[0] - track.box[2]) / 2 / dt,
                     (box[1] + box[3] - track.box[1] - track.box[3]) / 2 / dt)
            track.velocity = ((track.velocity[0] + moved[0]) / 2, (track.velocity[1] + moved[1]) / 2)
        track.box, track.seen_at = box, now
        track.hits += 1
