"""Détection de personnes avec YOLOX (licence Apache 2.0) exécuté par ONNX Runtime."""

import cv2
import numpy as np
import onnxruntime as ort

Box = tuple[float, float, float, float]
STRIDES = (8, 16, 32)
PERSON = 0  # première classe COCO


def suppress(boxes: np.ndarray, scores: np.ndarray, threshold: float) -> list[int]:
    """Supprime les boîtes qui recouvrent trop une boîte mieux notée."""
    order = scores.argsort()[::-1]
    areas = (boxes[:, 2] - boxes[:, 0]) * (boxes[:, 3] - boxes[:, 1])
    kept: list[int] = []
    while order.size:
        best, rest = order[0], order[1:]
        kept.append(int(best))
        width = np.minimum(boxes[best, 2], boxes[rest, 2]) - np.maximum(boxes[best, 0], boxes[rest, 0])
        height = np.minimum(boxes[best, 3], boxes[rest, 3]) - np.maximum(boxes[best, 1], boxes[rest, 1])
        shared = np.clip(width, 0, None) * np.clip(height, 0, None)
        order = rest[shared / (areas[best] + areas[rest] - shared) <= threshold]
    return kept


class PersonDetector:
    def __init__(self, model_path: str, score: float = 0.45, overlap: float = 0.5):
        self.session = ort.InferenceSession(model_path, providers=ort.get_available_providers())
        entry = self.session.get_inputs()[0]
        self.input_name = entry.name
        self.height, self.width = int(entry.shape[2]), int(entry.shape[3])
        self.score, self.overlap = score, overlap
        grids, strides = [], []
        for stride in STRIDES:
            x, y = np.meshgrid(np.arange(self.width // stride), np.arange(self.height // stride))
            grids.append(np.stack((x, y), 2).reshape(-1, 2))
            strides.append(np.full((grids[-1].shape[0], 1), stride))
        self.grid, self.stride = np.concatenate(grids), np.concatenate(strides)

    def detect(self, frame: np.ndarray) -> list[Box]:
        """Boîtes des personnes en pixels de l'image reçue."""
        scale = min(self.height / frame.shape[0], self.width / frame.shape[1])
        resized = cv2.resize(frame, (int(frame.shape[1] * scale), int(frame.shape[0] * scale)))
        # Image complétée en gris, comme à l'entraînement ; ni normalisation ni inversion des couleurs.
        canvas = np.full((self.height, self.width, 3), 114, dtype=np.uint8)
        canvas[: resized.shape[0], : resized.shape[1]] = resized
        blob = canvas.transpose(2, 0, 1)[None].astype(np.float32)

        raw = self.session.run(None, {self.input_name: blob})[0][0]
        scores = raw[:, 4] * raw[:, 5 + PERSON]
        keep = scores >= self.score
        if not keep.any():
            return []
        centers = (raw[keep, :2] + self.grid[keep]) * self.stride[keep]
        sizes = np.exp(raw[keep, 2:4]) * self.stride[keep]
        boxes = np.concatenate((centers - sizes / 2, centers + sizes / 2), 1) / scale
        limits = [frame.shape[1], frame.shape[0]] * 2
        boxes = np.clip(boxes, 0, limits)
        return [tuple(float(v) for v in boxes[i]) for i in suppress(boxes, scores[keep], self.overlap)]
