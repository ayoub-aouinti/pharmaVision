"""Flux vidéo : fichier lu à sa vitesse réelle, ou flux RTSP dont on garde la dernière image."""

import threading
import time

import cv2
import numpy as np

RECONNECT_SECONDS = 5


class Source:
    def __init__(self, location: str, loop: bool = False):
        self.location, self.loop = location, loop
        self.live = "://" in location
        self.finished = False
        self._frame: np.ndarray | None = None
        self._number = 0
        self._read = 0
        self._lock = threading.Lock()
        self._stop = threading.Event()
        if not self.live and not self._open().isOpened():
            raise FileNotFoundError(f"Vidéo illisible : {location}")
        self._thread = threading.Thread(target=self._run, daemon=True)
        self._thread.start()

    def _open(self) -> cv2.VideoCapture:
        return cv2.VideoCapture(self.location)

    def _run(self) -> None:
        while not self._stop.is_set():
            capture = self._open()
            period = 0 if self.live else 1 / (capture.get(cv2.CAP_PROP_FPS) or 25)
            next_at = time.monotonic()
            while not self._stop.is_set():
                ok, frame = capture.read()
                if not ok:
                    break
                with self._lock:
                    self._frame, self._number = frame, self._number + 1
                if period:
                    next_at += period
                    self._stop.wait(max(0, next_at - time.monotonic()))
            capture.release()
            if self.live:
                self._stop.wait(RECONNECT_SECONDS)
            elif not self.loop:
                break
        self.finished = True

    def read(self) -> np.ndarray | None:
        """Dernière image non encore lue, ou None."""
        with self._lock:
            if self._number == self._read:
                return None
            self._read = self._number
            return self._frame

    def close(self) -> None:
        self._stop.set()
        self._thread.join(timeout=5)
