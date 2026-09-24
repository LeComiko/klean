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
| Node.js | 22 ou plus | `node --version` |
| PostgreSQL | 16 ou plus | `psql --version` |
| git | — | `git --version` |

Un serveur PostgreSQL doit écouter sur `localhost:5432`.

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

`appsettings.example.json` montre la forme attendue de la configuration.
En production, utiliser des variables d'environnement, par exemple
`ConnectionStrings__Klean`.

### 3. Backend

```bash
cd backend
dotnet restore
dotnet watch run
```

L'API écoute sur <http://localhost:5137>. En développement, Swagger est
exposé sur <http://localhost:5137/swagger> et un point de contrôle répond
sur `/api/sante`.

### 4. Frontend

Dans un second terminal :

```bash
cd frontend
npm install
npm run dev
```

L'interface est servie sur <http://localhost:5173>. Le serveur de
développement Vite redirige `/api` vers `http://localhost:5137` : le
frontend n'appelle jamais l'URL absolue du backend.

## Vérifications

```bash
cd backend  && dotnet build      # doit être vert
cd frontend && npm run build     # tsc -b && vite build
cd frontend && npm run lint
```

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
- **Aucune bibliothèque de composants** côté frontend. Le CSS est écrit à la
  main sur les variables de `src/styles/tokens.css`. `react-router-dom` est
  la seule dépendance frontend prévue au-delà de React.

Le détail des conventions est dans `CLAUDE.md`, la feuille de route dans
`SPEC.md`.

## Contribuer

Le dépôt suit les étapes de `SPEC.md`, dans l'ordre, chacune close par un
build vert et un commit. Avant toute contribution, lire `CLAUDE.md` :
conventions de nommage en français, pile technique imposée, pièges connus.
