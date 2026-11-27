# Boîtier local PharmaVision (Python)

Lit des caméras (RTSP ou fichiers vidéo), détecte les personnes sur place et n'envoie au serveur que des **événements anonymes** et une image de référence. La vidéo ne quitte pas la machine.

## Ce qui est mesuré

| Zone | Événements envoyés |
| --- | --- |
| Ligne de comptage | `Entry`, `Exit` au passage des pieds sur la ligne |
| Passage | `Pass` à l'entrée dans la zone, `Stop` après plus de 5 s |
| Linéaire | `Pass`, `Stop`, `Unattended` après 90 s |
| File d'attente | `QueueWait` (durée passée dans la zone, à partir de 3 s) |
| Zone réglementée | `Stop` après 2 s de présence |
| Masque de confidentialité | Partie noircie avant toute analyse |

**Non mesurés** : remplissage des linéaires, prises en main, tickets de caisse. Dans l'application, un linéaire suivi par ce boîtier apparaît « Sans mesure ». « Client sans conseil » signifie seulement qu'une personne reste longtemps devant le rayon : le personnel n'est pas distingué des clients. Un « arrêt » est une présence de plus de 5 s dans la zone, immobile ou non.

## Installation

Python 3.11 ou plus.

```powershell
cd edge
python -m venv .venv
.venv\Scripts\activate          # Linux : source .venv/bin/activate
pip install -r requirements.txt
python download_model.py        # YOLOX-tiny, 20 Mo, empreinte vérifiée
python -m pytest -q
```

## Brancher une vidéo

1. Dans l'application (titulaire) : **Paramètres › Boîtiers**, ajouter un boîtier et copier sa clé.
2. **Caméras › Ajouter une caméra**, en la rattachant à ce boîtier. Pour un fichier vidéo, l'URL RTSP peut être quelconque.
3. Lancer le boîtier ; l'identifiant de la caméra est celui affiché au démarrage s'il est erroné.

```powershell
$env:PHARMAVISION_KEY = "pv_…"
python -m pharmavision_edge --camera 19=C:\videos\magasin.mp4 --loop --preview data\preview
```

4. L'image de référence arrive après une quinzaine de secondes : dessiner alors les zones dans l'écran Caméras. Le boîtier les prend en compte en 10 secondes.

| Option | Rôle |
| --- | --- |
| `--server` | Adresse de l'API (`http://127.0.0.1:5080` par défaut ; HTTPS obligatoire hors du poste local) |
| `--camera ID[=SOURCE]` | Répétable ; sans source, l'URL RTSP enregistrée est utilisée. Sans l'option, toutes les caméras du boîtier |
| `--key-file` | Fichier contenant la clé, à la place de la variable d'environnement |
| `--fps` | Images analysées par seconde et par caméra (5 par défaut) |
| `--score` | Confiance minimale d'une détection (0,45 par défaut) |
| `--loop`, `--duration` | Rejouer un fichier, limiter la durée |
| `--preview DOSSIER` | Vidéo de contrôle avec zones, numéros et personnes floutées |
| `--no-image` | Ne transmettre aucune image |

Une ligne de comptage tracée **de gauche à droite** compte une entrée quand on la franchit vers le bas de l'image ; la tracer dans l'autre sens inverse les directions.

## Vie privée

- L'image de référence est la médiane d'images prises sur une quinzaine de secondes (les passants disparaissent), puis les personnes encore détectées sont floutées et les masques noircis.
- Le floutage dépend de la détection : **une personne immobile non détectée peut rester visible**. Vérifier l'image avant d'équiper une officine, et poser des masques sur les zones sensibles.
- La vidéo de contrôle (`--preview`) reste sur le boîtier ; les personnes non détectées y sont visibles.
- Les identifiants de suivi sont temporaires et changent à chaque démarrage.

## Fonctionnement hors connexion

Les événements sont écrits dans `data/outbox-N/` puis envoyés dans l'ordre. En cas de coupure ils sont conservés et renvoyés, y compris après un redémarrage. L'analyse continue pendant ce temps.

## Limites connues

- Détection sur processeur : environ 50 ms par image sur le poste de développement, soit quelques caméras à 5 images par seconde. Pour la carte graphique ou OpenVINO, installer la variante d'ONNX Runtime correspondante.
- Suivi simple par recouvrement : une personne masquée plus d'1,5 s reçoit un nouveau numéro et peut être comptée deux fois.
- L'état en ligne / hors ligne d'une caméra reste réglé à la main dans l'application.
- Réglages à valider sur des vidéos de l'officine : aucun essai n'a encore porté sur des images de pharmacie.

## Licences

[YOLOX](https://github.com/Megvii-BaseDetection/YOLOX) et son modèle sont sous licence Apache 2.0, utilisables dans un produit fermé. Le cahier de conception prévoit Ultralytics avec une licence commerciale : une fois achetée, seul `pharmavision_edge/detector.py` est à remplacer.
