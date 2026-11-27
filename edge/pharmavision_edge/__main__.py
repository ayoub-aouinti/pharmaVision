"""
Boîtier local : python -m pharmavision_edge --camera 7=video.mp4

La clé du boîtier se donne par la variable PHARMAVISION_KEY ou par --key-file,
jamais en argument (elle resterait dans l'historique des commandes).
"""

import argparse
import logging
import os
import sys
from pathlib import Path

from .client import Client, ServerError
from .detector import PersonDetector
from .outbox import Outbox
from .runner import CameraWorker, run, wait_for_config
from .source import Source

ROOT = Path(__file__).resolve().parent.parent


def parse_camera(value: str) -> tuple[int, str]:
    number, separator, location = value.partition("=")
    if not number.isdigit() or (separator and not location):
        raise argparse.ArgumentTypeError("Format attendu : IDENTIFIANT ou IDENTIFIANT=vidéo_ou_rtsp")
    return int(number), location


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(prog="pharmavision_edge", description="Boîtier local PharmaVision")
    parser.add_argument("--server", default="http://127.0.0.1:5080")
    parser.add_argument("--key-file", type=Path, help="Fichier contenant la clé du boîtier")
    parser.add_argument("--camera", action="append", type=parse_camera, default=[], metavar="ID[=SOURCE]",
                        help="Caméra à analyser ; sans SOURCE, son adresse RTSP enregistrée. Toutes par défaut.")
    parser.add_argument("--model", type=Path, default=ROOT / "models" / "yolox_tiny.onnx")
    parser.add_argument("--fps", type=float, default=5, help="Images analysées par seconde et par caméra")
    parser.add_argument("--score", type=float, default=0.45, help="Confiance minimale d'une détection")
    parser.add_argument("--duration", type=float, help="Arrêt après ce nombre de secondes")
    parser.add_argument("--loop", action="store_true", help="Rejoue les fichiers vidéo en boucle")
    parser.add_argument("--no-image", action="store_true", help="N'envoie aucune image de référence")
    parser.add_argument("--preview", type=Path, help="Dossier des vidéos de contrôle (personnes floutées)")
    parser.add_argument("--data", type=Path, default=ROOT / "data", help="File d'attente locale")
    args = parser.parse_args(argv)
    logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s")
    log = logging.getLogger("pharmavision_edge")

    key = args.key_file.read_text(encoding="utf-8").strip() if args.key_file else os.environ.get("PHARMAVISION_KEY", "")
    if not key:
        parser.error("Clé manquante : variable PHARMAVISION_KEY ou option --key-file")
    if not args.model.is_file():
        parser.error(f"Modèle absent : {args.model} (lancez « python download_model.py »)")
    if not 0 < args.fps <= 30:
        parser.error("--fps doit être compris entre 0 et 30")

    client = Client(args.server, key)
    try:
        config = wait_for_config(client)
    except ServerError as error:
        log.error("%s : clé refusée", error)
        return 1
    except KeyboardInterrupt:
        return 0

    cameras = {c["id"]: c for c in config["cameras"]}
    wanted = args.camera or [(number, "") for number in cameras]
    unknown = [number for number, _ in wanted if number not in cameras]
    if unknown or not wanted:
        listing = ", ".join(f"{c['id']} ({c['name']})" for c in cameras.values()) or "aucune"
        log.error("Caméra(s) %s non rattachée(s) à ce boîtier ou hors ligne. Disponibles : %s", unknown, listing)
        return 1

    detector = PersonDetector(str(args.model), score=args.score)
    if args.preview:
        args.preview.mkdir(parents=True, exist_ok=True)
    try:
        workers = [
            CameraWorker(number, Source(location or cameras[number]["rtspUrl"], args.loop), detector,
                         str(args.preview / f"camera-{number}.mp4") if args.preview else None, args.fps)
            for number, location in wanted
        ]
    except FileNotFoundError as error:
        log.error("%s", error)
        return 1
    log.info("Analyse de %s caméra(s) vers %s", len(workers), args.server)
    # Une file par boîtier : un lot ne doit pas partir avec la clé d'un autre.
    outbox = Outbox(args.data / f"outbox-{config['deviceId']}")
    run(client, workers, outbox, config, args.fps, args.duration, not args.no_image)
    return 0


if __name__ == "__main__":
    sys.exit(main())
