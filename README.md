# KLEAN

Logiciel libre de gestion opérationnelle pour les entreprises de propreté :
nettoyage de bureaux et de copropriétés. Il couvre le planning hebdomadaire
des agents, le remplacement d'un agent absent avec candidats classés par
score, la gestion des stocks et la certification des passages.

Cible : entreprises de 5 à 150 agents, plusieurs dizaines de sites.

Hors périmètre : comptabilité, paie, CRM commercial, ERP financier,
facturation complexe.

Licence : **AGPL v3** (voir `LICENSE`). Toute personne qui exploite KLEAN
comme service doit publier ses modifications.

---

## Prérequis

| Outil | Version | Vérification |
|---|---|---|
| .NET SDK | 10.0 | `dotnet --version` |
| Node.js | **22.18** ou plus | `node --version` |
| PostgreSQL | 16 ou plus | `psql --version` |
| git | — | `git --version` |

Un serveur PostgreSQL doit écouter sur `localhost:5432`.

`22.18` et non `22` tout court : c'est la première version de Node qui dépouille
les types TypeScript sans drapeau et qui porte son propre lanceur de tests. Les
séries frontend en dépendent, et n'ajoutent en échange **aucune dépendance** — ni
`vitest`, ni `jsdom`, ni bibliothèque de rendu.

## Installation

### 1. Base de données

Créer la base :

```bash
createdb -h localhost -p 5432 klean
```

### 2. Chaîne de connexion

Elle n'est **jamais** versionnée. En développement, elle vit dans les
*user secrets* .NET :

```bash
cd backend
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:Klean" \
  "Host=localhost;Port=5432;Database=klean;Username=VOTRE_UTILISATEUR;Password=VOTRE_MOT_DE_PASSE"
```

Si votre installation PostgreSQL accepte l'authentification locale sans mot
de passe, omettez le segment `Password`.

### 3. Clé de signature et amorçage

Le jeton JWT est signé en HMAC-SHA256 : la clé doit faire au moins 32 octets.

```bash
dotnet user-secrets set "Jwt:Cle" "$(openssl rand -base64 64)"
dotnet user-secrets set "Jwt:Emetteur" "klean"
dotnet user-secrets set "Jwt:Audience" "klean"
```

Au premier démarrage, si aucun utilisateur n'existe, l'application crée les
cinq rôles, une entreprise et un compte administrateur à partir de ces
valeurs :

```bash
dotnet user-secrets set "Amorcage:EmailAdmin" "admin@votre-domaine.fr"
dotnet user-secrets set "Amorcage:MotDePasseAdmin" "VOTRE_MOT_DE_PASSE"
dotnet user-secrets set "Amorcage:RaisonSocialeEntreprise" "VOTRE_RAISON_SOCIALE"
dotnet user-secrets set "Amorcage:SiretEntreprise" "00000000000000"
```

Le mot de passe doit faire au moins dix caractères et contenir une majuscule,
une minuscule et un chiffre. Aucun caractère spécial n'est exigé. Un compte se
verrouille quinze minutes après cinq échecs de connexion.

`appsettings.example.json` montre la forme attendue de la configuration.
En production, utiliser des variables d'environnement, par exemple
`ConnectionStrings__Klean` et `Jwt__Cle`.

### 4. Backend

```bash
cd backend
dotnet restore
dotnet watch run
```

L'API écoute sur <http://localhost:5137>. En développement, Swagger est
exposé sur <http://localhost:5137/swagger> et un point de contrôle répond
sur `/api/sante`.

### 5. Frontend

Dans un second terminal :

```bash
cd frontend
npm install
npm run dev
```

L'interface est servie sur <http://localhost:5173>. Le serveur de
développement Vite redirige `/api` vers `http://localhost:5137` : le
frontend n'appelle jamais l'URL absolue du backend.

## Tests

Les tests tournent contre une base PostgreSQL dédiée, à créer une fois :

```bash
createdb -h localhost -p 5432 klean_test
```

Puis, depuis la racine :

```bash
dotnet test
```

La base est migrée automatiquement au démarrage de la série, puis vidée et
réamorcée avec **deux entreprises** avant chaque test — tout test d'isolation a
besoin d'un tenant témoin.

La fabrique de test pose ses propres valeurs de configuration par variables
d'environnement : chaîne de connexion, clé JWT, comptes d'amorçage. Elle vérifie
avant toute écriture que la base visée est bien `klean_test`, et refuse de
continuer sinon. Sur un exécuteur d'intégration continue, surcharger
`KLEAN_TEST_CONNECTION`.

Aucune de ces dépendances de test n'est ajoutée au projet de production.

### Frontend

```bash
cd frontend
npm test          # séries de logique, sans navigateur
npm run verifier  # conformité du système d'interface de CLAUDE.md §12
```

Aucune base de données, aucun navigateur, aucune dépendance : le lanceur de tests
de Node suffit. Deux modules sont couverts, et deux seulement — ceux où un défaut
est cher et invisible :

