# PharmaVision — Cahier de conception

Nov 27, 2026 · @AYOUB AOUINTI

## Le concept en bref

PharmaVision transforme les caméras de surveillance déjà installées dans une pharmacie en capteurs commerciaux. L'application web affiche en temps réel, avec historique, l'état de chaque linéaire, zone et famille de médicaments : remplissage, ruptures, attention des clients, attente au comptoir.

- **Pour qui** : pharmaciens titulaires, groupements de pharmacies, laboratoires (rapports de visibilité).
- **Promesse** : moins de ruptures, plus de ventes conseil, des décisions de merchandising basées sur des données.
- **Principe technique** : un boîtier local analyse les flux RTSP/ONVIF ; seuls des événements anonymes partent vers le serveur, jamais la vidéo.
- **Différence avec le marché** : les offres existantes sont soit généralistes (comptage de personnes), soit des solutions d'entreprise coûteuses. Aucune n'est pensée pour l'officine au Maghreb.

## Fonctionnalités

Sept modules, du plus simple à livrer (1) au plus avancé (7).

| Module | Ce qu'il mesure | Ce que l'équipe fait |
| --- | --- | --- |
| 1. Fréquentation | Entrées/sorties, visiteurs par heure, occupation | Adapter le planning du personnel |
| 2. Comptoir | Longueur de file, temps d'attente, comptoirs inoccupés | Ouvrir un 2e poste, alerte mobile |
| 3. Carte de chaleur | Temps de présence par zone, parcours, zones mortes | Déplacer une tête de gondole, réaménager |
| 4. Linéaires | Taux de remplissage, facings vides, ruptures, produits hors planogramme | Réassortir, replacer le produit |
| 5. Engagement produit | Arrêts devant un rayon (> 5 s), prises en main | Mesurer l'intérêt par famille et par marque |
| 6. Conseil | Client arrêté longtemps sans conseiller (dermo, bébé) | Envoyer un conseiller, vente additionnelle |
| 7. Croisement caisse | Prises en main ÷ tickets par rayon, effet des promotions | Décider des promos, négocier avec les labos |

**Fonctions transverses**

- Historique et replay : chaque alerte garde une vignette de l'instant (personnes floutées) et la courbe de la zone.
- Rapports PDF/Excel par période, par zone, par marque ; envoi automatique hebdomadaire.
- Rapports laboratoires : preuve de visibilité et de respect du planogramme, vendable comme service.
- Multi-officines : comparaison entre pharmacies pour les groupements.
- Application mobile (ou notifications WhatsApp/Telegram) pour les alertes à l'équipe.
- Intégration avec le logiciel de gestion d'officine (stock, ventes) par import CSV puis API.

## Design de l'interface

