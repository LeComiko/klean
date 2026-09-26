# SPEC.md — KLEAN

Feuille de route. Dix-neuf étapes, dans l'ordre. Chacune se termine par un
build vert, une validation fonctionnelle et un commit.

Les conventions, la pile et les pièges sont dans `CLAUDE.md`.

> **Renumérotation.** La cible a changé : d'outil interne, KLEAN devient un
> produit mis à disposition d'entreprises tierces. Les étapes 1 à 5 sont
> livrées et inchangées. L'isolation multi-tenant est réécrite — elle corrigeait une faille,
> voir son avertissement — et passe en étape 7, derrière le harnais de tests,
> pour que ses trois tests naissent automatisés. Trois étapes de solidité
> s'insèrent dans ce bloc, et
> les anciennes étapes 7 à 12 deviennent 9, 10, 11, 13, 14, 15. Un bloc
> d'avant-production suit, et l'étape 19 ferme la liste — elle attend une
> réponse d'un client réel, pas du code.

---

## Proposition de valeur

> Le logiciel qui organise automatiquement vos équipes, vos interventions et
> vos produits, et prouve à vos clients que le travail a été réalisé.

**Pour l'entreprise de propreté** : moins de temps à organiser les équipes,
moins d'appels pour trouver un remplaçant, moins de ruptures de produits,
plus de visibilité sur les interventions.

**Pour le syndic** : une preuve simple et transparente des prestations
réalisées dans chaque copropriété.

---

## Rôles utilisateurs

| Rôle | Accès |
|---|---|
| **Administrateur** | Tout : paramètres, utilisateurs, données de référence, intégrations |
| **Responsable d'exploitation** | Planning, affectations, absences, remplacements, interventions, stocks, anomalies, rapports |
| **Chef d'équipe** | Planning de son équipe, interventions, contrôles, validation des prestations |
| **Agent** | Application mobile : ses interventions, consignes, checklist, anomalies, photos |
| **Client / Syndic** | Portail très restreint : prestations prévues et réalisées, passages certifiés, rapports, anomalies le concernant |

Le rôle **Client / Syndic** est le seul accès externe au système. Son
périmètre doit être cadré avec une attention particulière : une erreur
exposerait les données internes d'une entreprise à ses clients.

⚠️ Ce cadrage est un **second axe d'isolation**, indépendant du multi-tenant.
Les filtres de l'étape 7 séparent les entreprises entre elles ; ils ne séparent
pas deux clients d'une même entreprise de propreté. Sans l'étape 8, le syndic A
voit les sites du syndic B.

---

# Étape 1 — Socle

Créer la structure du dépôt et vérifier que tout démarre.

**Périmètre**
- `git init`, `.gitignore` .NET + Node
- Solution `KLEAN.sln`, projet `backend` (`dotnet new webapi`)
- Projet `frontend` (`npm create vite@latest -- --template react-ts`,
  linter **oxlint**, livré par le template)
- `.oxlintrc.json` : activer `react/exhaustive-deps` et le jeu `jsx-a11y`
  en `error` — ce sont les règles qui servent les étapes 10 à 12 et les
  exigences d'accessibilité de `CLAUDE.md` §12
- Packages : `Npgsql.EntityFrameworkCore.PostgreSQL`,
  `Microsoft.EntityFrameworkCore.Design`, `Swashbuckle.AspNetCore`
- Base PostgreSQL `klean` créée
- Chaîne de connexion dans les **user secrets**, jamais dans
  `appsettings.json`
- `appsettings.example.json` versionné avec `VOTRE_MOT_DE_PASSE`
- `LICENSE` (texte intégral AGPL v3) et `README.md` (prérequis,
  installation, architecture — en français, factuel, sans marketing)

**Validation** : `dotnet run` démarre sur `http://localhost:5137`,
`npm run dev` sur `http://localhost:5173`, `git status` ne montre ni
`node_modules/`, ni `bin/`, ni `obj/`.

---

# Étape 2 — Modèle de données

Les onze entités et la migration initiale.

**Périmètre**
- `backend/Models/` : Entreprise, Client, Site, Contrat, Prestation, Agent,
  Intervention, Absence, Remplacement, Produit, Stock — une classe par
  fichier, enums dans le fichier de l'entité qui les utilise
- `Id` de type `Guid`, data annotations `[Key]`, `[Required]`, `[MaxLength]`
- Propriétés de navigation bidirectionnelles sur les relations one-to-many
- `EntrepriseId` (Guid, requis) sur **dix** entités dès le départ — les trois
  qui la portent naturellement avec clé étrangère (Client, Agent, Produit) et
  les sept dénormalisées **sans** clé étrangère (Site, Contrat, Prestation,
  Intervention, Absence, Remplacement, Stock). `Entreprise` n'en porte pas :
  son propre `Id` **est** le tenant, et l'amorçage de l'étape 5 doit créer la
  première entreprise avant qu'aucun tenant n'existe
- `backend/Data/KleanDbContext.cs` : les onze `DbSet`, les cinq conversions
  d'enum en string
- Migration `InitialCreate`

**Validation** : les 11 tables existent dans PostgreSQL, les colonnes d'enum
sont de type `text`, `dotnet build` est vert.

⚠️ Présenter la migration pour relecture avant de l'appliquer.

---

# Étape 3 — CRUD, premier lot

Établir le pattern qui sera répliqué.

**Périmètre** — Entreprise, Client, Site, Agent
- `DTOs/XxxDto.cs` : `XxxDto` (avec `Id`) et `XxxCreateDto` (sans)
- `Controllers/XxxController.cs` : cinq endpoints async
  (GET liste, GET par id, POST, PUT, DELETE)
- `Program.cs` : `AddControllers()`, `MapControllers()`, Swagger

**Validation** : 20 endpoints dans Swagger, un POST puis un GET fonctionnent
sur chaque entité.

---

# Étape 4 — CRUD, lots suivants

Répliquer strictement le pattern de l'étape 3.

**Périmètre** — Contrat, Prestation, Intervention, puis Absence,
Remplacement, Produit, Stock. Par lots de trois ou quatre.

Points d'attention : `Intervention` porte un enum, un `Guid?` nullable et un
`TimeSpan` ; `Remplacement` porte un enum et trois champs nullables.

**Validation** : 55 endpoints dans Swagger, 22 schémas DTO.

---

