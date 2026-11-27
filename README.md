# PharmaVision

Analyse commerciale d'officine à partir des caméras existantes. **Les données de démonstration sont fictives.** Un [boîtier Python](edge/README.md) permet de brancher de vraies vidéos : il détecte les personnes sur place et n'envoie que des événements anonymes.

Documents de conception : [docs/](docs/).

## Contenu

| Dossier | Rôle |
| --- | --- |
| `backend/src/PharmaVision.Domain` | Entités, contrat boîtier ↔ serveur, formules des indicateurs, générateur de visites |
| `backend/src/PharmaVision.Infrastructure` | EF Core, migrations, hypertable TimescaleDB, données fictives initiales |
| `backend/src/PharmaVision.Api` | API REST, ingestion, SignalR, moteur d'alertes |
| `backend/src/PharmaVision.Simulator` | Boîtier local simulé (même API que le boîtier réel) |
| `edge` | Boîtier local Python : détection de personnes sur vidéo ou flux RTSP |
| `frontend` | React + TypeScript : connexion, tableau de bord, Caméras, Zones & linéaires, Produits & marques, Alertes, Historique & replay, Rapports, Paramètres |

## Lancer en développement

Prérequis : Docker, SDK .NET 10, Node 20.19 ou plus.

```bash
docker compose up -d db                                      # PostgreSQL + TimescaleDB, port 5450
dotnet run --project backend/src/PharmaVision.Api            # http://localhost:5080/swagger
dotnet run --project backend/src/PharmaVision.Simulator      # ajouter « -- --speed 10 » pour accélérer
cd frontend && npm install && npm run dev                    # http://localhost:5175
```

Chaque commande `dotnet run` et `npm run dev` occupe son propre terminal.
Au premier démarrage, l'API crée le schéma puis génère 60 jours d'historique (30 affichés, 30 pour les tendances).

Tout en conteneurs : `docker compose --profile app up --build`, puis http://localhost:5175.

## Le but : le placement

PharmaVision détermine quels rayons et quels médicaments sont chauds ou froids et propose quoi déplacer. L'écran « Chaud & froid » donne :

- la **température** de chaque rayon : moitié contacts avec les produits (caméras), moitié unités vendues (caisse), rapportés au meilleur rayon de l'officine ;
- les **rayons sans contact** pendant une durée choisie (30 min à 4 h) d'ouverture ;
- des **propositions** (échanger, rapprocher du passage, rendre visible, présenter autrement, conseiller, réassortir), issues de règles lisibles dans [Placement.cs](backend/src/PharmaVision.Domain/Placement.cs) ;
- les médicaments qui partent le mieux et ceux qui dorment, d'après la caisse.

La caméra juge un rayon, pas une boîte. Les contacts avec les produits sont aujourd'hui **simulés** : le boîtier réel ne les détecte pas encore.

## Comptes de démonstration

Créés au démarrage en développement ; mot de passe commun défini par `Seed:DemoPassword` dans `appsettings.Development.json` (`Demo-Pharma-2026`).

| Adresse | Rôle | Accès |
| --- | --- | --- |
| `titulaire@pharmavision.demo` | Titulaire | Les 3 officines, configuration comprise |
| `equipe.tunis@pharmavision.demo` | Équipe | Tunis |
| `equipe.sfax@pharmavision.demo` | Équipe | Sfax |
| `equipe.sousse@pharmavision.demo` | Équipe | Sousse |

Le rôle Équipe consulte le tableau de bord et traite les alertes ; caméras et zones sont en lecture seule et les adresses RTSP lui sont masquées. La session est un cookie `HttpOnly` de 12 heures ; la connexion est limitée à 10 tentatives par minute et par adresse IP, et un compte est bloqué 15 minutes après 5 mots de passe erronés. Désactiver, supprimer ou modifier un compte ferme aussitôt ses sessions. Les comptes se gèrent dans Paramètres.

## Données fictives

- 3 officines (Tunis, Sfax, Sousse), 6 caméras chacune, linéaires, comptoirs, entrée et zone réglementée.
- Horaires 8 h – 20 h avec pics en fin de matinée et fin d'après-midi. Hors horaires, le simulateur garde une activité réduite (`Simulator:AlwaysOpen`).
- Au premier démarrage, le rayon Dermo de la pharmacie de Tunis est en rupture et sa caméra « Réserve » hors ligne, pour montrer les alertes. « Réassortir » remplit le rayon simulé, ce qui clôt l'alerte.
- Les images de caméra et les vignettes sont des dessins générés par l'API.
- La découverte ONVIF renvoie des caméras inventées.
- Un catalogue de produits fictif ([ProductCatalog.cs](backend/src/PharmaVision.Domain/ProductCatalog.cs)) rattache les ventes à des marques. Sur une base créée avant son ajout, les anciennes ventes restent comptées par famille mais sans marque.

Repartir de zéro (**efface la base**) :

```bash
docker compose down -v
```

## Analyse par zone et par marque

