"""
Transforme les positions suivies en événements anonymes, au format attendu par le serveur.

Ne sont pas mesurés : le remplissage des linéaires, les prises en main et les tickets de caisse.
« Sans conseil » signifie seulement « longtemps devant le rayon » : le personnel n'est pas distingué.
"""

from dataclasses import dataclass

from .geometry import Point, crossing, point_in_polygon

CONFIRM_SECONDS = 0.5    # présence minimale avant de compter une entrée dans une zone
LEAVE_SECONDS = 1.0      # absence minimale avant de la considérer quittée
STOP_SECONDS = 5.0       # au-delà : arrêt (même valeur que côté serveur)
QUEUE_SECONDS = 3.0      # en dessous : simple passage devant le comptoir
RESTRICTED_SECONDS = 2.0
UNATTENDED_SECONDS = 90.0
LINE_DEBOUNCE_SECONDS = 2.0


@dataclass(frozen=True)
class Zone:
    id: int
    type: str  # Shelf, Passage, Queue, CountingLine, Restricted
    polygon: list[Point]


@dataclass(frozen=True)
class Event:
    zone_id: int
    type: str
    value: float
    track: int


@dataclass(frozen=True)
class Person:
    """Position normalisée d'une personne suivie."""

    track: int
    foot: Point    # pieds : zones au sol et lignes
    center: Point  # centre du corps : rayons verticaux face à la caméra


@dataclass
class _Visit:
    since: float
    last_inside: float
    confirmed: bool = False
    reported: bool = False


class ZoneAnalyzer:
    def __init__(self, zones: list[Zone]):
        self.zones = zones
        self._visits: dict[tuple[int, int], _Visit] = {}
        self._feet: dict[int, Point] = {}
        self._crossed: dict[tuple[int, int], float] = {}

    def set_zones(self, zones: list[Zone]) -> None:
        """Applique une nouvelle configuration sans perdre les présences des zones inchangées."""
        kept = {z.id for z in zones if z in self.zones}
        self._visits = {key: v for key, v in self._visits.items() if key[0] in kept}
        self.zones = zones

    def update(self, people: list[Person], now: float, lost: list[int] = []) -> list[Event]:
        events: list[Event] = []
        for person in people:
            previous = self._feet.get(person.track)
            for zone in self.zones:
                if zone.type == "CountingLine":
                    if previous:
                        events += self._line(zone, person, previous, now)
                    continue
                anchor = person.center if zone.type == "Shelf" else person.foot
                if point_in_polygon(anchor, zone.polygon):
                    events += self._inside(zone, person.track, now)
            self._feet[person.track] = person.foot

        # Zones quittées, ou personne perdue de vue.
        by_id = {z.id: z for z in self.zones}
        for key, visit in list(self._visits.items()):
            zone_id, track = key
            if track in lost or now - visit.last_inside >= LEAVE_SECONDS:
                events += self._leave(by_id[zone_id], track, visit)
                del self._visits[key]
        for track in lost:
            self._feet.pop(track, None)
        return events

    def close(self) -> list[Event]:
        """Fin de flux : termine les présences en cours."""
        by_id = {z.id: z for z in self.zones}
        events = [e for (zone_id, track), visit in self._visits.items()
                  for e in self._leave(by_id[zone_id], track, visit)]
        self._visits.clear()
        self._feet.clear()
        return events

    def _line(self, zone: Zone, person: Person, previous: Point, now: float) -> list[Event]:
        direction = crossing(zone.polygon[0], zone.polygon[1], previous, person.foot)
        key = (zone.id, person.track)
        if direction == 0 or now - self._crossed.get(key, float("-inf")) < LINE_DEBOUNCE_SECONDS:
            return []
        self._crossed[key] = now
        return [Event(zone.id, "Entry" if direction > 0 else "Exit", 1, person.track)]

    def _inside(self, zone: Zone, track: int, now: float) -> list[Event]:
        visit = self._visits.setdefault((zone.id, track), _Visit(since=now, last_inside=now))
        visit.last_inside = now
        dwell = now - visit.since
        events: list[Event] = []
        if not visit.confirmed and dwell >= CONFIRM_SECONDS:
            visit.confirmed = True
            if zone.type in ("Shelf", "Passage"):
                events.append(Event(zone.id, "Pass", 1, track))
        # Signalés pendant la présence : l'alerte ne doit pas attendre le départ.
        if not visit.reported:
            if zone.type == "Restricted" and dwell >= RESTRICTED_SECONDS:
                visit.reported = True
                events.append(Event(zone.id, "Stop", round(dwell, 1), track))
            elif zone.type == "Shelf" and dwell >= UNATTENDED_SECONDS:
                visit.reported = True
                events.append(Event(zone.id, "Unattended", round(dwell, 1), track))
        return events

    @staticmethod
    def _leave(zone: Zone, track: int, visit: _Visit) -> list[Event]:
        dwell = visit.last_inside - visit.since
        if not visit.confirmed:
            return []
        if zone.type in ("Shelf", "Passage") and dwell > STOP_SECONDS:
            return [Event(zone.id, "Stop", round(dwell, 1), track)]
        if zone.type == "Queue" and dwell >= QUEUE_SECONDS:
            return [Event(zone.id, "QueueWait", round(dwell, 1), track)]
        return []