- **`src/utils/dates.ts`**, rejoué dans **cinq fuseaux horaires**. Ce n'est pas
  du zèle : les deux pièges de date de `CLAUDE.md` §10 sont symétriques, l'un
  casse aux décalages positifs et l'autre aux négatifs, et **aucun des deux ne se
  voit sous `TZ=UTC`** — qui est le fuseau par défaut de l'intégration continue.
- **`src/api/client.ts`** : la relecture du jeton anti-CSRF après chaque
  rotation, le rafraîchissement en un seul vol, le rejeu unique sur 401. Un
  défaut là produit des déconnexions apparemment aléatoires, le symptôme le plus
  coûteux à diagnostiquer du projet.

Les composants, le rendu et le CSS ne sont **pas** testés. Le rendu se valide au
navigateur, et pour le système d'interface `npm run verifier` fait mieux qu'un
test de rendu : il mesure les 32 paires de contraste des deux thèmes — lues dans
`tokens.css` lui-même, donc la table ne peut pas divarier du CSS —, et refuse une
couleur en dur, une ombre portée, un focus supprimé, une troncature par ellipse
ou une opacité partielle sur du texte.

## Vérifications

```bash
dotnet build                     # solution entière, doit être vert
dotnet test                      # doit être vert
cd frontend && npm run lint
cd frontend && npm run verifier  # système d'interface, CLAUDE.md §12
cd frontend && npm test          # dates et session, cinq fuseaux
cd frontend && npm run build     # tsc -b && vite build
```

L'intégration continue de `.github/workflows/ci.yml` exécute ces six commandes
sur chaque poussée.

## Migrations

L'outil `dotnet-ef` est requis :

```bash
dotnet tool install --global dotnet-ef --version 10.0.12
```

Sous macOS avec Homebrew, il faut lui indiquer où trouver le runtime, sinon il
échoue en annonçant un runtime manquant alors qu'il est installé :

```bash
export DOTNET_ROOT=/opt/homebrew/opt/dotnet/libexec
```

Puis, depuis `backend/` :

```bash
dotnet ef migrations add NomDeLaMigration
dotnet ef database update
```

La migration `InitialCreate` crée les onze tables métier. Les cinq colonnes
d'énumération sont de type `text` : la validation des valeurs est applicative,
pas contrainte par la base.

## Architecture

```
KLEAN/
├── KLEAN.sln
├── backend/            ASP.NET Core 10, Entity Framework Core 10, PostgreSQL
│   ├── Models/         entités métier
│   ├── Data/           KleanDbContext, filtres multi-tenant
│   ├── DTOs/           un fichier par entité
│   ├── Controllers/    un par entité, plus Auth et Planning
│   ├── Services/       affectation, remplacement, planning, JWT, amorçage
│   └── Migrations/
└── frontend/           React 19, TypeScript, Vite
    └── src/
        ├── api/        wrappers fetch typés
        ├── auth/       contexte d'authentification, routes protégées
        ├── composants/ un .tsx et un .css par composant
        ├── layout/     navigation, disposition
        ├── pages/      un fichier par écran
        ├── styles/     tokens.css
        ├── types/      interfaces TypeScript
        └── utils/      utilitaires de date
```

Points structurants :

- **Multi-tenant.** Chaque entité porte un `EntrepriseId`. Le tenant est lu
  dans la revendication `entrepriseId` du jeton JWT, appliqué par des filtres
  globaux Entity Framework, et affecté automatiquement à la création.
- **Authentification.** ASP.NET Core Identity et jeton JWT de 8 heures, sans
  jeton de rafraîchissement. Cinq rôles : administrateur, responsable
  d'exploitation, chef d'équipe, agent, client/syndic.
- **Autorisations.** Chaque endpoint porte les rôles qui y ont droit. La matrice
  complète est dans `SPEC.md`, étape 8. Deux axes d'isolation s'ajoutent au
  multi-tenant, à l'intérieur d'une même entreprise : un compte client/syndic ne
  voit que son client et ses sites, un compte agent que ses interventions. Les
  trois axes sont appliqués par filtres globaux Entity Framework, jamais par du
  code de contrôleur.
- **Pagination.** Les onze listes acceptent `?page=1&taille=50` et renvoient
  `{ elements, total, page, taille }`. La taille est plafonnée à 200 : sans
  plafond, une seule requête suffirait à épuiser la mémoire du serveur. `total`
  compte les lignes visibles par l'appelant, filtres appliqués.
- **Aucune bibliothèque de composants** côté frontend. Le CSS est écrit à la
  main sur les variables de `src/styles/tokens.css`. `react-router-dom` est
  la seule dépendance frontend prévue au-delà de React.

Le détail des conventions est dans `CLAUDE.md`, la feuille de route dans
`SPEC.md`.

## Contribuer

Le dépôt suit les étapes de `SPEC.md`, dans l'ordre, chacune close par un
build vert et un commit. Avant toute contribution, lire `CLAUDE.md` :
conventions de nommage en français, pile technique imposée, pièges connus.
