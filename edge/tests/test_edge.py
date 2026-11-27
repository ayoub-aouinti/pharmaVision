from datetime import datetime, timezone

import numpy as np
import pytest

from pharmavision_edge.client import Client, ServerError, event_payload
from pharmavision_edge.detector import suppress
from pharmavision_edge.geometry import crossing, point_in_polygon
from pharmavision_edge.outbox import Outbox
from pharmavision_edge.privacy import apply_masks, background, blur_people
from pharmavision_edge.tracker import Tracker
from pharmavision_edge.uplink import Uplink
from pharmavision_edge.zones import Person, Zone, ZoneAnalyzer

SQUARE = [(0.2, 0.2), (0.6, 0.2), (0.6, 0.6), (0.2, 0.6)]
INSIDE, OUTSIDE = (0.4, 0.4), (0.9, 0.9)


def test_point_in_polygon():
    assert point_in_polygon(INSIDE, SQUARE)
    assert not point_in_polygon(OUTSIDE, SQUARE)


def test_line_crossing_has_a_direction_and_bounded_ends():
    a, b = (0.2, 0.5), (0.8, 0.5)
    assert crossing(a, b, (0.5, 0.4), (0.5, 0.6)) == 1
    assert crossing(a, b, (0.5, 0.6), (0.5, 0.4)) == -1
    assert crossing(a, b, (0.5, 0.6), (0.5, 0.7)) == 0
    assert crossing(a, b, (0.9, 0.4), (0.9, 0.6)) == 0  # à côté de la ligne


def walk(analyzer, positions, track=1, step=0.5):
    """Fait suivre des positions à une personne, puis la perd de vue."""
    events, now = [], 0.0
    for position in positions:
        events += analyzer.update([Person(track, position, position)], now)
        now += step
    return events + analyzer.update([], now, lost=[track])


def summary(events):
    return [(e.type, e.value) for e in events]


@pytest.mark.parametrize("kind,seconds,expected", [
    ("Shelf", 8, [("Pass", 1), ("Stop", 8.0)]),
    ("Shelf", 3, [("Pass", 1)]),
    ("Passage", 8, [("Pass", 1), ("Stop", 8.0)]),
    ("Queue", 20, [("QueueWait", 20.0)]),
    ("Queue", 1, []),
    ("Restricted", 10, [("Stop", 2.0)]),
    ("Shelf", 100, [("Pass", 1), ("Unattended", 90.0), ("Stop", 100.0)]),
])
def test_presence_in_a_zone(kind, seconds, expected):
    analyzer = ZoneAnalyzer([Zone(7, kind, SQUARE)])
    positions = [INSIDE] * (int(seconds / 0.5) + 1) + [OUTSIDE] * 4

    assert summary(walk(analyzer, positions)) == expected


def test_a_brief_flicker_outside_does_not_split_a_visit():
    analyzer = ZoneAnalyzer([Zone(7, "Queue", SQUARE)])

    events = walk(analyzer, [INSIDE] * 10 + [OUTSIDE] + [INSIDE] * 10 + [OUTSIDE] * 4)

    assert summary(events) == [("QueueWait", 10.0)]


def test_counting_line_counts_entries_and_exits_once():
    analyzer = ZoneAnalyzer([Zone(1, "CountingLine", [(0.1, 0.5), (0.9, 0.5)])])
    hesitation = [(0.5, 0.45), (0.5, 0.55), (0.5, 0.45), (0.5, 0.55)]

    events = walk(analyzer, hesitation + [(0.5, 0.55)] * 5 + [(0.5, 0.45)])

    assert [e.type for e in events] == ["Entry", "Exit"]


def test_changing_the_zones_keeps_unchanged_visits():
    queue = Zone(7, "Queue", SQUARE)
    analyzer = ZoneAnalyzer([queue])
    for i in range(10):
        analyzer.update([Person(1, INSIDE, INSIDE)], i * 0.5)

    analyzer.set_zones([queue, Zone(8, "Shelf", SQUARE)])

    assert summary(analyzer.close()) == [("QueueWait", 4.5)]