La maquette interactive comporte deux écrans : [ouvrir la maquette PharmaVision](https://claude.ai/artifact/BuCG9pEvGELX4AjYWNsryr). Les chiffres affichés sont fictifs.

### Écran 1 — Tableau de bord temps réel

1. **Menu latéral sombre** : Tableau de bord, Caméras, Zones & linéaires, Produits & marques, Alertes (badge du nombre), Historique & replay, Rapports, Paramètres ; en bas, l'état du boîtier local.
2. **En-tête** : nom de la pharmacie, date, sélecteur de période (Direct / Aujourd'hui / 7 jours / 30 jours), voyant « En direct », bouton Exporter.
3. **5 indicateurs clés** : visiteurs du jour, taux de conversion, attente moyenne au comptoir, linéaires en rupture, taux d'engagement linéaire ; chacun avec sa tendance.
4. **Flux caméras** : vignettes avec les zones dessinées en surimpression et le nombre de personnes.
5. **Carte de chaleur du plan** : une case par zone, couleur selon le temps de présence, légende faible → forte.
6. **Tableau « État des linéaires »** : remplissage (barre + %), arrêts clients, taux de prise en main, étiquette Conforme / Stock bas / Rupture.
7. **Alertes & interventions** : chaque alerte a un bouton d'action (Réassortir, Notifier, Voir l'image, Envoyer, Diagnostiquer).
8. **Historique horaire** : barres visiteurs vs tickets de caisse de 8 h à 20 h.

### Écran 2 — Caméras & dessin des zones

1. **Liste des caméras** : nom, marque, protocole, nombre de zones, voyant en ligne / hors ligne / non configurée ; boutons « Découvrir (ONVIF) » et « Ajouter une caméra ».
2. **Image de la caméra** avec barre d'outils : Rectangle, Polygone, Ligne de comptage, Masque de confidentialité, Photo de référence.
3. **Propriétés de la zone** : nom, type (linéaire, passage, file d'attente, ligne de comptage, zone réglementée), catégorie et marques, planogramme de référence, règles d'alerte (seuil de remplissage, délai sans conseil, hors planogramme, notification mobile).

### Charte graphique

| Élément | Valeur |
| --- | --- |
| Fond | #F3F6F5 (gris-vert très clair), cartes blanches, bord #E1E8E5 |
| Texte / menu | #0F1A17 (vert presque noir) |
| Couleur principale | #12A06E / #0B6E4C (vert pharmacie) |
| Alerte moyenne | #E8792B (orange) |
| Alerte forte | #C9541A (rouge-orange) |
| Information | #3D8FD1 et #8EC5E8 (bleu) |
| Typographie | Space Grotesk (titres), Inter (texte) |
| Rayons | 8 px boutons, 14–16 px cartes |

Composants React conseillés : shadcn/ui ou Ant Design pour les tableaux et formulaires, Recharts ou ECharts pour les graphiques, Konva.js ou Fabric.js pour dessiner les zones sur l'image.

## Architecture technique

Trois niveaux : un boîtier local par pharmacie, un serveur central .NET, et les interfaces web et mobile. Cette stack correspond à vos compétences React / .NET ; seule la partie IA du boîtier est en Python.

&#91;embedded content: architecture PharmaVision · 3 niveaux\]

La vidéo ne dépasse jamais le boîtier ; le serveur ne reçoit que des événements horodatés par zone.

| Couche | Technologies proposées |
| --- | --- |
| Ingestion vidéo | go2rtc ou FFmpeg, découverte ONVIF (python-onvif-zeep ou Onvif.Core en .NET) |
| IA sur le boîtier | Python, YOLO (ou RF-DETR), ByteTrack, Supervision (zones, temps dans la zone), OpenVINO / TensorRT |
| Transport | MQTT (Mosquitto ou EMQX) avec TLS, ou HTTPS en lot ; file locale si Internet coupe |
| Backend | ASP.NET Core, SignalR (temps réel), Hangfire ou Quartz (tâches), EF Core |
| Données | PostgreSQL + extension TimescaleDB (séries temporelles et configuration dans la même base), stockage objet pour vignettes |
| Frontend | React + TypeScript, Recharts/ECharts, Konva.js pour dessiner les zones, flux live via WebRTC (go2rtc) |
| Mobile | React Native ou PWA, notifications push ; WhatsApp Business API en option |
| Déploiement | Docker sur le boîtier, mise à jour à distance ; serveur sur VPS ou Azure |

## Modèle de données et indicateurs

Le boîtier local n'envoie que des événements horodatés ; tous les indicateurs sont calculés côté serveur à partir de ces événements.

### Entités principales

| Entité | Champs clés |
| --- | --- |
| Pharmacie | id, nom, ville, groupement, fuseau horaire |
| Boîtier (EdgeDevice) | id, pharmacieId, version, dernier signal |
| Caméra | id, boitierId, url RTSP, marque, résolution, statut |
| Zone | id, caméraId, type, polygone (JSON), catégorie, marques, planogramme |
| Règle d'alerte | zoneId, indicateur, seuil, durée, canal de notification |
| Événement | horodatage, zoneId, type (entrée, sortie, arrêt, prise, remplissage), valeur, trackId anonyme |
| Alerte | id, règleId, début, fin, vignette, statut (ouverte, prise en charge, résolue), utilisateur |
| Vente (import caisse) | horodatage, code produit, famille, quantité, montant |

### Formules des indicateurs

- **Taux de conversion** = tickets de caisse ÷ visiteurs entrés, sur la même période.
- **Taux de remplissage d'un linéaire** = surface de produits détectée ÷ surface de la zone (ou facings occupés ÷ facings du planogramme).
- **Taux d'engagement** = personnes arrêtées plus de 5 s devant la zone ÷ personnes passées devant.
- **Taux de prise en main** = interactions main-produit ÷ arrêts.
- **Transformation par rayon** = ventes de la famille ÷ prises en main de la zone.
- **Temps d'attente** = moyenne du temps passé dans la zone « file d'attente » avant d'atteindre le comptoir.
- **Durée de rupture** = temps cumulé où le remplissage reste sous le seuil.

## Interventions nécessaires sur site

L'installation se fait en une demi-journée si les caméras existantes suffisent ; il faut compter une journée si des caméras de rayon doivent être ajoutées.

1. **Audit** : lister les caméras et le DVR/NVR (marque, modèle, accès RTSP/ONVIF, identifiants), photographier chaque champ de vision, relever le plan de la pharmacie.
2. **Positionnement** : les caméras de plafond grand angle conviennent à la fréquentation, au comptoir et à la carte de chaleur. Pour lire un linéaire, une caméra doit faire face au rayon (à 2–3 m, légèrement en plongée). Ajouter une caméra IP 4 MP par rayon stratégique (dermo, bébé, solaires).
3. **Boîtier local** : mini-PC (Intel N100 + OpenVINO) pour 4 à 8 flux en détection de personnes, ou NVIDIA Jetson Orin / boîtier Hailo-8 pour plus de flux et l'analyse des linéaires. Onduleur conseillé.
4. **Réseau** : boîtier sur le même réseau que le NVR ; sortie Internet sortante uniquement (HTTPS/MQTT chiffré), aucun port ouvert vers l'extérieur.
5. **Configuration** : découverte ONVIF, dessin des zones dans l'écran 2, prise des photos de référence des rayons pleins, réglage des seuils.
6. **Calibration** : comparer pendant une semaine le comptage automatique à un comptage manuel et aux tickets de caisse ; ajuster.
7. **Formation et affichage** : 1 h de formation de l'équipe, affiche d'information des clients sur la vidéo-analyse.

## Applications similaires du marché

iFactory est le concurrent le plus proche : il cible précisément les linéaires de pharmacie avec les caméras existantes. Les autres couvrent une partie du besoin.

| Solution | Ce qu'elle fait | Lien avec PharmaVision |
| --- | --- | --- |
| [iFactory — AI Vision Pharmacy](https://ifactoryapp.com/ai-vision-camera/ai-vision-camera-pharmacy-health-retail-shelf-compliance) | Audit continu des rayons : disponibilité des médicaments, planogramme, dates de péremption visibles | Concurrent direct (linéaires) |
| [Roboflow Shelf Monitoring](https://roboflow.com/ai/shelf-monitoring) | Ruptures, stock bas, produits mal placés, facings, prix | Modèles et outils pour le module 4 |
| [Roboflow Planogram Compliance](https://roboflow.com/ai/planogram-compliance) | Comparaison rayon / planogramme, score de conformité | Module 4 |
| [Scandit Shelf Intelligence](https://www.scandit.com/blog/shelf-intelligence-fundamentals/) | Analyse des rayons par téléphone, caméras fixes ou robots | Alternative mobile |
| [ParallelDots (pharmacie)](https://www.paralleldots.com/resources/blog/ai-retail-pharmacy-innovation-impact) | Suivi de visibilité des marques en pharmacie pour les laboratoires | Modèle « rapports labos » |
| [Trassir — pharmacies](https://trassir.com/news/preventing_theft_of_high_value_medicines_in_pharmacies/) | Surveillance des médicaments coûteux, lien vidéo ↔ stock | Sécurité, zones réglementées |
| [Axis — pharmacies](https://www.axis.com/blog/secure-insights/?p=7139) | Files d'attente, alertes de stock bas, sécurité | Caméras + analyses embarquées |
| [BriefCam](https://www.briefcam.com/resources/blog/using-video-content-analytics-for-loss-prevention-in-pharmacies/) | Analyse de contenu vidéo, recherche dans l'historique | Module historique / replay |
| [Agent DVR (iSpy)](https://www.ispyconnect.com/analytics) | Comptage, carte de chaleur, files, sur PC local, toute caméra IP/ONVIF/RTSP | Modules 1 à 3 |
| [Camlytics](https://camlytics.com/solutions/people-counting) | Comptage et heatmap locaux sur caméras existantes | Modules 1 et 3 |
| [FootfallCam](https://www.footfallcam.com/Industries/RetailEnterprise) | Comptage, heatmap, conversion des ventes | Modules 1, 3, 7 |
| [V-Count](https://v-count.com/store-zone-analytics-heatmaps/) | Analyse par zone, temps de présence, flux de visiteurs | Modules 3 et 5 |
| [Sciforce — étude de cas](https://web-dev.sciforce.solutions/case-studies/pharmacy-staffing-computer-vision) | Vision par zones en pharmacie pour le personnel au comptoir | Modules 2 et 6 |

## Codes sources open source réutilisables

Aucune application complète pour pharmacie n'est publiée en open source, mais chaque brique existe. Attention aux licences : Ultralytics est en AGPL-3.0 (licence commerciale à acheter pour un produit fermé) et SKU-110K est réservé à un usage académique.

### Applications complètes à étudier

| Projet | Ce qu'il apporte | Licence |
| --- | --- | --- |
| [Frigate NVR](https://github.com/blakeblackshear/frigate) | NVR local avec détection IA sur caméras IP, éditeur de zones et de masques, MQTT, interface React/TypeScript | MIT |
| [yolo11-grafana-influxdb (Seeed)](https://github.com/bleaaach/yolo11-grafana-influxdb) | Carte de chaleur et fréquentation : caméras IP + Jetson, InfluxDB, Grafana ([guide](https://wiki.seeedstudio.com/solutions/retail-heatmap-grafana/)) | voir dépôt |
| [reCamera-with-Heatmap (Seeed)](https://github.com/xr686/reCamera-with-Heatmap) | Même chose sur caméra IA intégrée | voir dépôt |
| [Intel smart-retail-analytics](https://github.com/intel-iot-devkit/smart-retail-analytics) | Comptage, engagement client et suivi d'inventaire avec OpenVINO + Grafana | voir dépôt (projet de référence ancien) |
| [Intel store-aisle-monitor-python](https://github.com/intel-iot-devkit/store-aisle-monitor-python) | Carte de chaleur d'allée avec modèle « person-detection-retail » | voir dépôt |
| [Retail Shelf Monitoring (alijanloo)](https://alijanloo.github.io/projects/Retail-Shelf-Monitoring) | Ruptures et produits mal placés depuis la vidéosurveillance, planogramme, YOLO + FAISS | non précisée |
| [shelf\_management (rokopi-byte)](https://github.com/rokopi-byte/shelf_management) | Code et données d'un article scientifique : détection de produits et de rangées d'étagères | voir dépôt |
| [Planogram Dataset (akul-bharadwaj)](https://github.com/akul-bharadwaj/Computer-Vision-Assessment-Planogram-Dataset) | Application Gradio de score de conformité + jeu de données YOLOv8 | voir dépôt |
| [Shopping-Analytics (saimj7)](https://github.com/saimj7/Shopping-Analytics) | Analyse du comportement d'achat (expérimental, non maintenu) | voir dépôt |
| [Retail-Shelf-Inventory-Monitoring](https://github.com/Indhu198/Retail-Shelf-Inventory-Monitoring) | Comptage d'articles sur une étagère | voir dépôt |

### Bibliothèques et briques techniques

| Brique | Rôle dans PharmaVision | Licence |
| --- | --- | --- |
| [Ultralytics YOLO](https://github.com/ultralytics/ultralytics) | Détection, suivi, et [solutions prêtes](https://docs.ultralytics.com/solutions) : comptage, comptage par région, carte de chaleur, gestion de file | AGPL-3.0 / commerciale |
| [Roboflow Supervision](https://github.com/roboflow/supervision) | Zones polygonales, comptage entrées/sorties, [temps dans la zone](https://github.com/roboflow/supervision/tree/develop/examples/time_in_zone) sur flux RTSP, heatmap | MIT |
| [go2rtc](https://github.com/AlexxIT/go2rtc) | Passerelle vidéo : RTSP/ONVIF → WebRTC/MSE pour afficher les caméras dans le navigateur | MIT |
| [ByteTrack](https://github.com/ifzhang/ByteTrack) | Suivi multi-personnes (ID anonymes stables) | MIT |
| [OpenVINO](https://github.com/openvinotoolkit/openvino) | Accélération de l'inférence sur mini-PC Intel | Apache-2.0 |
| [SKU110K\_CVPR19](https://github.com/eg4000/SKU110K_CVPR19) | Jeu de données de rayons très denses ([doc Ultralytics](https://docs.ultralytics.com/datasets/detect/sku-110k)) | Académique, non commercial |

**Conseil** : décision prise : acheter la licence commerciale Ultralytics (Enterprise) pour vendre PharmaVision en code fermé, sans obligation de publier le code. Garder Supervision (MIT) pour les zones et le temps dans la zone. Entraîner le modèle de linéaires sur vos propres photos de pharmacies, car SKU-110K est interdit en usage commercial. Prévoir le coût de la licence (sur devis) dans le budget du pilote.

## Conformité et vie privée

Le produit doit être conçu « sans identification » dès le départ : c'est un argument de vente auprès des pharmaciens autant qu'une obligation.

- **Aucune reconnaissance faciale**, aucune donnée biométrique ; les personnes ne sont que des points anonymes avec un identifiant temporaire.
- **Traitement local** : la vidéo ne quitte pas la pharmacie ; seuls des chiffres et des événements partent vers le serveur.
- **Floutage automatique** des personnes sur les vignettes d'alerte ; masques de confidentialité sur le comptoir d'ordonnances et la zone de conseil.
- **Durée de conservation** limitée et paramétrable (par exemple 30 jours pour les vignettes, 24 mois pour les statistiques agrégées).
- **Information des clients** : affichage à l'entrée.
- **Tunisie** : la vidéosurveillance et le traitement de données personnelles relèvent de l'INPDP (Instance nationale de protection des données personnelles). Faire valider les démarches (déclaration ou autorisation) par un juriste avant le pilote.
- **Export** (France, UE) : le RGPD exige une analyse d'impact pour ce type de traitement.

## Feuille de route

Commencer par ce que les caméras existantes savent déjà faire (fréquentation, comptoir, chaleur), puis ajouter les linéaires pendant le pilote. Les durées et les seuils des portes sont des propositions à ajuster.

&#91;embedded content: feuille de route · 3 phases, 2 portes de décision\]

Chaque phase ne démarre que si la porte précédente est franchie.

## Sources

- [Seeed Studio — Retail People-Flow Heatmap](https://wiki.seeedstudio.com/solutions/retail-heatmap-grafana/)
- [Ultralytics Solutions](https://docs.ultralytics.com/solutions)
- [Ultralytics — jeu de données SKU-110K](https://docs.ultralytics.com/datasets/detect/sku-110k)
- [Retail Shelf Monitoring (alijanloo)](https://alijanloo.github.io/projects/Retail-Shelf-Monitoring)
- [Axis — solutions pour pharmacies](https://www.axis.com/blog/secure-insights/?p=7139)
- [iFactory — AI Vision Camera for Pharmacy](https://ifactoryapp.com/ai-vision-camera/ai-vision-camera-pharmacy-health-retail-shelf-compliance)
