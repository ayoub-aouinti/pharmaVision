# PharmaVision

Analyse commerciale d'officine à partir des caméras existantes. **Itération 1 : toutes les données sont fictives** ; ni caméra ni IA ne sont branchées.

Documents de conception : [docs/](docs/).

## Contenu

| Dossier | Rôle |
| --- | --- |
| `backend/src/PharmaVision.Domain` | Entités, contrat boîtier ↔ serveur, formules des indicateurs, générateur de visites |
| `backend/src/PharmaVision.Infrastructure` | EF Core, migrations, hypertable TimescaleDB, données fictives initiales |
| `backend/src/PharmaVision.Api` | API REST, ingestion, SignalR, moteur d'alertes |
| `backend/src/PharmaVision.Simulator` | Boîtier local simulé (même API que le futur boîtier réel) |
| `frontend` | React + TypeScript : connexion, tableau de bord, Caméras & zones, Alertes |

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

## Comptes de démonstration

Créés au démarrage en développement ; mot de passe commun défini par `Seed:DemoPassword` dans `appsettings.Development.json` (`Demo-Pharma-2026`).

| Adresse | Rôle | Accès |
| --- | --- | --- |
| `titulaire@pharmavision.demo` | Titulaire | Les 3 officines, configuration comprise |
| `equipe.tunis@pharmavision.demo` | Équipe | Tunis |
| `equipe.sfax@pharmavision.demo` | Équipe | Sfax |
| `equipe.sousse@pharmavision.demo` | Équipe | Sousse |

Le rôle Équipe consulte le tableau de bord et traite les alertes ; caméras et zones sont en lecture seule et les adresses RTSP lui sont masquées. La session est un cookie `HttpOnly` de 12 heures ; la connexion est limitée à 10 tentatives par minute et par adresse IP.

## Données fictives

- 3 officines (Tunis, Sfax, Sousse), 6 caméras chacune, linéaires, comptoirs, entrée et zone réglementée.
- Horaires 8 h – 20 h avec pics en fin de matinée et fin d'après-midi. Hors horaires, le simulateur garde une activité réduite (`Simulator:AlwaysOpen`).
- Au premier démarrage, le rayon Dermo de la pharmacie de Tunis est en rupture et sa caméra « Réserve » hors ligne, pour montrer les alertes. « Réassortir » remplit le rayon simulé, ce qui clôt l'alerte.
- Les images de caméra et les vignettes sont des dessins générés par l'API.
- La découverte ONVIF renvoie des caméras inventées.

Repartir de zéro (**efface la base**) :

```bash
docker compose down -v
```

## Brancher un vrai boîtier

Le boîtier s'authentifie avec l'en-tête `X-Api-Key` :

| Appel | Usage |
| --- | --- |
| `GET /api/ingest/config` | Zones à analyser, réassorts demandés |
| `POST /api/ingest/heartbeat` | Signal de présence |
| `POST /api/ingest/events` | Lot d'événements et de ventes (5 000 lignes maximum) |

Le format est défini dans [Contracts.cs](backend/src/PharmaVision.Domain/Contracts.cs).

## Tests

```bash
dotnet test backend
cd frontend && npm run build
```

## Pas encore fait

- **Pas prêt pour une mise en ligne** : pas de HTTPS ; les ports sont limités au poste local.
- Gestion des comptes (création, changement de mot de passe, verrouillage) : seuls les comptes de démonstration existent. Un changement de droits ne prend effet qu'à la reconnexion.
- En conteneurs, les sessions sont perdues quand l'API est recréée (clés de chiffrement non conservées). Derrière le proxy web, la limite de tentatives est partagée par tous les postes.
- Clés de boîtier et mot de passe de base codés pour le développement.
- Écrans Zones & linéaires, Produits & marques, Historique & replay, Rapports, Paramètres.
- Envoi réel des notifications (le canal est enregistré, rien n'est envoyé).
- Règle « hors planogramme », rapports PDF/Excel, import CSV de la caisse.
- Durées de conservation des données.
