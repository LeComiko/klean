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

## Installer KLEAN sur un serveur

C'est le chemin recommandé pour exploiter une instance : une commande, des questions
en français, et la même commande `klean` pour la tenir à jour, la sauvegarder et la
restaurer. Il n'ajoute aucun service tiers obligatoire : GitHub n'est sollicité que
pour **télécharger** les versions. Qui préfère tout faire à la main suit
[`DEPLOIEMENT.md`](DEPLOIEMENT.md).

### Ce qu'il faut

- Un serveur **Debian ou Ubuntu**, en **amd64**, avec **1 Go de mémoire** au moins.
- Un **nom de domaine** qui pointe sur ce serveur : un enregistrement A, et un
  enregistrement AAAA si le serveur a une adresse IPv6. Un AAAA oublié suffit à faire
  échouer le certificat.
- Les ports **80 et 443** ouverts dans le pare-feu de l'hébergeur : Let's Encrypt vérifie
  le domaine par le port 80.
- Un accès `ssh` en root, ou par un compte qui peut lancer `sudo`.

### La commande

Sur le serveur :

```bash
curl -fsSL https://github.com/LeComiko/klean/releases/latest/download/installer.sh | sudo bash
```

Elle installe Docker s'il manque, télécharge KLEAN, puis pose ses questions : le nom de
domaine, la raison sociale de l'entreprise, l'adresse électronique de l'administrateur, la
localisation des sites, et un serveur de courriel si vous en avez un (facultatif).
**Toutes les questions sont posées avant la moindre écriture** : un Ctrl-C à ce stade ne
laisse rien. Le reste se fait seul, jusqu'à l'obtention du certificat HTTPS, qui peut
prendre quelques minutes.

À la fin, la commande affiche **une seule fois** le mot de passe provisoire de
l'administrateur. Notez-le, puis tapez `NOTE` : il est alors retiré du fichier de
configuration. La première connexion le fait remplacer, puis équiper la double
authentification (`DEPLOIEMENT.md` §2.4).

Une installation interrompue — réseau coupé, domaine pas encore prêt, `ssh` fermé —
**reprend en relançant la même commande**, sans régénérer aucun secret. La commande
`klean` existe dès le début : `sudo klean installer` reprend aussi.

Sur un serveur auquel on se connecte par `ssh`, la commande pose des questions : un
terminal est nécessaire (`ssh -t serveur '…'` si la commande est passée en argument).

### La variante prudente

Lancer ce qu'un serveur renvoie sans l'avoir lu demande de la confiance. Pour lire avant
de lancer :

```bash
curl -fsSLO https://github.com/LeComiko/klean/releases/latest/download/installer.sh
curl -fsSLO https://github.com/LeComiko/klean/releases/latest/download/installer.sh.sha256
sha256sum -c installer.sh.sha256
less installer.sh
sudo bash installer.sh
```

**Ce que la vérification garantit :** que `installer.sh` est entier, tel que publié, et que
l'archive qu'il télécharge ensuite est exactement celle de la version qu'il annonce — son
empreinte SHA-256 est **gravée dans l'installateur** —, le tout par HTTPS. **Ce qu'elle ne
garantit pas :** qui pourrait publier sur le dépôt changerait l'installateur et son
empreinte ensemble. C'est la confiance accordée au dépôt, pas une signature.

### Les commandes `klean`

```
sudo klean installer               première installation, ou reprise
sudo klean mettre-a-jour [1.2.3]   sans version : la dernière publiée
sudo klean revenir                 la version précédente, et la base d'avant (14 jours)
sudo klean sauvegarder             ce que fait la tâche de chaque nuit
sudo klean restaurer <sauvegarde>  une sauvegarde de la base, et le trousseau
sudo klean etat                    les services, la dernière sauvegarde, les alertes
sudo klean journaux [n]            les n dernières lignes de l'API et de Caddy (200)
sudo klean code                    le code d'installation, tant qu'aucun compte n'existe
```