L'écran Zones & linéaires donne, pour chaque zone, ses mesures sur la période (par tranche de 5 minutes, par heure ou par jour) et ses alertes. L'écran Produits & marques croise la caisse et les linéaires. La caméra ne reconnaît pas les produits : arrêts et prises en main sont mesurés par linéaire, et les marques d'un même rayon les partagent.

## Rapports

PDF ou Excel pour la période choisie : synthèse, zones, produits et marques, et rapport laboratoire limité à une marque (ventes, visibilité et disponibilité de ses linéaires). Ils sont produits par l'API avec PDFsharp-MigraDoc et ClosedXML (licence MIT). Hors Docker sur Linux, les PDF demandent le paquet `fonts-dejavu-core`.

## Historique et conservation

« Historique & replay » rejoue une journée par quart d'heure ; chaque alerte montre la courbe de sa zone. Dans Paramètres, chaque officine choisit combien de temps les images d'alerte restent consultables (30 jours par défaut) et les mesures, ventes et alertes sont conservées (24 mois). L'effacement a lieu au démarrage puis une fois par jour ; les totaux horaires anonymes (`events_hourly`) sont gardés.

## Import de la caisse

Paramètres › Import caisse (titulaire). Le fichier est d'abord analysé, puis importé après confirmation ; une seule ligne invalide refuse le fichier entier.

| Fichier | Colonnes obligatoires | Facultatives |
| --- | --- | --- |
| Ventes (une ligne par produit d'un ticket) | `date` (avec heure, ou colonne `heure`), `ticket`, `code`, `quantite`, `montant` | `famille` (sinon reprise du catalogue) |
| Catalogue | `code`, `nom`, `famille` | `marque`, `prix` |

Séparateur `;`, `,` ou tabulation ; UTF-8 ou Windows-1252 ; dates `jj/mm/aaaa hh:mm` ou `aaaa-mm-jj hh:mm`, à l'heure de l'officine ; un seul séparateur décimal par montant. 10 Mo et 100 000 lignes au maximum.

- Réimporter le même fichier n'ajoute rien.
- Le numéro de ticket est préfixé par sa date, pour les caisses qui recommencent chaque jour.
- « Remplacer » efface les ventes **non importées** (simulées) des jours couverts ; elles ne reviennent pas si l'import est annulé. Le simulateur continue ensuite d'en produire : l'arrêter avec `docker compose stop simulator`.
- Le catalogue est commun aux officines : il faut avoir accès à toutes pour le modifier.
- La famille doit porter le même nom que la catégorie du linéaire pour calculer sa transformation.

## Brancher un vrai boîtier

Installation et essai sur une vidéo : [edge/README.md](edge/README.md). Une caméra rattachée à un boîtier réel alimente les indicateurs de son officine, mêlés aux données fictives du simulateur : pour un essai propre, utiliser une base neuve ou une officine dont le simulateur est arrêté.

Tant qu'une image de référence n'a pas été reçue, la caméra affiche un décor dessiné ; ensuite, les vignettes d'alerte montrent cette image avec la zone concernée (et non l'instant de l'alerte).

Le boîtier s'authentifie avec l'en-tête `X-Api-Key` (clé créée dans Paramètres › Boîtiers ; seule son empreinte est enregistrée) :

| Appel | Usage |
| --- | --- |
| `GET /api/ingest/config` | Zones à analyser, réassorts demandés |
| `POST /api/ingest/heartbeat` | Signal de présence |
| `POST /api/ingest/events` | Lot d'événements et de ventes (5 000 lignes maximum) |
| `PUT /api/ingest/cameras/{id}/image` | Image de référence JPEG (2 Mo maximum) |

Le format est défini dans [Contracts.cs](backend/src/PharmaVision.Domain/Contracts.cs).

## Tests

```bash
dotnet test backend
cd frontend && npm run build
cd ../edge && python -m pytest -q
```

## Pas encore fait

- Boîtier réel : remplissage des linéaires, prises en main, distinction du personnel, découverte ONVIF, image de l'instant sur les alertes, détection automatique des caméras hors ligne, mise à jour à distance, MQTT.

- **Pas prêt pour une mise en ligne** : pas de HTTPS ; les ports sont limités au poste local.
- Mot de passe oublié par e-mail, double authentification, journal des actions d'administration.
- En conteneurs, les sessions sont perdues quand l'API est recréée (clés de chiffrement non conservées). Derrière le proxy web, la limite de tentatives est partagée par tous les postes.
- Clés de boîtier et mot de passe de base codés pour le développement.
- Gestion du catalogue de produits (il sera alimenté par le logiciel de gestion d’officine).
- Envoi réel des notifications (le canal est enregistré, rien n'est envoyé).
- Règle « hors planogramme » et mesure du respect du planogramme.\n- Liaison directe (API) avec le logiciel de gestion d'officine, import du stock.\n- Envoi automatique hebdomadaire des rapports, période au choix (dates libres).