def test_tracker_keeps_one_number_per_person():
    tracker = Tracker(min_hits=3)
    ids, lost = [], []
    for i in range(8):
        x = i * 10.0
        tracks, gone = tracker.update([(x, 0, x + 50, 100), (400 - x, 0, 450 - x, 100)], i * 0.2)
        ids.append(sorted(t.id for t in tracks))
        lost += gone

    assert ids[:2] == [[], []]  # pas encore confirmées
    assert all(frame == [1, 2] for frame in ids[2:]) and lost == []
    assert tracker.update([], 10.0)[1] == [1, 2]


def test_overlapping_detections_are_merged():
    boxes = np.array([[0, 0, 10, 10], [1, 1, 11, 11], [50, 50, 60, 60]], dtype=float)

    assert suppress(boxes, np.array([0.8, 0.9, 0.5]), 0.5) == [1, 2]


def test_masks_and_blur_remove_details():
    noise = np.random.default_rng(1).integers(0, 255, (200, 200, 3), dtype=np.uint8)

    masked = apply_masks(noise, [[(0, 0), (0.5, 0), (0.5, 0.5), (0, 0.5)]])
    blurred = blur_people(noise, [(50, 50, 150, 150)])

    assert masked[:90, :90].max() == 0 and noise[:90, :90].max() > 0
    assert blurred[60:140, 60:140].std() < noise[60:140, 60:140].std() / 5
    assert np.array_equal(blurred[:20, :20], noise[:20, :20])


def test_background_removes_a_passing_person():
    empty = np.full((20, 20, 3), 100, dtype=np.uint8)
    frames = [empty.copy() for _ in range(5)]
    for i in range(5):
        frames[i][:, i * 4:i * 4 + 4] = 255

    assert np.array_equal(background(frames), empty)


def test_events_use_the_server_format():
    payload = event_payload(datetime(2026, 1, 2, 3, 4, 5, tzinfo=timezone.utc), 3, "Stop", 6.5, "c1-ab-2")

    assert payload == {"ts": "2026-01-02T03:04:05+00:00", "zoneId": 3, "type": "Stop", "value": 6.5,
                       "trackId": "c1-ab-2"}


def test_outbox_keeps_batches_until_the_server_accepts_them(tmp_path):
    outbox = Outbox(tmp_path)
    outbox.add([{"n": 1}])
    outbox.add([{"n": 2}])

    def offline(_):
        raise ServerError("coupure")

    assert not outbox.flush(offline)
    sent = []
    assert Outbox(tmp_path).flush(sent.extend)  # après redémarrage
    assert sent == [{"n": 1}, {"n": 2}] and outbox.pending() == []


def test_outbox_sets_aside_a_rejected_batch(tmp_path):
    outbox = Outbox(tmp_path)
    outbox.add([{"n": 1}])

    def refuse(_):
        raise ServerError("refusé", 400)

    assert outbox.flush(refuse)
    assert outbox.pending() == [] and len(list(tmp_path.glob("*.rejected"))) == 1


class FakeClient:
    def __init__(self, fail: int = 0):
        self.fail, self.images, self.sent = fail, [], []

    def events(self, events):
        self.sent += events

    def image(self, camera_id, jpeg):
        if self.fail:
            self.fail -= 1
            raise ServerError("coupure")
        self.images.append((camera_id, jpeg))
        return "date"


def test_a_photo_is_kept_until_the_server_accepts_it(tmp_path):
    client = FakeClient(fail=1)
    uplink = Uplink(client, Outbox(tmp_path))
    uplink.send_photo(3, b"jpeg")

    with pytest.raises(ServerError):
        uplink.exchange()
    uplink.exchange()
    uplink.exchange()

    assert client.images == [(3, b"jpeg")]
    assert uplink.inbox.get_nowait() == ("photo", (3, "date")) and uplink.inbox.empty()


def test_client_requires_a_web_address():
    with pytest.raises(ValueError):
        Client("file:///etc/passwd", "clé")
