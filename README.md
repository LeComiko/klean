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

⚠️ **Avant d'exploiter une instance avec de vraies données de salariés, lire
`RGPD.md`.** KLEAN détient sur des agents leur adresse, **les coordonnées GPS de
leur domicile** et leurs absences. `RGPD.md` porte le registre des traitements, la
cartographie des données, les durées de conservation proposées, un modèle de note
d'information aux agents, et la liste honnête des écarts constatés — dont un qui
concerne tous les utilisateurs de toutes les instances.

Vous êtes **responsable de traitement** dès lors que vous hébergez KLEAN pour votre
entreprise. L'auteur du logiciel ne l'est pas : il n'y a aucune instance centrale,
aucune télémétrie, et il ne reçoit aucune donnée.

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

### 5. Certificat de développement

Le serveur de développement du frontend sert la page en **https**, et il a
besoin d'un certificat pour cela. Le plus simple est de réutiliser celui de
.NET, exporté au format PEM :

```bash
dotnet dev-certs https --trust
dotnet dev-certs https --export-path frontend/certs/localhost.pem \
    --format PEM --no-password
```

Les deux fichiers produits ne sont pas versionnés.

Pourquoi c'est nécessaire : les trois cookies de session portent l'attribut
`Secure`, sans condition. Chrome et Firefox traitent `localhost` comme une
origine sûre et les acceptent en clair ; **Safari non**, il les jette sans rien
dire. Le symptôme n'accuse jamais le cookie — la connexion réussit, la
navigation fonctionne, et le premier rechargement de page déconnecte.

Sans certificat, `npm run dev` démarre quand même, en clair, avec un
avertissement. Sous Chrome ou Firefox c'est utilisable ; sous Safari, non.

### 6. Frontend

Dans un second terminal :

```bash
cd frontend
npm install
npm run dev
```

L'interface est servie sur <https://localhost:5173>. Le serveur de
développement Vite redirige `/api` vers `http://localhost:5137` : le
frontend n'appelle jamais l'URL absolue du backend. Ce saut-là reste en clair
délibérément — il est en boucle locale sur la même machine, et ce qui décide du
sort d'un cookie `Secure` est l'origine que voit le navigateur.

`npm run preview` sert la **construction de production** sur
<https://localhost:4173>, avec la politique de sécurité du contenu de production.
C'est le seul endroit où l'on constate qu'elle ne bloque ni la feuille de style
ni les polices avant de déployer.

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
- **Authentification.** ASP.NET Core Identity et jeton JWT de **quinze
  minutes**, livré dans un cookie `HttpOnly` `Secure` `SameSite=Strict` :
  aucun jeton ne traverse le corps d'une réponse, et l'en-tête `Authorization`
  n'est plus accepté. Le renouvellement passe par un jeton de rafraîchissement
  opaque, persisté par empreinte, donc **révocable** — ce qu'un JWT signé ne peut
  pas être. Cinq rôles : administrateur, responsable d'exploitation, chef
  d'équipe, agent, client/syndic.
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

- **Surface HTTP.** Hors développement, l'API redirige le clair vers HTTPS et
  annonce HSTS. Toute réponse porte `Content-Security-Policy`,
  `X-Content-Type-Options`, `Referrer-Policy` et `X-Frame-Options`. Le corps
  d'une requête est plafonné à un mégaoctet et sa durée à trente secondes.
  Voir *Déploiement* ci-dessous.

Le détail des conventions est dans `CLAUDE.md`, la feuille de route dans
`SPEC.md`.

## Déploiement

Trois réglages à connaître avant d'exposer une instance. Tous ont un défaut sûr,
et aucun ne contient de secret : ils vivent dans la configuration ordinaire, pas
dans les *user secrets*. Le modèle complet est `backend/appsettings.example.json`.

**Le plus simple est de servir le frontend et l'API sous la même origine.** La
construction de `frontend/dist/` est un ensemble de fichiers statiques ; les
servir derrière le même nom d'hôte que `/api` évite CORS entièrement, et évite
aussi le piège décrit juste en dessous.

### Origines autorisées

```json
"Cors": { "Origines": ["https://klean.votre-domaine.fr"] }
```

Vide ou absent, aucune origine tierce n'est autorisée. C'est le bon défaut.

⚠️ Les cookies de session sont `SameSite=Strict`. Déclarer une origine ici **ne
suffit pas** à faire fonctionner un frontend hébergé sur un *autre domaine
enregistrable* : le navigateur n'enverrait aucun cookie, quelle que soit la
politique CORS. Un déploiement à deux origines n'est viable qu'entre
sous-domaines d'un même domaine.

### Politique de sécurité du contenu

Il y en a **deux**, différentes, et les confondre casse quelque chose dans les
deux sens :

- **L'API** pose la sienne elle-même, sur chaque réponse :
  `default-src 'none'`, rien d'autre. Elle ne sert que du JSON. Aucune
  configuration.
- **Le document** a besoin de `'self'` sur `script-src`, `style-src`, `font-src`
  et `connect-src` : il charge une feuille de style, un script et quatre polices,
  tous servis par lui-même. La politique est écrite dans `frontend/csp.ts`,
  appliquée par `npm run preview`, et c'est cette chaîne-là que le serveur web de
  production doit renvoyer en en-tête `Content-Security-Policy`. **La lire, ne
  jamais la recopier** : recopiée dans un `nginx.conf`, elle divergera du jour où
  `index.html` changera.

⚠️ Ne pas recopier la politique de l'API sur le document : la feuille de style, le
script et les polices seraient bloqués, et le défaut ne se verrait pas au
développement, où le cache du navigateur garde les polices.

**Aucune des deux politiques ne déclare d'origine externe**, et c'est une
propriété à conserver. Figtree et IBM Plex Mono sont servies depuis
`frontend/public/polices/` — 79 Ko, licences OFL incluses. Jusqu'au
3 octobre 2026 elles venaient de Google, ce qui transmettait l'adresse IP de
chaque visiteur à un tiers, sur toute instance et sans que l'exploitant l'ait
choisi : voir `RGPD.md` §9.1. `npm run verifier` échoue désormais sur toute
origine externe réapparue dans `csp.ts` ou `index.html`.

Conséquence pratique pour le déploiement : **KLEAN n'a besoin d'aucun accès
sortant pour servir son interface.** Une instance derrière un pare-feu sans route
vers l'extérieur affiche la même typographie qu'une autre.

### HTTPS

```json
"Http": { "PortHttps": 443, "TailleMaxCorpsOctets": 1048576, "DelaiRequeteSecondes": 30 }
```

Hors développement, l'API redirige toute requête en clair vers `PortHttps` et
annonce HSTS pour un an. `IncludeSubDomains` est délibérément **absent** : une
politique HSTS ne se retire pas avant son échéance, et une instance de KLEAN ne
possède pas les autres sous-domaines de son hôte.

## Contribuer

Le dépôt suit les étapes de `SPEC.md`, dans l'ordre, chacune close par un
build vert et un commit. Avant toute contribution, lire `CLAUDE.md` :
conventions de nommage en français, pile technique imposée, pièges connus.

Une contribution qui touche aux données d'agents, d'absences ou de comptes doit en
plus être relue contre `RGPD.md` : un DTO qui expose un nom tombe dans le filet par
réflexion de `JournalAccesTests`, et une colonne ajoutée à une table du §3 change la
cartographie.