Une seule commande `klean` à la fois : une seconde répond tout de suite qu'une autre est
en cours. La sauvegarde de la nuit, elle, attend jusqu'à trente minutes que le verrou se
libère. `mettre-a-jour` et `revenir` sont décrites dans `DEPLOIEMENT.md` §3 et §5.

### Sauvegardes

La base est sauvegardée **chaque nuit à 3 h 30** (tâche `/etc/cron.d/klean`, journal dans
`/var/log/klean-sauvegarde.log`). Chaque sauvegarde est relue en entier avant d'être gardée,
et **quatorze jours** sont conservés.

- La base va dans `/var/backups/klean`.
- Le trousseau de l'API, qui chiffre les secrets de la double authentification, va
  **ailleurs** : `/var/lib/klean-trousseau`.

**Ces sauvegardes restent sur le serveur : s'il est perdu, elles le sont avec lui.
Copiez-les régulièrement sur une autre machine, la base et le trousseau
séparément — jamais ensemble, ni au même endroit en clair : une copie des deux livre
toutes les clés.** Elles contiennent des données de santé et les adresses des domiciles
des salariés : chiffrez-les (`gpg --symmetric`) si elles quittent un stockage que vous
maîtrisez. Par exemple :

```bash
sudo rsync -a /var/backups/klean/ utilisateur@autre-machine:sauvegardes-klean/
sudo rsync -a /var/lib/klean-trousseau/ utilisateur@une-troisieme-machine:trousseau-klean/
```

`sudo klean etat` signale l'absence de sauvegarde récente, et le rappelle. La restauration
est décrite dans `DEPLOIEMENT.md` §4.

---

## Développement

Les parties qui suivent concernent le développement de KLEAN, pas son exploitation.

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

### 4. Tables

L'API ne crée pas ses tables, en développement pas plus qu'en production. Sur une
base vide, elle s'arrête au démarrage sur `relation "AspNetRoles" does not exist`.
Les migrations demandent l'outil `dotnet-ef`, à installer une fois :

```bash
dotnet tool install --global dotnet-ef --version 10.0.12
```

Sous macOS avec Homebrew, indiquez-lui où trouver le runtime. Sans cela, il échoue en
annonçant un runtime manquant alors que le runtime est installé :

```bash
export DOTNET_ROOT=/opt/homebrew/opt/dotnet/libexec
```

Puis, toujours depuis `backend/` :

```bash
dotnet ef database update
```

La migration lit la même chaîne de connexion que l'API, dans les *user secrets*.

### 5. Un jeu de démonstration, facultatif

Pour voir KLEAN rempli, sans rien saisir :

```bash
dotnet run -- demonstration
```

La commande démarre comme l'API : elle crée l'entreprise et l'administrateur de
l'étape 3 s'ils n'existent pas encore. Elle remplit ensuite cette entreprise, puis elle
s'arrête sans servir de requête.

Le jeu se construit autour du jour de l'installation, et il contient :
- deux clients, cinq sites, six agents ;
- les vacations de deux semaines en arrière à quatre semaines devant ;
- un remplacement en recherche et un remplacement pourvu ;
- des passages pointés, avec trois anomalies ;
- un stock, dont un produit sous son seuil.

Elle crée aussi un compte par rôle : responsable, chef d'équipe, agent et client
syndic. Leurs mots de passe s'affichent **une seule fois**, dans la sortie de la
commande. Chacun est provisoire : la première connexion le fait remplacer.

Trois choses à savoir :

- **Elle ne s'installe que dans une instance neuve** : une seule entreprise, sans
  client ni agent. Sinon elle refuse en disant pourquoi, sort avec le code 1 et
  n'écrit rien. Elle ne complète jamais des données existantes, même les siennes :
  pour recommencer, partez d'une base neuve.
- **Elle n'est pas atomique.** Si une règle refuse un geste en cours de route, la
  commande s'arrête et le dit, mais ce qui précède reste écrit. Ce refus est prévu
  pour apparaître dans la série de tests (`JeuDeDemonstrationTests`), qui joue la
  commande à chaque passage, et pas chez vous.
