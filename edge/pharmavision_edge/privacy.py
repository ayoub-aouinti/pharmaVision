"""Masques de confidentialité et floutage : appliqués avant toute analyse ou sortie d'image."""

import cv2
import numpy as np

from .geometry import Point

Box = tuple[float, float, float, float]
MARGIN = 0.15  # marge autour d'une personne détectée


def apply_masks(frame: np.ndarray, masks: list[list[Point]]) -> np.ndarray:
    """Noircit les zones masquées (copie de l'image)."""
    if not masks:
        return frame
    masked = frame.copy()
    height, width = frame.shape[:2]
    polygons = [np.array([(x * width, y * height) for x, y in mask], dtype=np.int32) for mask in masks]
    cv2.fillPoly(masked, polygons, (0, 0, 0))
    return masked


def blur_people(frame: np.ndarray, boxes: list[Box]) -> np.ndarray:
    """Rend méconnaissable chaque personne : réduction à quelques pixels puis flou."""
    blurred = frame.copy()
    height, width = frame.shape[:2]
    for x1, y1, x2, y2 in boxes:
        mx, my = (x2 - x1) * MARGIN, (y2 - y1) * MARGIN
        left, top = max(0, int(x1 - mx)), max(0, int(y1 - my))
        right, bottom = min(width, int(x2 + mx) + 1), min(height, int(y2 + my) + 1)
        if right <= left or bottom <= top:
            continue
        region = blurred[top:bottom, left:right]
        tiny = cv2.resize(region, (6, 10), interpolation=cv2.INTER_AREA)
        smooth = cv2.resize(tiny, (region.shape[1], region.shape[0]), interpolation=cv2.INTER_LINEAR)
        blurred[top:bottom, left:right] = cv2.GaussianBlur(smooth, (0, 0), 3)
    return blurred


def background(frames: list[np.ndarray]) -> np.ndarray:
    """Médiane d'images espacées dans le temps : les personnes en mouvement disparaissent."""
    return np.median(np.stack(frames), axis=0).astype(np.uint8)