# Étape 5 — Authentification

**Périmètre**
- Package `Microsoft.AspNetCore.Identity.EntityFrameworkCore`
- `Models/Utilisateur.cs` (`IdentityUser<Guid>` + EntrepriseId, AgentId,
  ClientId, Nom, Prenom, DateCreation, Actif)
- `Models/RolesKlean.cs` : les cinq constantes
- `KleanDbContext` hérite d'`IdentityDbContext<Utilisateur,
  IdentityRole<Guid>, Guid>` ; `base.OnModelCreating` appelé **en premier**
- Package `Microsoft.AspNetCore.Authentication.JwtBearer`
- `Services/JwtService.cs` : revendications dont **`entrepriseId`**
- `Services/AmorcageService.cs` : rôles, puis entreprise et administrateur
  si aucun utilisateur n'existe
- `Controllers/AuthController.cs` : `connexion` (anonyme), `comptes`
  (Administrateur), `moi` (authentifié)
- Configuration JWT dans `Program.cs` — voir le piège Identity/cookie

**Validation** : connexion retourne un token, `GET /api/auth/moi` avec le
token retourne le profil, sans token retourne **401 sans redirection**.

---

# Étape 6 — Socle de tests et intégration continue

Les critères de validation des étapes suivantes sont déjà rédigés comme des cas
de test — « détection 1 remplacement, idempotence 0 au second appel, accepter
sur `Recherche` → 400 ». Les exécuter à la main ne protège de rien : la
vérification disparaît avec le terminal.

Le harnais passe **avant** l'isolation, et c'est délibéré : les trois tests de
l'étape 7 naissent alors automatisés au lieu d'être vérifiés à la main puis
convertis. Cette étape existe pour que l'étape 11 ne puisse pas casser
l'isolation de l'étape 7 en silence.

**Périmètre**
- Projet `backend.Tests/Klean.Api.Tests.csproj`, ajouté à `KLEAN.sln`
- Dépendances, **de test uniquement** : `Microsoft.NET.Test.Sdk`, `xunit`,
  `xunit.runner.visualstudio`, `Microsoft.AspNetCore.Mvc.Testing`. Pas de
  Testcontainers, pas de bibliothèque d'assertions, pas de bibliothèque de
  substituts — `WebApplicationFactory` et les assertions d'xUnit suffisent.
  Aucune dépendance ajoutée au projet de production.
- Base dédiée `klean_test`, migrée au démarrage de la série, vidée entre les
  tests. **Pas de fournisseur InMemory** : il ne reproduit ni les filtres
  globaux, ni les contraintes, ni le refus des `DateTime` non datés de Npgsql —
  soit trois des choses qu'on veut précisément tester.
- Fixture d'amorçage à **deux entreprises** : tout test d'isolation a besoin
  d'un tenant témoin.
- **Tests de référence sur l'existant** : les validations des étapes 3 à 5,
  vérifiées à la main jusqu'ici, converties en tests. Aller-retour CRUD, 409 sur
  suppression d'un agent porteur d'interventions, 400 sur enum invalide,
  connexion, `moi` avec et sans jeton, verrouillage après cinq échecs. Ce qui a
  été vérifié une fois doit le rester.
- Intégration continue : `dotnet build`, `dotnet test`, `npm run build`,
  `npm run lint` sur chaque poussée. Une suite que personne n'exécute ne vaut
  rien.

⚠️ La fabrique de test doit **écraser la chaîne de connexion après** les *user
secrets*, sinon une série de tests vise la base de développement et la vide.
C'est le seul endroit du projet où une erreur de configuration détruit des
données.

**Validation** : `dotnet test` vert sur les tests de référence. Et une preuve
que le harnais détecte : casser volontairement une assertion doit faire rougir
la série. Un harnais qui tourne sans rien vérifier est pire que pas de harnais,
parce qu'il rassure.

---

# Étape 7 — Isolation multi-tenant

**Périmètre**
- `[Authorize]` sur les onze contrôleurs métier (pas sur `AuthController`)
- `Services/ITenantContext.cs` et `TenantContext.cs`
- Filtres globaux dans `OnModelCreating` — voir le piège du cache de modèle
- `SaveChangesAsync` surchargé : **force** `EntrepriseId` à la valeur du jeton
  sur toute entité à l'état `Added`, sans condition
- `EntrepriseId` retiré des dix `XxxCreateDto` : il n'a rien à faire dans un
  contrat d'entrée, sa seule source légitime est le jeton
- `AmorcageService` appelle `DesactiverFiltrage()` en premier

⚠️ **Correction d'une faille de la version précédente de cette étape.** Le
mécanisme décrit au départ n'affectait `EntrepriseId` que si sa valeur était
`Guid.Empty`, et en concluait qu'un utilisateur ne pouvait pas créer de données
pour une autre entreprise. La conclusion ne découlait pas du mécanisme : un
`POST` portant l'identifiant **réel** d'une autre entreprise n'était pas
écrasé, et la ligne était créée dans le tenant de cette entreprise. Les filtres
de lecture empêchaient de la relire, mais l'écriture inter-tenant passait —
interventions fantômes, absences, stocks injectés chez un concurrent. Le test de
validation d'origine ne couvrait que le cas `00000000-...`, donc exactement
celui qui fonctionnait.

**Validation** — trois tests **automatisés** dans le harnais de l'étape 6, les
trois obligatoires :
1. Un administrateur ne voit **aucune** donnée d'une autre entreprise.
2. Un POST sans `entrepriseId` crée l'enregistrement avec celui du jeton.
3. Un POST portant l'`EntrepriseId` **réel** d'une autre entreprise ne crée
   rien dans cette entreprise. C'est le cas que le test d'origine ratait.

Plus une quatrième vérification, sur le harnais lui-même : retirer un filtre
global doit faire **échouer** le premier test. Un filtre qui laisse tout passer
en silence est le piège documenté en §10 de `CLAUDE.md` ; il faut prouver que la
série le voit.

---

# Étape 8 — Autorisations, périmètres et pagination

Le tenant isole les entreprises entre elles. Il n'isole rien **à l'intérieur**
d'une entreprise, et `ClientSyndic` est le seul accès externe du système. Une
seule passe sur les onze contrôleurs, puisque autorisations et pagination
touchent les mêmes fichiers.

**Périmètre**
## Matrice des droits

`L` lire · `C` créer · `M` modifier · `S` supprimer · `—` aucun accès, 403

| Ressource | Administrateur | Responsable | ChefEquipe | Agent | ClientSyndic |
|---|---|---|---|---|---|
| Entreprises | L M | L | L | — | — |
| Clients | L C M S | L | L | — | L¹ |
| Sites | L C M S | L | L | — | L¹ |
| Contrats | L C M S | L | L | — | — |
| Prestations | L C M S | L | L | — | — |
| Agents | L C M S | L | L | — | — |
| Interventions | L C M S | L C M S | L **M** | L² | — |
| Absences | L C M S | L C M S | L | — | — |
| Remplacements | L C M S | L C M S | L | — | — |
| Produits | L C M S | L | L | — | — |
| Stocks | L C M S | L C M S | L | — | — |

¹ **`ClientSyndic`** ne voit que les ressources rattachées à son propre
`ClientId`. Deuxième axe d'isolation, indépendant du tenant.
² **`Agent`** ne voit que les interventions dont il est l'affectataire.
Troisième axe, même mécanisme.

**Lecture de la matrice, ligne par ligne**

- **Administrateur** : tout, sauf créer ou supprimer une entreprise — voir
  ci-dessous.
- **Responsable d'exploitation** : écrit sur ce que `SPEC.md` lui attribue —
  interventions, absences, remplacements, stocks. Il **lit** les données de
  référence : clients, sites, contrats, prestations, agents, produits. Le
  partage entre exploitation et référence vient du tableau des rôles, qui
  réserve les « données de référence » à l'administrateur.
- **Chef d'équipe** : lecture sur toute son entreprise, écriture **uniquement**
  sur les interventions, et en modification seulement — planifier relève du
  responsable, contrôler et valider relèvent de lui. Défini par le verbe et non
  par le périmètre : le cadrage par périmètre est l'étape 19.
- **Agent** : ses interventions, en lecture. Le reste de l'application mobile —
  consignes, checklist, anomalies, photos — suppose des endpoints conçus pour
  elle, et des entités qui n'existent pas encore. Cette étape **sécurise** le
  rôle, elle ne le sert pas.
- **Client / Syndic** : son client et ses sites, en lecture. Même raisonnement :
  ce qu'un syndic veut réellement voir — prestations réalisées, passages
  certifiés, rapports — c'est le portail, avec ses propres endpoints. On ferme
  d'abord, on ouvre ensuite ce qui est conçu pour l'être.

⚠️ **`POST` et `DELETE` sur `/api/entreprises` sont retirés.** Créer ou
supprimer une entreprise est du provisionnement d'instance, pas de la gestion de
tenant. En l'état, un administrateur pouvait créer une entreprise que le filtre
lui masquait aussitôt, et supprimer la sienne en cascade. L'amorçage crée la
première ; les suivantes relèveront d'un parcours d'inscription. Le décompte
passe de 55 à **53** endpoints métier, plus 3 d'authentification.

⚠️ Le point où je m'attends à de la contradiction du terrain : un responsable
d'exploitation qui ne peut pas créer un site ni un contrat. C'est ce que dit le
tableau des rôles, mais ça peut ne pas résister à un premier client. C'est une
ligne de la matrice, donc un attribut à changer.

**Périmètre de mise en œuvre**
- `[Authorize(Roles = ...)]` par endpoint, conformément à la matrice
- les deux nouveaux axes d'isolation appliqués par **filtres globaux**, comme le
  tenant : `Client` sur `e.Id`, `Site` sur `e.ClientId`, `Intervention` sur
  `e.AgentId`. Les revendications `clientId` et `agentId` sont déjà dans le
  jeton. Aucun contrôleur ne doit refaire ce travail à la main.
- **Second axe d'isolation** : un compte `ClientSyndic` ne voit que les sites de
  son `ClientId`, en lecture seule. Filtre appliqué côté requête, jamais en
  confiance dans le contrôleur.
- `PUT /api/remplacements/{id}` : interdire la modification de `Statut`. Le
  CRUD brut de l'étape 4 laisse forcer `Accepte` et contourne la machine à
  états de l'étape 10.
- Pagination sur les onze `Lister()` : `?page=1&taille=50`, plafond de taille,
  réponse enveloppée `{ elements, total, page, taille }`. Sans plafond,
  `GET /api/interventions` est un vecteur d'épuisement de ressources.
- **Gestion des comptes.** `AspNetUsers` n'est pas couverte par les filtres
  globaux — Identity a besoin de requêtes non filtrées pour la connexion et le
  contrôle d'unicité d'adresse. Tout endpoint listant ou cherchant des comptes
  doit donc filtrer à la main sur `EntrepriseId`.
- **`POST` et `DELETE` sur `/api/entreprises`** sont des opérations de
  provisionnement d'instance, pas de gestion de tenant. En l'état, un
  administrateur peut créer une entreprise qu'il ne verra jamais — le filtre la
  masque aussitôt — et supprimer la sienne en cascade. À sortir de l'API
  destinée aux tenants, ou à réserver à un rôle d'exploitation de l'instance.

- **`ChefEquipe` est défini par le verbe, pas par le périmètre.** Lecture sur
  toute son entreprise, comme le responsable. Écriture limitée aux
  interventions : changement de statut, contrôles, validation des prestations.
  Aucune écriture sur contrats, clients, agents, produits, stocks. Le cadrage
  par périmètre est l'étape 19 — il demande de savoir si le périmètre d'un chef
  est un ensemble de personnes ou de bâtiments, et cette réponse vient d'un
  client réel, pas d'un raisonnement.
- **`Absence.Motif` interdit quand `Type = Maladie`.** Le planning a besoin de
  savoir *qu'*un agent est absent, pas *pourquoi*. Un motif d'arrêt maladie en
  texte libre est une donnée de santé au sens de l'article 9 du RGPD, et le
  produit n'en fait rien. `AbsencesController` refuse en 400 avec un message
  qui explique le motif du refus plutôt que de vider le champ en silence ; le
  frontend n'affiche pas le champ quand `Maladie` est sélectionné. L'interface
  empêche, l'API fait barrage.
  Le champ reste conservé pour `Autre` — où il est indispensable, « Autre » sans
  motif ne veut rien dire —, `Indisponibilite`, `Retard` et `Conge`.
  Aucune migration : seule une règle de validation change.

⚠️ Si le besoin derrière le texte libre est de distinguer un **accident du
travail** — régime juridique et obligations employeur différents —, la réponse
est une valeur d'énumération `AccidentTravail`, pas de la prose : structuré,
minimal, et ça n'invite pas au diagnostic. Les enums étant stockés en `text`
sans contrainte `CHECK`, ajouter une valeur ne demande aucune migration.
À trancher si le besoin est confirmé.

**Validation** — par des tests, pas à la main :
- un `ClientSyndic` du client A n'obtient rien des sites du client B **de la
  même entreprise**
- un `ClientSyndic` obtient 403 sur tout verbe d'écriture
- un `PUT` de remplacement tentant de forcer `Accepte` est refusé
- `?taille=100000` est ramenée au plafond
- un `ChefEquipe` obtient 403 sur toute écriture hors interventions
- un POST d'absence `Maladie` avec un `Motif` non vide est refusé en 400
- pour chaque rôle, la liste des endpoints attendus en 200, 403 et 404

---

# Étape 9 — Moteur d'affectation

**Périmètre**
- `Services/AffectationService.cs` : filtres durs puis score pondéré
  (voir `CLAUDE.md` §8)
- `DTOs/CandidatDto.cs` : les trois notes **et** les données brutes
- `GET /api/interventions/{id}/candidats`
- `Program.cs` : `AddScoped<AffectationService>()`

- `backend.Tests/AffectationTests.cs` : un test par filtre dur, un par note

**Validation** — 25 tests, sans attendre le jeu de données de l'étape 11 :
un agent proche et expérimenté sort en tête · un agent en absence, inactif, ou
déjà affecté sur un créneau chevauchant n'apparaît pas du tout · les trois notes
valent le chiffre calculé à la main, pas « plus de zéro » · Haversine donne
4,1 km entre Notre-Dame et la Tour Eiffel · une intervention inconnue renvoie
**404**, une intervention sans candidat renvoie **200** et une liste vide ·
`Agent` et `ClientSyndic` reçoivent **403**.

Treize sabotages du service ont été rejoués, chacun rattrapé par le ou les
tests attendus : exemption du même jour, filtre d'absence, comparaison au jour,
filtre `Actif`, passages à venir, pondération, chevauchement, rayon terrestre,
seuil de 30 km, charge pleine, note neutre, plafond de dix passages, lundi
comme premier jour de semaine.

⚠️ **Correction d'une faille de `CLAUDE.md` §8.** La règle de repos y était
écrite sans restriction de jour, ce qui interdit à un agent d'enchaîner deux
sites dans la même matinée — 30 minutes d'écart, donc moins de 11 h, donc
exclusion. §8 annonçait le symptôme (« si la liste des candidats revient vide »)
en l'imputant au jeu de données ; la cause était la règle. Le repos quotidien
sépare deux journées de travail, pas deux tâches d'une même journée.

---

# Étape 10 — Cycle de remplacement

**Périmètre**
- `Services/RemplacementService.cs` : `DetecterImpactsAsync`,
  `ProposerAsync`, `AccepterAsync`, `RefuserAsync`
- `DTOs/RemplacementActionDto.cs` : `ProposerDto { Guid AgentId }`
- `AbsencesController` : détection à la création + endpoint de rattrapage
  `POST /api/absences/{id}/detecter-impacts`
- `RemplacementsController` : `candidats`, `proposer`, `accepter`, `refuser`

- `backend.Tests/RemplacementTests.cs` : scénario complet et transitions
  invalides

**Validation** — 24 tests, chaque point du scénario vérifié :
détection déclenchée par la création de l'absence (1 remplacement) ·
idempotence (0 au second appel) · seules les interventions `Planifiee` sont
couvertes · chaque jour de l'absence est couvert · l'agent absent n'est pas
candidat à son propre remplacement · `accepter` et `refuser` sur statut
`Recherche` → **400** · proposition → `Propose` avec score · refus → retour
`Recherche` **sans réaffecter l'intervention** · proposition puis acceptation →
l'intervention **change réellement d'agent** · proposition et acceptation après
acceptation → 400 · proposition après refus acceptée · id inexistant → **404**
sur les trois actions · `ChefEquipe` lit les candidats mais reçoit **403** sur
`proposer`.

Dix sabotages rejoués, chacun rattrapé : garde d'idempotence, filtre
`Planifiee`, dernier jour de l'absence, `accepter` sans garde de statut,
`proposer` sans garde de statut, refus laissant `Refuse`, éligibilité à la
proposition, éligibilité à l'acceptation, acceptation qui n'écrit pas l'agent,
404 traduit en 400.

**Deux gardes ajoutées, que la spécification n'imposait pas explicitement.**
`proposer` refuse un agent écarté par un filtre dur au lieu de le proposer avec
un score nul — sinon les filtres durs sont décoratifs. Et `accepter` revérifie
l'éligibilité : entre la proposition et l'acceptation, une absence a pu être
saisie.

⚠️ **Faille trouvée à cette étape, corrigée à l'étape 10 bis.** `CLAUDE.md` §9
affirmait qu'`AccepterAsync` était le seul endroit du code où une intervention
change d'agent. C'était faux, et vérifié : le `PUT` écrivait `AgentId` après
avoir seulement vérifié que l'agent existe, et acceptait avec un `204` un agent
en arrêt maladie ce jour-là, absent de la liste des candidats. Un `Responsable`
pouvait donc double-affecter un agent, l'affecter pendant son arrêt, ou violer
son repos de 11 h, sans rien forcer ni forger.

---

# Étape 10 bis — Éligibilité sur toutes les écritures

**Périmètre**
- `Services/AffectationService.cs` : extraction d'un `Creneau`, plus
  `CreneauAsync` et `EstEligibleAsync`
- `Controllers/InterventionsController.cs` : garde sur `POST` et sur `PUT`
- `Services/RemplacementService.cs` : message d'inéligibilité mutualisé
- `backend.Tests/EligibiliteTests.cs`

**La garantie change de nature.** Elle n'est plus « un seul chemin écrit
l'agent » — intenable, et fausse — mais **« aucun chemin n'affecte un agent
inéligible »**. Le `PUT` continue donc d'accepter une réaffectation directe :
« Marie prend le site de Paul mardi », sans absence de Paul, est un geste
courant d'exploitation. Geler `AgentId` l'aurait interdit, et une règle qui gêne
le travail normal finit contournée par des suppressions-recréations, qui elles
ne passent par aucun contrôle.

**Trois points de conception**
1. Le moteur raisonne désormais sur un `Creneau` — site, coordonnées, jour,
   heure, durée — et non sur un identifiant d'intervention. Sans cela le `POST`
   restait hors de portée : quand on juge l'agent, la vacation n'a pas encore
   d'identifiant.
2. `EstEligibleAsync` passe par la liste complète des candidats au lieu de
   réimplémenter les quatre filtres. Une seconde implémentation finirait par
   s'écarter de la première, et c'est exactement cet écart qui laisse passer un
   agent inéligible.
3. La vérification porte sur l'horaire demandé et s'applique à chaque écriture
   portant un agent, pas seulement quand l'agent change : déplacer une vacation
   en gardant le même agent peut tout autant la rendre illégale. Une ligne déjà
   illégale ne peut donc pas être réenregistrée telle quelle — il faut la
   libérer d'abord, en envoyant `agentId` à null. C'est l'unique échappatoire,
   et elle ne contourne rien.

**Aucun drapeau de forçage.** Une échappatoire qui existe devient le chemin
normal dès la première fois qu'elle dépanne. Si le besoin d'affecter en urgence
un agent formellement inéligible se confirme, il mérite son propre geste tracé,
pas un paramètre sur le CRUD.

**Validation** — 13 tests : les quatre filtres durs refusés à la création · deux
sites dans la même journée toujours acceptés · une vacation sans agent toujours
acceptée · l'agent en arrêt maladie refusé sur le `PUT` alors qu'il l'était
accepté avant · la réaffectation légitime toujours possible · le déplacement qui
rend le titulaire inéligible refusé · le `PUT` qui ne touche pas à l'agent
toujours accepté · la libération toujours possible · le message nomme les quatre
causes · l'agent d'un autre tenant reste « inconnu » avant tout jugement.

Cinq sabotages rejoués, dont **l'erreur naturelle** : ne vérifier que lorsque
`AgentId` change. Elle est rattrapée par le test du déplacement d'horaire.

---

# Étape 11 — Endpoint planning

**Périmètre**
- `DTOs/PlanningDto.cs` : `PlanningSemaineDto`, `PlanningAgentDto`,
  `PlanningVacationDto`, `PlanningNonPourvueDto`
- `Services/PlanningService.cs` : agents en lignes, vacations par jour,
  agrégats de la semaine
- `GET /api/planning?lundi=AAAA-MM-JJ`, 400 si la date n'est pas un lundi

Une vacation est **non pourvue** si `AgentId` est null, ou si l'agent affecté
a une absence chevauchant la date.

⚠️ Piège `DateTime.SpecifyKind` — voir `CLAUDE.md` §10.

- `backend.Tests/PlanningTests.cs` : le jeu de référence et les bornes

**Validation** — 21 tests. Le jeu de référence annoncé est atteint exactement :
**4 agents, 17 vacations, 34 heures, 3 non pourvues**. Il est construit en
fixture et non en script SQL — un script de démonstration dérive du schéma sans
que rien ne le signale, alors qu'une fixture échoue le jour où le modèle change.

Semaine du lundi 7 au dimanche 13 septembre 2026, vacations de deux heures :
15 affectées à quatre agents, 2 jamais pourvues, et un arrêt maladie de Keita le
jeudi qui découvre la troisième. L'absence est saisie **après** les vacations,
comme dans la vie — posée avant, la garde d'éligibilité de l'étape 10 bis
refuserait à juste titre d'affecter Keita ce jour-là.

Autres points vérifiés : agents triés par nom · vacations triées par date puis
heure · le total ne s'obtient pas en additionnant les lignes, puisque deux
vacations n'ont pas d'agent · une vacation du dimanche est dedans, celle du lundi
suivant non · une vacation `Annulee` sort du planning et des agrégats · un agent
sans vacation garde une ligne vide, sinon la grille ne dit plus qui est libre ·
un agent devenu inactif qui porte encore une vacation garde sa ligne, sinon ses
heures comptent sans qu'aucune ligne ne les explique · le planning d'une autre
entreprise ignore tout · `Agent` et `ClientSyndic` reçoivent **403**.

Le planning et le cycle de remplacement décrivent le même trou : un test vérifie
que la vacation non pourvue de Keita est bien celle du `Remplacement` créé par la
détection d'impacts. Sans cela, l'écran montrerait une vacation à pourvoir que le
panneau de remplacement ignore.

Dix sabotages rejoués, dont **l'erreur naturelle** — calculer les totaux en
additionnant les lignes d'agents — qui casse sept tests, et le retrait du
`SpecifyKind`, qui en casse dix.

**`lundi` est obligatoire, sans valeur par défaut.** Une valeur calculée sur
l'horloge du serveur rendrait la réponse dépendante du moment de l'appel, donc
les tests dépendants du jour où ils tournent, et l'écran incapable de dire quelle
semaine il montre.

**Réservé aux rôles internes.** La grille nomme tous les agents de l'entreprise :
le filtre multi-tenant restreindrait bien les interventions d'un `Agent`, mais
pas la colonne des noms. Un agent qui doit consulter son propre planning aura
besoin d'un endpoint dédié — voir *Suite — non planifié*.

---

# Étape 12 — Durcissement de l'authentification

Placée avant le frontend, pour que celui-ci consomme directement le mécanisme
définitif au lieu d'être réécrit ensuite.

**Périmètre**
- **Révocation.** `Actif` et `SecurityStamp` vérifiés à chaque requête, pas
  seulement à la connexion. En l'état, désactiver un compte le laisse actif
  jusqu'à l'expiration du jeton, soit huit heures.
- Jeton d'accès court, quinze minutes, en cookie `httpOnly` `Secure`
  `SameSite=Strict`. Jeton de rafraîchissement persisté et révocable. Remplace
  le `localStorage`, qui exposait un porteur de huit heures à toute XSS.
- Protection CSRF, conséquence directe du passage au cookie
- `POST /api/auth/mot-de-passe-oublie` et `POST /api/auth/reinitialiser`, jeton
  à usage unique et de courte durée. Aucun parcours n'existe aujourd'hui : un
  responsable qui oublie son mot de passe n'a strictement aucune issue, et un
  administrateur qui le réinitialise à la main est pire.
- Limitation de débit sur `connexion`, `mot-de-passe-oublie` et `reinitialiser`,
  **par adresse et par compte**. Le verrouillage actuel est par compte :
  pulvériser un mot de passe sur cent comptes n'est ralenti par rien.
- `IdentityErrorDescriber` en français — les messages d'Identity traversent
  aujourd'hui l'API en anglais

⚠️ **Le courriel de réinitialisation suppose un service SMTP**, et `CLAUDE.md`
§2 interdit toute dépendance à un service tiers obligatoire. La configuration
SMTP doit donc rester optionnelle, et son absence produire une erreur explicite
au démarrage plutôt qu'un échec silencieux à l'envoi.

**Validation** : désactiver un compte coupe l'accès à la requête suivante. Un
jeton de rafraîchissement révoqué ne délivre plus rien. Le parcours de
réinitialisation fonctionne de bout en bout. La limitation de débit répond 429.
Tous vérifiés par des tests.

---

# Étape 13 — Frontend, socle

**Périmètre**
- `src/styles/tokens.css` : toutes les variables de `CLAUDE.md` §12,
  thème clair et sombre, reset minimal
- Google Fonts : Figtree et IBM Plex Mono dans `index.html`
- `src/api/client.ts` : wrapper fetch typé, `credentials: 'include'` — le
  jeton vit dans un cookie `httpOnly` posé à l'étape 12, le JavaScript n'y
  accède jamais. Rafraîchissement transparent sur 401, purge et redirection
  vers la connexion si le rafraîchissement échoue.
- `src/auth/` : `AuthContext` avec restauration de session, `RouteProtegee`
- `src/pages/Connexion.tsx`
- `src/layout/Navigation.tsx` : barre latérale 64 px, pastilles K, PL, TB,
  SI, AG, ST, déconnexion
- `src/layout/Disposition.tsx` et les routes dans `App.tsx`
- `src/composants/EnTetePage.tsx`

**Validation** : connexion, persistance après F5, navigation entre sections,
contraste suffisant sur les pastilles inactives.

---

# Étape 14 — Écran de planning

**Périmètre**
- `src/types/planning.ts`, `src/api/planning.ts`
- `src/utils/dates.ts` : `lundiDeLaSemaine`, `versChaineIso` (sans décalage
  de fuseau, ne pas utiliser `toISOString`), `numeroSemaine`
- `src/composants/` : `BlocVacation`, `CartesKpi`, `GrillePlanning`,
  `SelecteurSemaine`
- `src/pages/Planning.tsx`

Grille : colonne agent de 160 px puis cinq colonnes lundi–vendredi.
Nom du site lisible — ne pas tronquer au point de confondre deux résidences.
Agent absent en `--alerte` avec le mot « Absent ». Heures dépassant 30 en
`--retard`.

**Jeu de données de test**, à créer via l'API sous l'entreprise de
l'administrateur, semaine du **7 septembre 2026** :
1 client, 3 sites parisiens géolocalisés, 3 contrats, 3 prestations (120,
180, 90 minutes), 4 agents géolocalisés à distances croissantes,
17 interventions du lundi au vendredi dont **2 sans agent**, et 1 absence
maladie couvrant le mercredi.

**Validation** : la grille affiche les 4 agents, les KPI indiquent 17 / 34 /
3, la récurrence des contrats se lit d'un coup d'œil.

---

# Étape 15 — Panneau de remplacement

Le moteur devient visible et utilisable.

**Périmètre**
- `src/types/remplacement.ts`, `src/api/remplacements.ts`
- `src/composants/ListeNonPourvues.tsx` : sous la grille, une ligne par
  vacation non pourvue, avec sa raison (« Absence de X » ou « Aucun agent
  affecté ») et un bouton
- `src/composants/PanneauCandidats.tsx` : panneau droit 420 px, composant
  **présentatif** — il reçoit candidats, chargement, erreur et callbacks en
  props
- `src/pages/Planning.tsx` : c'est **la page** qui orchestre les appels —
  résolution ou création du remplacement, chargement des candidats,
  enchaînement proposer puis accepter, rechargement du planning

Chaque candidat : score sur 100 en IBM Plex Mono 26 px, nom, et en dessous
« X,X km · N passages · X h cette semaine ».

**Validation** : cliquer sur la vacation du mercredi ouvre le panneau avec
les candidats classés ; proposer un agent fait disparaître la vacation des
non pourvues et apparaître dans la ligne de cet agent, compteurs à jour.

---

# Étape 16 — Surface HTTP

**Périmètre**
- HTTPS : `UseHttpsRedirection`, HSTS, `RequireHttpsMetadata = true` hors
  développement — il vaut `false` aujourd'hui
- En-têtes : `Content-Security-Policy`, `X-Content-Type-Options`,
  `Referrer-Policy`, `X-Frame-Options`
- CORS : politique nommée, origines déclarées par configuration. Il n'y a rien
  aujourd'hui ; le proxy Vite masque le problème en développement.
- Limite de taille de corps de requête et délai d'expiration
- `/api/sante` enrichi d'un état de la base, pour les sondes de disponibilité

**Validation** : une origine non déclarée est refusée. Les en-têtes sont
présents. Une requête HTTP est redirigée hors développement.

---

# Étape 17 — Données personnelles

Le produit détient, sur des salariés : nom, adresse, **coordonnées GPS du
domicile**, date d'embauche, et **arrêts maladie avec un motif en texte libre**.
Un motif d'arrêt maladie est une donnée de santé, catégorie particulière au sens
de l'article 9 du RGPD.

L'arbitrage le plus lourd est déjà tranché : le motif libre est interdit sur les
arrêts maladie depuis l'étape 8. Ce qui reste ici est du mécanisme, pas de la
décision produit.

**Périmètre**

- **Journal d'accès** : qui a lu quoi, sur les données d'agents et d'absences.
  Il n'existe pas.
- **Anonymisation.** La clé étrangère `Restrict` sur `Intervention.AgentId` rend
  un agent impossible à supprimer dès qu'il porte une intervention. C'est le bon
  choix pour l'intégrité du planning, et il entre en conflit direct avec une
  demande d'effacement. Concevoir un chemin d'anonymisation — nom, adresse et
  coordonnées remplacés par des valeurs neutres, ligne conservée — plutôt qu'une
  suppression.
- Durées de conservation, par nature de donnée
- Registre des traitements et cartographie des données

⚠️ Stocker les coordonnées du domicile d'un salarié pour le calcul de distance
relève de la doctrine CNIL sur la géolocalisation des employés : c'est
défendable — ça sert l'affectation, pas la surveillance — mais ça demande de la
transparence et de la documentation, pas du code. Une AIPD est probablement
requise. **Ce fichier ne remplace pas une relecture juridique.**

---

# Étape 18 — Exploitation et déploiement

**Périmètre**
- Journalisation structurée avec identifiant de corrélation. `CLAUDE.md` §11 dit
  « pas de logging ajouté » : c'était le bon choix pour un prototype, ça ne
  l'est plus pour un produit qu'on doit diagnostiquer à distance.
- `docker-compose.yml` : PostgreSQL, API, frontend servi. En AGPL les clients
  auto-hébergent — ce n'est pas du confort d'exploitation, c'est une
  fonctionnalité du produit.
- Sauvegarde et restauration documentées, et procédure de retour arrière de
  migration
- Secrets de production : variables d'environnement, et documentation de ce qui
  doit être tourné, quand et comment

**Validation** : un inconnu déploie en suivant le fichier, sans aide.

---

# Étape 19 — Cadrage du chef d'équipe par périmètre

Placée en dernier, et volontairement : le modèle à retenir dépend d'une
information qu'aucun raisonnement ne produit, et qui vient d'un pilote chez un
client réel — donc après les étapes 16 à 18.

**La question à poser, écrite ici pour que personne ne la devine plus tard :**

> Chez vous, le périmètre d'un chef d'équipe est-il un ensemble de **personnes**
> ou un ensemble de **bâtiments** ?

Les deux existent dans le métier, et le modèle n'est pas le même :

| Réponse | Modèle | Coût |
|---|---|---|
| Des personnes — il encadre douze agents mobiles | `Agent.ChefEquipeId`, auto-référence nullable | une colonne |
| Des bâtiments — il tient un secteur de huit copropriétés | `ChefEquipeSite` **plus** `AgentSite` | deux tables |

⚠️ `AgentSite` seule ne cadre pas un chef d'équipe : elle relie des agents à des
sites, elle ne dit pas quel chef supervise qui. Il faut dans tous les cas un
lien chef → sites ou chef → agents. C'est ce qui rend la question préalable, et
non postérieure, au choix du modèle.

**Périmètre** — une fois la réponse connue
- le modèle retenu, sa migration présentée pour relecture
- le filtre de périmètre appliqué côté requête, comme pour `ClientSyndic`
- l'écran de planning limité au périmètre du chef connecté

**Validation** : un chef d'équipe ne voit que son périmètre ; un responsable
voit toujours tout. Vérifié par des tests.

⚠️ Jusqu'à cette étape, un chef d'équipe voit le planning de toute son
entreprise. C'est un inconfort — cent cinquante lignes au lieu de douze — et non
une faille : depuis l'étape 8 il n'accède à rien qu'un autre salarié de la même
entreprise ne puisse légitimement connaître, le motif des arrêts maladie n'étant
plus collecté. Le seul axe qui est une véritable frontière de sécurité reste
`ClientSyndic`, traité à l'étape 8.

---

# Suite — non planifié

Par ordre de valeur, à cadrer le moment venu.

**Double authentification** pour les rôles Administrateur et Responsable.

**RLS PostgreSQL** en défense en profondeur : si un filtre global est oublié
sur une entité future, la base refuse quand même. Ceinture et bretelles sur le
seul invariant dont une erreur est catastrophique.

**Test d'intrusion externe** avant la première mise en production réelle.

**Génération automatique des interventions** depuis les contrats. Sans elle,
le produit est inutilisable en conditions réelles. À décider : horizon
glissant, comportement lors d'une modification de contrat, jours fériés.

**Écran agent mobile (PWA).** Une action par écran, cibles 56 px. Voir son
intervention → consignes → démarrer → checklist → anomalie avec photo →
terminer. Suppose les entités `Checklist` et `Anomalie`. Suppose aussi un
`GET /api/planning/moi` : le planning général est réservé aux rôles internes
parce que sa grille nomme tous les agents de l'entreprise, et le filtre
multi-tenant ne restreint pas cette colonne.

**Module stock.** L'entité pivot est `PrestationProduit` (PrestationId,
ProduitId, QuantiteParIntervention). Sans elle, le stock se réduit à un
inventaire sans valeur. Ensuite : consommation prévisionnelle, alertes
🟢🟠🔴, proposition de réapprovisionnement.

**Rattachement agent ↔ site.** Table `AgentSite` avec `EstTitulaire`. Sert au
filtrage et au périmètre du chef d'équipe. Ne remplace pas le calcul
d'expérience du moteur, fondé sur l'historique réel.

**Certification des passages.** Couche d'abstraction
`ICertificationProvider` plutôt qu'un couplage direct à un prestataire.
Règle absolue : ne jamais considérer une intervention comme réalisée au seul
motif qu'elle était planifiée.

**Jeu de démonstration installable.** Le `README` doit permettre à un inconnu de
faire tourner le projet ; au premier lancement il n'a rien à regarder. Le jeu de
référence existe en fixture de test — `PlanningTests.Semer()`, 4 agents et
17 vacations — mais il n'est pas installable dans une base de développement. À
cadrer avec le frontend, pas avant : c'est l'écran qui dira ce qu'il faut
montrer. Forme à trancher alors, et le choix n'est pas neutre — un script SQL
dérive du schéma en silence, une commande qui rejoue les mêmes appels HTTP que la
fixture reste vraie par construction.

**Portail conseil syndical.** Outil de transparence et de preuve, pas un ERP.

**Module devis** avec catalogue de prestations, **sans IA** : des règles
explicites (tarif au m², coefficients de fréquence, majorations, temps
standard) donnent le même résultat qu'un modèle opaque, mais **auditables**.
Quand un client demande « pourquoi ce montant ? », on peut répondre.

---

## Décisions prises

À ne pas rejouer sans raison nouvelle.

| Sujet | Décision | Motif |
|---|---|---|
| Licence | AGPL v3 | Protège l'intention de gratuité contre la revente propriétaire |
| Langage backend | C# | VB.NET ne supporte plus les templates web .NET |
| Frontend | React + TypeScript | Écosystème, PWA, qualité du code généré |
| Base | PostgreSQL | Gratuit, portable, non lié à Microsoft |
| Multi-tenant | Dénormalisation d'`EntrepriseId` | Évite trois jointures par ligne sur la vue planning |
| Tenant | Issu du token JWT | Pas d'en-tête HTTP, pas de paramètre de requête |
| Token | Cookie `httpOnly`, accès 15 min, rafraîchissement révocable | Remplace localStorage à l'étape 12. L'ancienne décision portait sa propre réserve : « à durcir avant exposition publique ». La cible étant devenue un produit tiers, la réserve s'applique |
| Couleur de marque | Ardoise `#3F4A54` | Le secteur est saturé de bleu `#2563EB` |
| Blocs de planning | Une seule couleur | Une couleur par agent devient illisible au-delà de dix agents et brûle la palette |
| Compétences | Hors V1 du moteur | L'entité n'existe pas ; ne pas bloquer le moteur dessus |
| Remplacement automatique | Non implémenté | Sans notifications ni application agent, il n'y a personne à contacter |
| `Stock.EmplacementId` | Sans clé étrangère | Peut pointer vers un dépôt, un véhicule ou un site |
| Écriture inter-tenant | `EntrepriseId` forcé sans condition, retiré des `CreateDto` | Le mécanisme conditionnel laissait passer un identifiant forgé valide, donc l'écriture chez un concurrent |
| Pagination | `?page&taille` plafonnée, réponse enveloppée | Onze endpoints renvoyaient tout : épuisement de ressources, et forme à figer avant d'écrire les écrans |
| Base de test | PostgreSQL dédiée `klean_test` | Le fournisseur InMemory ne reproduit ni les filtres globaux, ni les contraintes, ni le refus des `DateTime` non datés — ce qu'on veut justement tester |
| Cadrage `ChefEquipe` | Par le verbe à l'étape 8, par le périmètre à l'étape 19 | Le modèle dépend d'une information métier — périmètre en personnes ou en bâtiments — qu'un client donne et qu'un raisonnement ne produit pas. Deviner, c'est risquer une migration sur une table peuplée |
| `Absence.Motif` | Interdit sur `Maladie`, conservé sur les autres types | Le champ est indispensable pour `Autre` et utile pour `Indisponibilite` : le supprimer pousserait à écrire le motif ailleurs. Restreindre l'accès par rôle serait plus faible — ça conserve la donnée de santé en ajoutant une porte devant, alors que la minimisation dit de ne pas la collecter. En AGPL auto-hébergé, le produit doit être sûr par défaut : aucun DPO en aval ne relira le schéma |
| Paramètre `lundi` | Obligatoire, aucune valeur par défaut | Une valeur tirée de l'horloge du serveur rend la réponse dépendante du moment de l'appel et les tests dépendants du jour où ils tournent |
| Agrégats du planning | Calculés par le serveur | Deux vacations sans agent n'ont pas de ligne : un client qui additionne les lignes trouve 30 heures au lieu de 34, et deux clients finissent par ne pas afficher le même total |
| Lignes d'agents | Tous les agents actifs, plus les inactifs portant encore une vacation | Une grille sans ligne vide ne dit pas qui est libre ; un inactif dont les heures comptent sans ligne rend le planning non additionnable |
| Vacation `Annulee` | Hors du planning et des agrégats | Ce n'est pas du travail, et elle sort déjà des filtres du moteur d'affectation |
| Jeu de démonstration | En fixture de test, pas en script SQL | Un script de démonstration dérive du schéma sans que rien ne le signale ; une fixture échoue le jour où le modèle change |
| Filtres durs | Appliqués à toute écriture portant un agent, pas seulement au cycle | `PUT /api/interventions` les contournait : un agent en arrêt maladie était accepté avec un 204. La garantie utile est « aucun chemin n'affecte un agent inéligible », pas « un seul chemin écrit l'agent » |
| Réaffectation directe | Reste autorisée sur le `PUT` | Geler `AgentId` interdirait « Marie prend le site de Paul mardi », geste courant sans absence. Une règle qui gêne le travail normal finit contournée par des suppressions-recréations, hors de tout contrôle |
| Forçage d'affectation | Aucun drapeau | Une échappatoire qui existe devient le chemin normal dès qu'elle dépanne. Un besoin d'urgence réel mériterait un geste tracé, pas un paramètre sur le CRUD |
| Vérification d'éligibilité | Passe par la liste complète des candidats | Une seconde implémentation des quatre filtres s'écarterait de la première, et c'est l'écart qui laisse passer un agent inéligible |
| Détection d'impacts | Automatique à la création de l'absence, rejouable par endpoint | C'est l'interconnexion qui fait le produit. Une détection à déclencher à la main est une détection oubliée |
| Proposition | Refuse un agent écarté par un filtre dur | Sans cette garde, les filtres durs sont décoratifs : il suffit de proposer à la main l'agent en arrêt maladie |
| Acceptation | Revérifie l'éligibilité avant d'écrire | Entre la proposition et l'acceptation, une absence a pu être saisie |
| Refus | Ramène en `Recherche` et efface l'agent | Le statut `Refuse` n'est jamais produit par le service. Limite V1 : sans table d'historique, le refus ne laisse pas de trace |
| Repos de 11 h | Entre deux journées de travail, pas entre deux vacations du même jour | La règle sans restriction de jour interdisait d'enchaîner deux sites dans une matinée et vidait la liste des candidats sur tout planning réel. L'article L3131-1 porte sur le repos quotidien |
| Expérience du site | Passages antérieurs seulement | Compter les passages à venir récompenserait le fait d'être déjà planifié, pas la connaissance du site |
| Liste de candidats | Non paginée | Bornée par le nombre d'agents actifs, pas par la requête ; tronquer un classement le rend faux |
| Candidats | Réservés aux rôles internes | La liste nomme des agents, et la matrice ne donne aucune lecture sur les agents à `Agent` ni à `ClientSyndic` |
| Linter frontend | oxlint, pas ESLint | Couvre `exhaustive-deps` et `jsx-a11y`, les deux familles utiles ici ; une devDep au lieu de six, cohérent avec la discipline de dépendances. Limite acceptée : pas de règle personnalisée en JavaScript |