- **Elle ne laisse aucune ligne au journal d'accès.** Elle passe par les contrôleurs de
  l'API, donc par leurs règles, mais pas par la journalisation ni par l'autorisation par
  attribut.

Lancez-la avant `dotnet watch run`, ou arrêtez celui-ci pendant la commande : les deux
construisent le même exécutable (voir `CLAUDE.md` §10).

### 6. Backend

```bash
cd backend
dotnet restore
dotnet watch run
```

L'API écoute sur <http://localhost:5137>. En développement, Swagger est
exposé sur <http://localhost:5137/swagger> et un point de contrôle répond
sur `/api/sante`.

### 7. Certificat de développement

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

### 8. Frontend

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

L'outil `dotnet-ef` s'installe comme à l'étape 4 de l'installation. Ensuite, depuis
`backend/` :

```bash
dotnet ef migrations add NomDeLaMigration
dotnet ef database update
```

Les colonnes d'énumération sont de type `text`. La validation des valeurs est faite
par l'application, pas par une contrainte de la base.

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

**La marche à suivre, de zéro jusqu'à la première connexion, est dans
[`DEPLOIEMENT.md`](DEPLOIEMENT.md)** : `docker-compose.yml` démarre PostgreSQL,
l'API et Caddy, qui obtient le certificat HTTPS et sert l'interface. Elle couvre
aussi les secrets, la sauvegarde, la restauration et le retour arrière d'une
migration.

Ce qui suit décrit les réglages de l'API elle-même, utiles à qui ne passe pas par
`docker-compose.yml`. Tous ont un défaut sûr, et aucun ne contient de secret : ils
vivent dans la configuration ordinaire, pas dans les *user secrets*. Le modèle
complet est `backend/appsettings.example.json`.

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
  `index.html` changera. L'image Caddy le fait déjà : `scripts/entetes-caddy.mjs`
  lit `csp.ts` à la construction et en tire le bloc d'en-têtes. Un autre serveur
  web peut s'en servir de la même façon.

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

### Proxy inverse

```json
"Proxy": { "Reseaux": ["172.28.0.0/16"] }
```

**Obligatoire dès que l'API est derrière un proxy**, et il ne doit contenir que le
réseau de ce proxy. Sans lui, l'API voit l'adresse du proxy pour tous les clients :
la limitation de débit devient un plafond commun à toute l'instance, et la
redirection HTTPS boucle. Ne jamais y mettre `0.0.0.0/0`, ni utiliser
`ASPNETCORE_FORWARDEDHEADERS_ENABLED` : chaque client pourrait alors choisir l'adresse
sous laquelle il est compté. Vide par défaut, ce qui est juste pour une API exposée
directement.

### Journaux et erreurs

Hors développement, l'API écrit une ligne JSON par événement sur la sortie standard :
une par requête, et chaque exception non rattrapée avec sa pile. Toute réponse porte
un en-tête `X-Correlation-Id`, qui est le `TraceId` de ces lignes. Une erreur serveur
rend un corps `application/problem+json` en français, sans rien de l'exception, avec
ce même identifiant dans le champ `correlation` : c'est ce qu'un utilisateur vous
transmettra, et ce qu'il faut chercher dans le journal.

Le journal ne contient ni adresse IP, ni identité de l'appelant, ni corps de requête
(`RGPD.md` §3).

## Contribuer

Le dépôt suit les étapes de `SPEC.md`, dans l'ordre, chacune close par un
build vert et un commit. Avant toute contribution, lire `CLAUDE.md` :
conventions de nommage en français, pile technique imposée, pièges connus.

Une contribution qui touche aux données d'agents, d'absences ou de comptes doit en
plus être relue contre `RGPD.md` : un DTO qui expose un nom tombe dans le filet par
réflexion de `JournalAccesTests`, et une colonne ajoutée à une table du §3 change la
cartographie.
