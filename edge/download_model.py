"""Télécharge le modèle YOLOX-tiny officiel et vérifie son empreinte."""

import hashlib
import sys
import urllib.request
from pathlib import Path

URL = "https://github.com/Megvii-BaseDetection/YOLOX/releases/download/0.1.1rc0/yolox_tiny.onnx"
SHA256 = "427cc366d34e27ff7a03e2899b5e3671425c262ea2291f88bb942bc1cc70b0f7"
TARGET = Path(__file__).resolve().parent / "models" / "yolox_tiny.onnx"


def main() -> int:
    if TARGET.is_file() and hashlib.sha256(TARGET.read_bytes()).hexdigest() == SHA256:
        print(f"Modèle déjà présent : {TARGET}")
        return 0
    print(f"Téléchargement de {URL}")
    with urllib.request.urlopen(URL, timeout=120) as response:
        data = response.read()
    if hashlib.sha256(data).hexdigest() != SHA256:
        print("Empreinte inattendue : fichier rejeté.", file=sys.stderr)
        return 1
    TARGET.parent.mkdir(exist_ok=True)
    TARGET.write_bytes(data)
    print(f"Modèle enregistré : {TARGET}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
