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

**Décision : on garde fermé, avec un déclencheur précis.** Créer un contrat est un
acte **commercial**, pas opérationnel : un contrat porte des dates et une
fréquence, c'est l'assiette de la facturation. Ouvrir sa création à l'exploitation,
c'est laisser l'exploitation créer du périmètre facturable — le même raisonnement
qui a fait retirer `POST /api/entreprises`. Et l'asymétrie commande : ouvrir un
droit plus tard est indolore, le refermer après six mois d'usage est une régression
que les utilisateurs combattront.

Mais le vrai risque est l'**inverse** de celui que ce paragraphe anticipait. Dans
une entreprise de huit personnes, le responsable *est* celui qui fait tout. Une
matrice trop serrée ne produit pas une demande d'évolution, elle produit **un mot
de passe administrateur partagé** — et là toute la traçabilité est perdue, pas
seulement une ligne de matrice.

D'où un déclencheur mesurable plutôt qu'une intuition. Au premier pilote, la
question à poser n'est pas « veux-tu créer des sites ? » mais **« combien de
personnes connaissent le mot de passe du compte administrateur ? »**. Si la réponse
dépasse un, ouvrir `Sites` et `Contrats` au `Responsable` le jour même.

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
**Décidé : oui, à l'étape 17, et l'interdiction de motif doit suivre.**

Le raisonnement n'est pas juridique, il est mécanique : **l'absence de cette valeur
pousse activement la donnée de santé vers la prose.** Aujourd'hui un accident du
travail se saisit soit en `Maladie` — motif interdit, donc l'information est perdue
— soit en `Autre`, où le motif est non seulement permis mais indispensable.
Quelqu'un écrira « chute dans l'escalier du hall B », ce que la règle sur `Maladie`
existe précisément pour empêcher, contourné par le seul chemin resté ouvert.

`AccidentTravail` doit donc **hériter de l'interdiction de motif** : même catégorie
de l'article 9. Ajouter la valeur sans étendre la règle créerait un nouveau canal
de texte libre pour de la donnée de santé, donc serait pire que le statu quo.

Pourquoi l'étape 17 et pas maintenant : aucun formulaire d'absence n'existe dans
l'application, les seuls écrivains sont l'API et le script de démonstration, donc
personne ne peut rien saisir. L'étape 17 inventorie les données personnelles et
prépare l'AIPD : la valeur, l'extension de la règle et sa justification écrite
atterrissent ensemble. Faite aujourd'hui, ce serait une modification de dix
minutes orpheline de son motif.

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

**Lot A — session révocable.** Livré.

- `Models/JetonRafraichissement.cs`, migration `SessionsRevocables` : une table,
  deux index, aucune opération destructrice
- `Services/ServiceSession.cs` : émission, rotation, révocation, cookies
- `Middleware/VerificationCsrf.cs` : double soumission
- `Program.cs` : lecture du cookie, contrôle `Actif` et `SecurityStamp`
- `Controllers/AuthController.cs` : `connexion`, `rafraichir`, `deconnexion`
- `backend.Tests/SessionTests.cs` : 21 tests

Trois points hors du périmètre littéral, qui découlent de ses décisions :
`rafraichir` (sans quoi le jeton de rafraîchissement ne sert à rien),
`deconnexion` (sans quoi « révocable » reste théorique), et **l'abandon de
l'en-tête `Authorization`** — aucun jeton n'étant plus remis au client, personne
ne pourrait s'en servir, et un second chemin d'authentification est un second
chemin à auditer.

⚠️ **Une affirmation fausse, corrigée par un test.** J'ai d'abord écrit que poser
`contexte.Token` depuis le cookie suffisait à fermer l'en-tête `Authorization`.
C'est faux : `JwtBearerHandler` retombe sur l'en-tête quand `Token` reste vide. Le
chemin `Bearer` restait donc grand ouvert, silencieusement. Corrigé par un
`NoResult()` explicite, consigné dans `CLAUDE.md` §10.

⚠️ **Fenêtre assumée après déconnexion.** Un JWT signé vaut jusqu'à son
expiration : rien côté serveur ne le rétracte. La déconnexion efface les cookies
et révoque le jeton de rafraîchissement — un porteur normal est bien déconnecté —
mais un jeton d'accès **déjà exfiltré** reste utilisable au plus quinze minutes,
contre huit heures avant cette étape. Deux tests figent la propriété et son
contrepoint : la **désactivation du compte**, elle, ferme immédiatement même un
jeton copié, et c'est la raison d'être du contrôle par requête.

Fermer cette fenêtre demanderait de porter l'identifiant de session dans le jeton
et de la vérifier à chaque requête, soit une **seconde** lecture par requête en
plus de celle sur `AspNetUsers`. Le gain ne concerne que le cas où un jeton a déjà
été volé, pendant au plus quinze minutes.

**Décidé : pas de lecture par requête. Une déconnexion globale explicite, à
l'étape 16.**

Précisons ce que la fermeture achèterait. Un attaquant copie les deux cookies à T,
l'utilisateur se déconnecte à T+1 : le jeton de rafraîchissement est révoqué, donc
mort — et son rejeu couperait tout. Reste le jeton d'accès, au plus quatorze
minutes. L'exposition est donc réelle. Mais pour avoir copié ces cookies il fallait
du code avec accès au magasin de cookies : `HttpOnly` interdit à une XSS de les
lire, `SameSite=Strict` et l'en-tête anti-CSRF interdisent l'usage inter-site.
Donc appareil ou profil compromis — et un adversaire avec un tel accès n'attend
pas, il recopie les cookies de la session suivante. On paierait une seconde lecture
en base sur **chaque requête, pour toujours**, pour retirer quatorze minutes à
quelqu'un qui a un accès persistant.

Or le besoin réellement exprimé — « on a volé le portable de Marie » — est **déjà
couvert, et immédiatement** : `Actif = false` coupe à la requête suivante. C'est ce
que ce paragraphe appelle lui-même la raison d'être du contrôle par requête.

La réponse est donc une action **« déconnecter tous mes appareils »** qui régénère
le `SecurityStamp`. Le chemin existe déjà — la réinitialisation de mot de passe le
fait. Coût récurrent : **zéro**. Effet : la fenêtre se ferme **entièrement**, et au
moment précis où l'utilisateur a une raison de le demander. Qu'elle déconnecte tous
les appareils n'est pas un effet de bord, c'est la sémantique correcte de ce bouton.
Le `sessionId` par requête achète moins, plus cher.

**Validation du lot A** — 21 tests. Désactiver un compte coupe l'accès à la
requête suivante · changer le mot de passe invalide les jetons déjà émis · un
compte désactivé ne peut plus rafraîchir · la rotation change le jeton sans
repousser l'échéance absolue · rejouer un jeton coupe **toute** la session ·
un jeton expiré ne délivre plus rien · la déconnexion révoque en base et efface
les trois cookies · les attributs `HttpOnly`, `Secure`, `SameSite=Strict` sont
vérifiés un par un · le cookie anti-CSRF est le seul lisible par le script · le
cookie de rafraîchissement est limité à `/api/auth` · une écriture sans en-tête
anti-CSRF donne **403**, avec en-tête **201** (témoin) · une lecture et la
connexion en sont dispensées · l'en-tête `Authorization` n'est plus accepté.

Onze sabotages rejoués, chacun rattrapé : contrôle `Actif`, contrôle
`SecurityStamp`, `NoResult()`, détection de rejeu, échéance absolue, `HttpOnly`,
`SameSite`, chemin du cookie de rafraîchissement, révocation à la déconnexion,
vérification CSRF désactivée, vérification CSRF étendue aux lectures.

**Lot B — mot de passe oublié, débit, français.** Livré.

- `Services/IServiceCourriel.cs`, `ServiceCourrielSmtp.cs` : SMTP **optionnel**
- `Services/LimiteurDebit.cs`, `PlafondsDebit` : fenêtre glissante
- `Services/DescripteurErreursFrancais.cs`
- `Controllers/AuthController.cs` : `mot-de-passe-oublie`, `reinitialiser`
- `appsettings.example.json` : clés `Courriel` et `Frontend`
- `backend.Tests/MotDePasseTests.cs`, `LimitationDebitTests.cs`

**SMTP optionnel, en trois modes**, tous les trois vérifiés sur l'application
réelle. *Absent* : démarrage normal, avertissement nommant les clés à renseigner
et les endpoints désactivés, puis **503** avec la marche à suivre. *Incomplet* :
**échec au démarrage** — « pas configuré » est un choix d'exploitation,
« configuré à moitié » est une erreur, et la découvrir quand un responsable a
perdu son mot de passe est le pire moment. *Complet* : démarrage et journal de la
configuration effective.

**Deux dimensions de limitation, pas une.** Le verrouillage d'Identity est par
compte : cinq échecs, quinze minutes. Il ne ralentit donc **en rien** le scénario
inverse — pulvériser un seul mot de passe probable sur cent comptes, un essai
chacun. Les deux dimensions ont leur test, parce qu'une seule des deux laisse un
trou entier. Une tentative refusée n'est pas comptée : sinon un client bloqué se
prolonge indéfiniment sa propre punition en réessayant.

⚠️ **`[MinLength]` retiré des DTOs de mot de passe.** La validation de modèle
d'ASP.NET s'exécute avant le contrôleur, donc avant Identity, et renvoyait son
message **en anglais** — que le descripteur français ne peut pas atteindre. Elle
dupliquait en plus une règle dont Identity est l'autorité : deux longueurs
minimales finissent par diverger. Trouvé par le test du mot de passe trop court,
qui attendait du français et recevait un `ProblemDetails` anglais.

⚠️ **`IServiceCourriel` déroge à `CLAUDE.md` §11.** C'est la deuxième interface de
service du projet, avec `ITenantContext`, et la justification est la même : la
substitution est nécessaire, aucun test ne doit envoyer de courriel.
L'alternative — une classe concrète qui « n'envoie pas vraiment » selon la
configuration — ferait dépendre le comportement de production d'un réglage, sur
un chemin d'authentification. Inscrit dans §11 plutôt qu'introduit en silence.

**Validation du lot B** — 21 tests. Parcours complet : demande → courriel → jeton
→ nouveau mot de passe, l'ancien refusé · le lien porte l'adresse et le jeton
encodés, et le jeton décodé est accepté · le jeton ne sert qu'une fois · la
réinitialisation révoque **toutes** les sessions, accès et rafraîchissement · une
adresse inconnue et un compte désactivé reçoivent **202 sans courriel** · une
adresse inconnue et un jeton faux donnent le **même** message · le 503 ne dépend
pas de l'adresse saisie · les règles de mot de passe et le doublon d'adresse sont
en français · **429** par compte et par adresse, sur `connexion` et sur
`mot-de-passe-oublie` · le plafond par compte bloque aussi le bon mot de passe ·
un autre compte n'est pas bloqué par le premier · le message de 429 ne nomme pas
la dimension atteinte.

Onze sabotages rejoués, chacun rattrapé : 404 au lieu de 202 sur adresse
inconnue, courriel envoyé à un compte désactivé, jeton invalide distingué du
compte inconnu, sessions non révoquées, SMTP absent ignoré, limitation retirée de
la connexion, dimension par compte retirée, limitation retirée du mot de passe
oublié, message de 429 nommant la dimension, refus comptés, descripteur français
retiré.

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

# Étape 14 bis — Filet de sécurité frontend

Deux étapes d'écran sont livrées et l'étape 15 sera la **première à écrire depuis
le navigateur** — `proposer`, `accepter`, `refuser`. Jusqu'ici le frontend n'a
fait que des lectures, plus la connexion et la déconnexion : le chemin de
l'en-tête anti-CSRF sur une écriture métier n'a jamais été exercé. Le moment de
poser un filet est avant, pas après.

**Périmètre**
- `frontend/scripts/verifier-interface.mjs` : conformité mécanique à §12
- `frontend/scripts/tests.mjs` : lanceur, rejoue les dates dans cinq fuseaux
- `frontend/src/utils/dates.test.ts`, `frontend/src/api/client.test.ts`
- `frontend/tsconfig.test.json`, plus la référence et l'exclusion
- `package.json` : `test`, `verifier`, `engines`
- `.github/workflows/ci.yml` : les deux commandes
- `README.md`

**Aucune dépendance ajoutée.** Node 22.18 dépouille les types TypeScript sans
drapeau et porte son propre lanceur de tests : ni `vitest`, ni `jsdom`, ni
bibliothèque de rendu. `CLAUDE.md` §3 n'autorise que `react-router-dom`, et il n'y
avait rien à négocier.

**Deux modules, et deux seulement** — ceux où un défaut est cher et invisible.
`utils/dates.ts` pour les deux pièges de date, `api/client.ts` pour la session.
Les composants, le rendu et le CSS ne sont pas testés : le rendu se valide au
navigateur, et pour §12 le vérificateur fait mieux qu'un test de rendu — il mesure
là où l'œil ne distingue pas 4,44:1 de 4,52:1.

⚠️ **La série des dates doit tourner dans plusieurs fuseaux, et c'est la raison
d'être du lanceur.** Les deux pièges sont symétriques : `toISOString` casse aux
décalages **positifs**, `new Date('AAAA-MM-JJ')` aux **négatifs**. Les deux
sabotages ont été joués, et **aucun des deux ne se voit sous `TZ=UTC`** — le
fuseau par défaut de l'exécuteur d'intégration continue. Une série qui ne tourne
qu'en UTC validerait donc un code faux dans les deux sens.

**Validation** — 20 sabotages joués, 20 détectés. Le jeton anti-CSRF mémorisé au
lieu d'être relu · la garde « un seul vol » retirée · `credentials` omis ·
l'en-tête envoyé sur les lectures · la connexion soumise au rafraîchissement · la
purge non signalée · la requête non rejouée · les cinq altérations de `dates.ts` ·
et sept manquements à §12, de la couleur en dur au contraste dégradé.

Un constat en passant : le sabotage de la branche `status === 204` de `client.ts`
n'a rien fait rougir, parce qu'elle était **redondante** — un corps vide rendait
déjà `undefined`. Ligne retirée. Une ligne qu'on ne peut pas casser trompe le
lecteur sur ce qui compte.

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

⚠️ **Le périmètre a débordé sur le backend, et il le fallait.** La « résolution
ou création du remplacement » demandée à la page n'était pas réalisable
proprement côté client : `GET /api/remplacements` n'a **aucun filtre**, plafond
200 par page, donc retrouver le remplacement d'une vacation demandait de balayer
les pages — et `POST /api/remplacements` ne garde rien contre le doublon, donc
deux responsables cliquant la même vacation en créaient deux, dont un restait en
`Recherche` sans être visible nulle part : les vacations non pourvues viennent du
planning, pas de cette table.

D'où **`POST /api/interventions/{id}/remplacement`**, idempotent : 200 quand un
remplacement actif existait, 201 quand il vient d'être créé, même corps dans les
deux cas. Un seul aller-retour, pas de course. La règle d'unicité est
`RemplacementService.StatutsActifs`, celle de la détection d'impacts — deux règles
d'unicité auraient divergé. Le décompte passe à **54 endpoints métier**, plus 3
d'authentification.

⚠️ **Le cas d'échec d'`accepter` est un cul-de-sac, et l'écran doit le défaire.**
`AccepterAsync` **revérifie** l'éligibilité : entre la proposition et
l'acceptation, une absence a pu être saisie. Si elle refuse, le remplacement reste
en `Propose` — et `ProposerAsync` n'accepte que `Recherche` ou `Refuse` en entrée.
Aucun autre agent ne pourrait plus être proposé sur cette vacation, définitivement.
La page appelle donc `refuser` sur l'échec, ce qui la ramène en `Recherche`, avant
de remonter l'erreur. C'est la seule raison pour laquelle `refuser` figure dans
`src/api/remplacements.ts` alors que l'écran n'a pas de bouton de refus.

**Vérification de bout en bout** — la séquence de `Planning.tsx` rejouée contre le
serveur sur le jeu de démonstration : ouverture idempotente sur la vacation du
mercredi (200, même identifiant au rappel), classement décroissant avec l'agent
absent écarté, `proposer` puis `accepter`, puis la vacation quitte les non
pourvues (3 → 2), apparaît sur la ligne du remplaçant (2 → 3 vacations, 6 → 7,5 h),
les totaux de la semaine ne bougent pas (17 / 34), et plus aucun « Absent » sur la
ligne de l'agent absent. Plus 10 tests backend sur l'endpoint et 7 sabotages, tous
détectés.

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
- **Déconnexion de tous les appareils** : régénère le `SecurityStamp`, donc ferme
  entièrement la fenêtre de quinze minutes du lot A de l'étape 12, sans aucune
  lecture supplémentaire par requête. Décision consignée à cette étape-là.

**Trois dettes que cette étape ramasse, et qui sont déjà annotées dans le code :**

1. `frontend/vite.config.ts` retire l'attribut `Secure` des `Set-Cookie` au passage
   du proxy, parce que Safari refuse un cookie `Secure` reçu en clair sur
   `http://localhost` là où Chrome et Firefox font une exception pour cette origine.
   Le contournement est confiné au serveur de développement, qui n'existe pas en
   production — mais il disparaît avec le HTTPS local de cette étape.
2. La `Content-Security-Policy` doit autoriser `fonts.googleapis.com` en
   `style-src` et `fonts.gstatic.com` en `font-src`. `frontend/index.html` charge
   Figtree et IBM Plex Mono de là, et tout le système typographique de §12 en
   dépend. Sans ces directives les polices sont bloquées en production.
3. `RequireHttpsMetadata` vaut `false` aujourd'hui.

**Validation** : une origine non déclarée est refusée. Les en-têtes sont
présents. Une requête HTTP est redirigée hors développement.

**Fait.** 20 tests — 16 dans `SurfaceHttpTests.cs`, 4 dans `SessionTests.cs` pour
la déconnexion totale — et 14 sabotages, dont deux enseignements.

**Ce que l'étape a décidé, et qui ne figurait pas dans le périmètre.**

*Il y a deux politiques de sécurité du contenu, pas une.* Le périmètre les
confondait. L'API ne sert que du JSON : sa politique est `default-src 'none'`, la
plus stricte possible, et les origines de polices de la dette n°2 n'ont rien à y
faire — elles appartiennent à la politique du **document**, que l'API ne sert pas.
Les écrire dans la politique de l'API l'aurait affaiblie pour un besoin qu'elle
n'a pas. La politique du document vit donc dans `frontend/csp.ts`, source unique
lue par `vite.config.ts`, avec une variante de développement — le serveur de Vite
exige `'unsafe-inline'` et `ws:`, et une politique unique aurait dû affaiblir la
production pour le confort du développement. `npm run preview` sert la variante de
production : c'est le seul endroit où l'on constate qu'elle ne bloque rien avant de
déployer. Un en-tête écrit dans un `nginx.conf` aurait été invisible du dépôt et
aurait divergé au premier `index.html` modifié.

*La dette n°1 se paie côté Vite, pas côté backend.* Ce qui décide du sort d'un
cookie `Secure` est l'origine que voit le **navigateur** ; le saut proxy → backend
est en boucle locale sur la même machine et reste en clair. `vite dev` sert donc la
page en https avec le certificat de développement .NET exporté en PEM — zéro
dépendance npm — et le bloc qui retirait `Secure` a disparu.

*Deux gardes sur la taille du corps.* `TestServer` ignore les options de Kestrel :
la limite y était invisible, et la série a vu un corps de deux mégaoctets lu et lié
en entier avant d'être refusé par un `[MaxLength(200)]` — après l'allocation que la
limite existe pour éviter. D'où `LimiteCorpsRequete`, qui refuse sur
`Content-Length` quel que soit le serveur. Kestrel reste nécessaire pour un corps
en fragments, qui n'annonce pas sa taille. Les deux sont prouvées contre le vrai
serveur : 413 avant l'authentification dans un cas, 413 de Kestrel pendant la
lecture dans l'autre, 201 pour le témoin.

**Deux enseignements des sabotages.**

*Un témoin peut être vert pour la mauvaise raison.* « La déconnexion totale ne
touche pas les autres comptes » vérifiait le `GET /api/auth/moi` de l'autre compte
— or révoquer un jeton de rafraîchissement ne périme pas un jeton d'accès, qui est
signé et vaut quinze minutes. Retirer la clause de compte de `RevoquerToutAsync`
laissait le test vert. Le témoin porte désormais sur `POST /api/auth/rafraichir`.

*`[Authorize]` seul est retirable de `deconnexion-totale`.* Le sabotage reste vert,
parce que le contrôle d'identifiant du contrôleur couvre le même cas — et
`OnTokenValidated` refuse déjà un jeton dont l'identifiant n'est pas exploitable.
Retirer les deux gardes passe au rouge. La propriété est donc bien tenue, deux
fois ; les deux sont conservées, `[Authorize]` parce que c'est la surface qu'on
audite, le contrôle par symétrie avec `Moi()`.

**Ce qui reste ouvert.** L'endpoint `deconnexion-totale` n'a pas de bouton :
aucun écran de compte n'existe. Sa place est à l'étape 17, avec les écrans de
données personnelles. En attendant il est appelable, et c'est déjà ce qui manquait.

**Non vérifié.** L'absence de violation de la CSP de production **dans un
navigateur** n'a pas pu être constatée — extension indisponible. Ce qui est prouvé
est statique : la construction ne contient aucun script ni style en ligne, son CSS
ne référence aucune ressource externe, et chacune des cinq ressources du document
est couverte par une directive.

---

# Étape 16 bis — Écrans de données de référence

**Ce qui manque n'est pas du confort, c'est la mise en route.** À la fin de
l'étape 16, le backend expose 54 endpoints et l'interface n'en sert que le
planning et le remplacement. Personne ne peut créer un client, un site, un contrat
ni un agent autrement qu'en tapant dans l'API ou dans Swagger. Pour un produit mis
à disposition d'entreprises tierces, ce n'est pas une lacune, c'est un empêchement
de déployer : le script `backend/Scripts/jeu-de-demonstration.py` est aujourd'hui
le **seul** chemin qui alimente KLEAN.

⚠️ Et une absence ne se saisit pas non plus. Conséquence directe : le panneau de
remplacement de l'étape 15 ne peut être exercé que sur des données posées par un
script. La fonction la plus distinctive du produit est inatteignable en usage
normal.

**Un seul motif, réutilisé** — pas six écrans écrits à la main. Une description
par ressource dans `src/reference/descriptions.ts`, source unique comme
`src/layout/sections.ts` l'est pour la navigation : libellé, route, colonnes de
liste, champs de formulaire, rôles autorisés en lecture et en écriture. Six
descriptions copiées-collées divergeraient, et la divergence prend une forme
précise : un bouton d'enregistrement qui répond 403.

**Périmètre, lot A — le motif et les trois ressources qui débloquent la mise en
route**
- `src/types/reference.ts` : types de description, `ChampRessource`
- `src/reference/descriptions.ts` : les descriptions, et leurs tests
- `src/api/ressources.ts` : CRUD génériques sur l'enveloppe de pagination
  `{ elements, total, page, taille }` déjà en place depuis l'étape 8
- `src/composants/TableauRessource.tsx`, `FormulaireRessource.tsx`,
  `Pagination.tsx`, chacun avec son `.css`
- `src/pages/Reference.tsx` : la page qui orchestre, pour n'importe quelle
  ressource
- Clients, Sites, Agents

**Lot A fait.** Trois ressources, un seul motif, et aucun fichier de page écrit par
ressource. 203 tests frontend de plus — `descriptions.test.ts` 24, `valeurs.test.ts`
30 × 5 fuseaux, `sections.test.ts` +5 — 3 tests backend de plus, et 32 sabotages,
tous détectés après deux corrections de tests.

*Aucun fichier backend touché*, et une chose n'a donc pas pu être faite : la
**descente de niveau**. Aucun endpoint de liste n'accepte de filtre, seulement la
pagination — « les sites de ce client » demande un `?clientId=` sur
`GET /api/sites`, un `?siteId=` sur les contrats, un `?contratId=` sur les
prestations, un `?agentId=` sur les absences. Le critère de validation n'en dépend
pas, et la métadonnée de parenté est en place — elle alimente déjà la liste
déroulante du champ de référence. À reprendre au lot B, qui ajoute justement les
trois ressources concernées.

**Deux défauts que l'étape a découverts, et qui ne venaient pas d'elle.**

*Toute la chaîne du modèle était en `DeleteBehavior.Cascade`, et le `DELETE` rendait
204.* Mesuré : supprimer un client a effacé le client, son site, son contrat, sa
prestation et son intervention, en un appel, sans un mot. Et un test nommé
`Suppression_client_emporte_ses_sites_en_cascade` l'affirmait — ce n'était pas un
oubli, c'était une décision. Ce qui a changé, c'est que cette étape en fait un bouton
de liste là où il n'existait que dans Swagger.

**Tranché et corrigé** par la migration `SuppressionRestreinteSurLaChaineExploitation`,
douze opérations toutes sur des contraintes, aucune sur des données : `Restrict` sur
`Sites.ClientId`, `Contrats.SiteId`, `Prestations.ContratId`,
`Interventions.PrestationId`, `Stocks.ProduitId` et `Absences.AgentId`. La règle du
modèle tient désormais en une phrase — *aucune suppression n'efface en silence une
ligne que l'utilisateur gère sur son propre écran* — et elle donne ses deux
exceptions : le rattachement au tenant, qu'aucun endpoint n'expose, et
`Remplacement`, état dérivé qu'aucun écran ne liste.

Les six refus **nomment ce qui bloque et par où sortir**, avec le compte exact : un
`Restrict` dont le message ne dit ni l'un ni l'autre bloque l'utilisateur au lieu de
protéger les données. Le message d'`Agents` comptait les interventions seules ; un
agent bloqué par ses seules absences lisait « porte 0 intervention(s) », ce qui
ressemble à un défaut du logiciel plutôt qu'à une règle.

⚠️ **Et la parade d'interface a été retirée.** Un champ `cascade` des descriptions
annonçait ce que la suppression emportait — juste quand il a été écrit, faux dès la
migration appliquée. Une correction de fond rend caduque la parade qui la contournait,
et laisser les deux coûte plus cher que de n'avoir eu que l'une.

*La validation de modèle d'ASP.NET répond en anglais sur tous les DTOs*, pas
seulement ceux de mot de passe : « The field Nom must be a string or array type with
a maximum length of '200' » traverse l'API tel quel. Rendu inatteignable plutôt que
traduit — `longueurMax` dans chaque description, `maxLength` sur la saisie, et le
navigateur refuse dans sa propre langue. Une série empêche un champ de texte de
l'oublier.

**Un enseignement des sabotages.** La conversion des valeurs a été **sortie du
composant** dans `reference/valeurs.ts` : c'est la seule partie de l'étape qui écrit
des données fausses au lieu d'afficher quelque chose de faux, et enfermée dans un
`.tsx` elle n'était pas éprouvable. Le sabotage qui remplace la composition
`jourIso` → `depuisChaineIso` → `formaterDateLongue` par un `new Date().getDate()`
n'est rouge **qu'à America/Los_Angeles** : une intégration continue en UTC l'aurait
livré. `dates.ts` était déjà rejoué dans cinq fuseaux ; éprouver trois fonctions
séparément ne prouve pas leur assemblage.

**Non vérifié.** Le critère de validation lui-même — un administrateur crée une
entreprise de zéro **par l'interface seule** — n'a pas pu être exercé dans un
navigateur, l'extension étant indisponible. Ce qui est prouvé est le contrat que les
écrans utilisent, rejoué contre le vrai serveur avec les corps exacts que
`valeurs.ts` produit : enveloppe de pagination, plafond de 200, création d'un client
puis d'un site rattaché puis d'un agent, les cinq valeurs de `TypeClient`, les
facultatifs à `null`, la date en `AAAA-MM-JJ` relue à minuit UTC, et les refus. Le
clic reste à faire.

**Périmètre, lot B**
- Contrats, Prestations, Produits
- **Absences**, et c'est le lot qui rend l'étape 15 utilisable
- **Les filtres de liste** que la descente de niveau demande, sur les quatre
  endpoints concernés — c'est du backend, et c'est ce qui manque au lot A

**Lot B fait.** Sept ressources en tout, toujours aucun fichier de page écrit par
ressource. Côté serveur, quatre endpoints acceptent désormais le filtre de parenté —
`?clientId=` sur les sites, `?siteId=` sur les contrats, `?contratId=` sur les
prestations, `?agentId=` sur les absences — et le filtre entre **avant le comptage**,
faute de quoi l'écran annoncerait quarante lignes au-dessus d'une liste qui en montre
trois. 16 tests backend de plus, 123 frontend de plus, 18 sabotages dont 17 détectés.

La **saisie d'une absence existe**, et c'est ce qui change le produit : le panneau de
remplacement de l'étape 15 cesse de dépendre du script de démonstration.

**Trois choses que le lot a tranchées, et une qu'il a trouvée.**

*Le motif d'un arrêt maladie disparaît du formulaire.* `masquerSi` dans la
description, et `corpsDe` envoie `null` — les deux moitiés sont nécessaires. La
seconde est la moins évidente : sans elle, saisir un motif **puis** basculer le type
sur « Maladie » enverrait ce texte, le serveur refuserait en parlant de donnée de
santé, et l'écran n'afficherait plus le champ dont le refus parle. Mesuré contre le
serveur : le `PUT` portant `motif: null` **efface** un motif déjà enregistré, il ne le
laisse pas en place. L'étape 17 ajoutera `AccidentTravail` à la même liste.

*L'étiquette d'une ressource ne peut pas être une clé étrangère.* Elle sert deux fois,
et la seconde contraint : c'est elle qui libelle les options de la liste déroulante
d'un **enfant**. Sur l'écran des prestations, le choix des contrats n'a pas chargé les
sites et n'aurait affiché que des identifiants. D'où `frequence` pour un contrat, et
une invariante qui l'empêche de redevenir une référence. La contrepartie est assumée :
deux contrats d'un même site peuvent porter la même fréquence, et c'est la descente de
niveau qui rend ce choix inutile dans le parcours normal — arrivé par le contrat, le
formulaire part avec lui déjà rempli.

*Les absences ne sont pas des données de référence.* Un responsable les écrit, le
serveur l'y autorise, et leur appliquer les droits des données de référence lui aurait
présenté un formulaire en lecture seule sur la seule chose que l'étape 15 lui demande
de saisir. Le test qui affirmait « le Responsable n’écrit rien » a donc changé — non
parce que la matrice s'est ouverte, mais parce que la liste des descriptions a dépassé
les seules données de référence.

⚠️ *Deux fois dans la même séance, un gris de marque a été posé comme couleur de
texte.* `brand-strong` mesure **12,47:1** sur `surface` en thème clair et **2,29:1**
en sombre ; sur `brand-tint`, **10,42:1** et **2,03:1**. Le thème clair laisse passer,
le sombre s'effondre — et l'œil ne le voit pas en relisant du CSS. Les deux liens sont
repassés en `texte`, distingués par la graisse et le soulignement, ce que §12 prescrit
justement pour un niveau de plus. Les quatre paires sont désormais mesurées par
`npm run verifier`, donc inoubliables.

⚠️ **Non vérifié, de nouveau.** Le critère de validation — créer une entreprise de
zéro par l'interface seule — n'a toujours pas été exercé dans un navigateur. Ce qui
est prouvé est le contrat, rejoué contre le vrai serveur avec les corps exacts que
`valeurs.ts` produit : un contrat sans échéance à `dateFin: null`, une durée en nombre
JSON, un produit sans référence fournisseur, un seuil décimal, les dates relues à
minuit UTC, le refus du motif de maladie puis son acceptation à `null`, et les deux
refus de suppression qui comptent leurs enfants et nomment la sortie. Le clic reste à
faire.

La sonde qui rejoue ce contrat vit dans `backend/Scripts/contrat-ecrans-reference.py`,
et non dans un fichier jetable : une vérification reportée qui n'est nulle part n'est
pas reportée, elle est abandonnée. Elle défait ce qu'elle a créé en remontant la
chaîne, ce qui prouve au passage la seconde moitié de la règle des `Restrict` — la
sortie qu'un refus nomme doit être franchissable, et le seul moyen de le savoir est de
l'emprunter.

**Décisions à respecter**

La **matrice des droits de l'étape 8** pilote l'affichage, pas seulement le
serveur. Un `Responsable` **lit** les données de référence et n'y écrit pas : le
formulaire doit lui être présenté en lecture seule, sans bouton
d'enregistrement. Afficher un bouton qui répond 403 est une faute d'interface, pas
une protection. Même remarque pour le `ClientSyndic`, qui lit ses sites et rien
d'autre. La garde reste au serveur ; l'écran ne fait que ne pas mentir.

Le **motif d'une absence `Maladie` n'est pas affiché**, promesse faite à l'étape 8
et sans support jusqu'ici : le champ disparaît quand `Maladie` est sélectionné.
L'interface empêche, l'API fait barrage. C'est aussi le formulaire qui recevra
`AccidentTravail` à l'étape 17, avec la même interdiction.

La **navigation suit la chaîne du modèle** plutôt que d'aligner huit pastilles
dans une barre de 64 px. `SI` porte la chaîne client — Clients → Sites →
Contrats → Prestations — avec descente de niveau ; `AG` porte Agents → Absences ;
`ST` garde Produits et Stocks pour son étape. Les libellés des pastilles pourront
être revus à ce moment-là.

**Hors périmètre**, et volontairement : les **Stocks**. C'est l'une des quatre
fonctions du cœur de produit, avec ses propres notions — emplacements, seuils,
mouvements — et ce n'est pas un formulaire de plus. Elle mérite son étape.

**Validation** — le critère est unique et il est dur : **un administrateur crée une
entreprise de zéro par l'interface seule**, sans script et sans Swagger — un
client, un site géolocalisé, un contrat, une prestation, deux agents, une absence
— puis l'écran de planning de l'étape 14 affiche ce qu'il vient de saisir, et le
panneau de remplacement de l'étape 15 propose des candidats sur l'absence qu'il
vient de poser. Le jour où c'est vrai, le script de démonstration cesse d'être le
seul chemin d'entrée dans le produit.

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

  **Lot A fait.** Douzième entité `AccesJournalise`, douzième filtre global, et un
  filtre d'action MVC posé sur **huit** actions — pas deux.

  ⚠️ C'est le fait qui a orienté tout le lot : **deux endpoints divulguent ces données
  sans porter « agent » ni « absence » dans leur chemin.** `CandidatDto` expose nom,
  prénom et `DistanceKm`, dérivée des coordonnées du domicile — la donnée même que
  l'avertissement CNIL de cette étape désigne — et elle sort par *deux* routes,
  `/api/interventions/{id}/candidats` et `/api/remplacements/{id}/candidats`. Quant à
  `PlanningNonPourvueDto.Cause`, elle vaut littéralement « Agent absent » : le planning
  **révèle une absence** sans qu'on lise la liste des absences. Un journal construit sur
  les routes évidentes aurait eu deux trous et l'air complet.

  La parade n'est donc pas la vigilance : `JournalAccesTests` énumère par réflexion
  toutes les actions de contrôleur, descend dans leurs types de retour jusqu'aux DTOs,
  et exige l'attribut sur toute action `GET` dès qu'un DTO porte `Nom`, `Prenom`,
  `Adresse`, `Email`, `Telephone`, `Latitude`, `Longitude` ou `Motif`. À **défaut
  fermé** : un DTO créé demain tombe dans le filet, et s'en dispenser demande une
  exemption motivée. Quatre existent — `ClientDto` et `SiteDto` pour une personne
  morale, `ProfilDto` pour l'identité de l'appelant lui-même, `ProduitDto` parce qu'un
  produit porte un nom aussi. ⚠️ `ClientDto` reste discutable, le contact d'une petite
  copropriété étant souvent une personne physique : question renvoyée à la cartographie
  des données, consignée et non tranchée.

  **C'est ce test qui a conduit le lot, et pas l'inverse.** Il a signalé six actions que
  je n'avais pas prévues : deux créations — qu'il ne faut *pas* journaliser, une écriture
  renvoyant ce que l'appelant a fourni, d'où la restriction aux `GET` — et quatre
  réponses portant `ProfilDto` ou `ProduitDto`, d'où deux exemptions. Aucune de ces six
  n'était visible en relisant les contrôleurs.

  Six règles de conception, détaillées en `CLAUDE.md` §7 bis. Les deux qui coûtent :
  le journal **ne contient jamais ce qui a été lu** — un compte de lignes, jamais une
  liste d'identifiants, sinon il recopie la table qu'il surveille — et **l'échec de son
  écriture fait échouer la réponse**, parce qu'un journal qu'on contourne en cassant
  son écriture n'en est pas un.

  Conservation **douze mois**, par `Scripts/purge-journal-acces.sql`. La purge est un
  geste d'exploitation sur la base et non un endpoint : `JournalAccesController`
  n'expose que `GET`, et un compte administrateur compromis ne doit pas pouvoir effacer
  la trace de ce qu'il a lu.
- **Anonymisation.** La clé étrangère `Restrict` sur `Intervention.AgentId` rend
  un agent impossible à supprimer dès qu'il porte une intervention. C'est le bon
  choix pour l'intégrité du planning, et il entre en conflit direct avec une
  demande d'effacement. Concevoir un chemin d'anonymisation — nom, adresse et
  coordonnées remplacés par des valeurs neutres, ligne conservée — plutôt qu'une
  suppression.

  **Lot B fait.** `POST /api/agents/{id}/anonymiser`, administrateur seul,
  irréversible, journalisée — `NatureAcces.Anonymisation` existait depuis le lot A et
  n'était écrite par personne.

  ⚠️ **Correction de vocabulaire, et elle compte pour le registre des traitements :
  c'est une pseudonymisation au sens de l'article 4(5), pas une anonymisation au sens de
  l'article 4(1).** L'identifiant de l'agent survit — toutes les interventions passées en
  dépendent — donc la ligne reste rattachable à une personne par quiconque détiendrait
  une correspondance externe. La route garde le mot « anonymiser » parce que c'est
  l'intention de l'exploitant ; la documentation juridique devra être exacte. Le compte
  rendu de l'opération le dit lui-même, article cité.

  ⚠️ **La personne est nommée en trois endroits, pas un.** `Agents`, `AspNetUsers` si un
  compte lui est rattaché — qui porte nom, prénom, courriel et téléphone — et le `Motif`
  en texte libre de ses absences. N'en traiter qu'un donne un effacement qui n'efface
  pas, et laisse en plus à la personne son accès au produit.

  **Le compte rendu énumère ce qui subsiste**, et c'est sa moitié la plus utile : qui
  répond à une demande d'effacement doit savoir ce qu'il peut promettre. Il nomme
  l'identifiant technique, les interventions conservées, et le résidu le plus gênant —
  une absence de type `Maladie` garde son type et ses dates après effacement du motif,
  ce qui **reste une donnée de santé**. L'effacer demanderait de supprimer l'absence et
  casserait l'historique des remplacements : c'est une question de durée de conservation,
  renvoyée au lot de documentation plutôt que tranchée en silence.

  Détail des six décisions en `CLAUDE.md` §7 bis. Celle qui aurait pu passer inaperçue :
  les coordonnées du domicile passent à `null` et **jamais à zéro** — un zéro passe le
  `[Range(-90, 90)]`, place l'agent dans le golfe de Guinée et fausse toutes les notes de
  distance sans rien signaler.
- Durées de conservation, par nature de donnée
- Registre des traitements et cartographie des données

  **Lot D fait.** `RGPD.md`, à la racine du dépôt. Aucun code.

  Six traitements au registre, les douze tables cartographiées colonne par colonne,
  dix durées de conservation proposées avec leur raison, les droits de la personne avec
  l'endpoint qui les sert ou la mention qu'aucun ne les sert, un modèle de note
  d'information aux agents, et le résumé des mesures de l'article 32.

  **Les trois questions laissées ouvertes par les lots A et B sont tranchées.** La
  qualification est **pseudonymisation**, article 4(5), partout dans le registre — et la
  conséquence n'est pas cosmétique : une donnée pseudonymisée reste une donnée
  personnelle, donc elle garde une durée de conservation. Le résidu de santé des
  absences `Maladie` devient une question de durée, résolue par un basculement de `Type`
  vers `Indisponibilite` après trois mois, qui conserve l'agent et les dates — ce dont
  l'historique des remplacements a besoin est **qu'il y avait un trou**, pas pourquoi.
  L'exemption de `ClientDto` est maintenue, mais sa raison est réécrite : non pas « c'est
  une personne morale », ce qui est faux pour une copropriété, mais « coordonnées
  professionnelles d'un interlocuteur dans l'exécution d'un contrat » — et la condition
  qui la ferait tomber est écrite pour ne plus avoir à rediscuter.

  ⚠️ **Six écarts constatés, dont un qui dépasse largement le périmètre du lot :**
  `frontend/index.html` charge Figtree et IBM Plex Mono **depuis Google**, donc chaque
  chargement de page transmet l'adresse IP du navigateur à un tiers, sur toute instance
  de KLEAN, sans que l'exploitant l'ait choisi. Cela contredit le §2 de `CLAUDE.md` —
  « pas de service tiers obligatoire » — et la règle avait été lue comme ne portant que
  sur les dépendances de construction, Google Fonts n'apparaissant pas dans
  `package.json`. Les cinq autres : l'adresse IP des sessions n'est jamais purgée, aucun
  chemin d'effacement n'existe pour un compte sans fiche agent, aucun export ne sert la
  portabilité, `Sites.Consignes` peut nommer une personne sans que rien ne l'en dissuade,
  et neuf durées sur dix n'ont aucun mécanisme.

  **Cinq des sept écarts sont désormais corrigés**, après le lot D et sur demande
  explicite. Deux ont un mécanisme complet — la purge des jetons de rafraîchissement, qui
  ferme la seule adresse IP conservée sans limite, et l'aide du champ consignes, qui agit
  sur la cause plutôt que sur la trace. Un troisième comble un droit entier :
  `GET /api/agents/{id}/dossier` sert l'accès de l'article 15 et la portabilité de
  l'article 20, et c'est **l'invariante par réflexion de `JournalAccesTests` qui a exigé
  sa journalisation** avant que la question se pose — le filet de l'étape 17 lot A faisant
  exactement ce pour quoi il avait été écrit.

  Les deux scripts de conservation métier — `conservation-donnees-metier.sql` et
  `degradation-absences-maladie.sql` — **rapportent sans supprimer** : leurs `DELETE` et
  `UPDATE` sont commentés, parce que les durées sont des propositions non validées
  juridiquement et qu'une purge de données métier est irréversible.

  ⚠️ **Un huitième écart est apparu en écrivant ces scripts**, et c'est le genre de trou
  qu'on ne voit qu'en essayant d'appliquer une politique de conservation : `Sites` et
  `Clients` ne portent **aucune date**. Leur échéance se déduit de celle de leurs
  contrats, ce qui ne fonctionne pas pour une ligne qui n'en a aucun — la requête ne peut
  pas distinguer un site dont les contrats ont été purgés d'un site saisi hier. Décision
  de modèle, pas de script : une `DateCreation` additive, ou l'examen manuel assumé.

  **L'écart le plus sérieux est également corrigé** : `frontend/index.html` chargeait
  Figtree et IBM Plex Mono **depuis Google**, donc chaque chargement de page transmettait
  l'adresse IP du navigateur à un tiers, sur toute instance et sans que l'exploitant
  l'ait choisi. C'était le seul écart à concerner tous les utilisateurs de toutes les
  instances, et il contredisait le §2 de `CLAUDE.md` — « pas de service tiers
  obligatoire » — en ayant échappé à la règle parce qu'il n'apparaissait pas dans
  `package.json`.

  Les quatre fichiers sont désormais dans `frontend/public/polices/`, licences OFL
  incluses : 79 Ko, grâce à la fonte **variable** de Figtree et au sous-ensemble
  **Latin1** d'IBM Plex Mono, dont la plage Unicode couvre tout ce qu'une interface
  française écrit. Les deux politiques de sécurité du contenu sont refermées sur
  `'self'`, et `npm run verifier` échoue sur toute origine externe réapparue, sur une
  `url()` pointant dans le vide, sur un `.woff2` sans signature et sur une licence
  manquante — **le rappel est mécanique, pas verbal**, parce qu'une police absente ne
  casse rien de visible.

  **Le dernier chemin d'effacement du produit est également ouvert** :
  `POST /api/auth/comptes/{id}/anonymiser`, pour les comptes sans fiche agent —
  `Responsable`, `Administrateur`, `ClientSyndic` — qui n'avaient aucun chemin, ni même
  une route pour être désactivés. Migration `AnonymisationDesComptes`, un `AddColumn`
  nullable : une **date** et non un drapeau, pour que le registre réponde « quand cet
  effacement a-t-il été exécuté ».

  ⚠️ **Un sabotage a fait apparaître un trou réel et déplacé une garde mal placée.**
  J'avais posé le refus « jamais le dernier administrateur actif » sur cette nouvelle
  route ; le retirer ne faisait rougir aucun test, parce que la garde y est
  **inatteignable** — l'appelant étant forcément un administrateur actif, une cible
  administratrice différente fait deux administrateurs. C'est le refus de s'effacer
  soi-même qui porte l'invariante.

  Le risque est sur l'**autre** route : `POST /api/auth/comptes` exige un `AgentId` pour
  le rôle `Agent` et ne l'interdit à aucun autre, donc un compte `Administrateur` peut
  porter une fiche agent — le cas normal dans une entreprise de cinq personnes où le
  responsable fait aussi le ménage. Anonymiser cette fiche neutralise son compte sans
  passer par ce refus, et laisse l'entreprise sans aucun compte capable d'en créer un.
  La garde est désormais là, éprouvée dans les deux sens.

  **Leçon à retenir du lot** : un sabotage resté vert ne dit pas toujours qu'un test
  manque — parfois il dit que le code est mort, et la vraie garde est ailleurs.

  **§9.7 tranché le 28 octobre 2026 : `DateCreation` sur `Sites` et `Clients`.** Aucun
  écart ne reste ouvert côté logiciel. La migration date du jour de son application les
  lignes qui existaient avant elle, et non du 1er janvier de l'an 1 : sinon le script de
  conservation les aurait toutes vues hors délai. Elle corrige au passage une colonne
  `supprimable_maintenant` qui valait toujours zéro. Détail en `RGPD.md` §9.7.

  ⚠️ **Une AIPD est requise selon toute vraisemblance** : trois critères de l'article
  35(3) sont réunis, là où deux suffisent à la présomption — données de l'article 9
  (`Maladie`), personnes en situation de dépendance économique (des salariés), et
  évaluation notée (le score de candidat, calculé en partie sur la distance au domicile).
  Elle appartient à l'exploitant ; `RGPD.md` lui fournit la matière.
- **Écran de compte**, qui est le premier de KLEAN à en avoir besoin : c'est là
  que vivent le changement de mot de passe et le bouton *Déconnexion de tous les
  appareils*. L'endpoint existe depuis l'étape 16 et n'a aucun bouton ; il n'avait
  pas de place avant cet écran.

  **Lot C fait.** `POST /api/auth/changer-mot-de-passe` et `frontend/src/pages/Compte.tsx`.

  Le changement de mot de passe **n'existait pas** : `AuthController` ne portait que la
  réinitialisation par courriel, qui exige un SMTP — optionnel sur une instance KLEAN
  — et un administrateur qui le pose à la main connaît alors le mot de passe d'un
  collègue. Un responsable connecté n'avait donc aucun chemin. Ici la preuve d'identité
  est le mot de passe actuel, et rien d'autre n'est requis.

  ⚠️ **La décision qui devait être prise avant d'écrire la ligne** : changer son mot de
  passe doit-il déconnecter l'appareil depuis lequel on le change ? Par défaut **oui**
  — Identity régénère le `SecurityStamp` et `OnTokenValidated` le vérifie à chaque
  requête, donc l'utilisateur serait éjecté à la seconde où il réussit. Tranché dans
  l'autre sens : une session neuve est rouverte dans la même réponse, parce qu'il vient
  de prouver son mot de passe actuel. Les **autres** appareils tombent, tous. Détail des
  trois pièges — l'ordre révoquer-puis-rouvrir, le 400 au lieu du 401, le plafond de
  débit sur un endpoint pourtant authentifié — en `CLAUDE.md` §6 et §10.

  ⚠️ **Le 400 n'est pas un détail de style.** `api/client.ts` traite tout 401 comme une
  session morte : il rafraîchit, rejoue, puis purge. Un 401 sur un mot de passe actuel
  faux — le code pourtant le plus naturel — aurait déconnecté l'utilisateur sur une
  faute de frappe. La règle générale est désormais écrite : 401 pour l'état de la
  session, 400 pour celui du corps.

  L'écran accueille enfin le bouton *Déconnexion de tous les appareils*, qui attendait
  depuis l'étape 16 sans qu'aucune interface ne l'appelle. Il n'est **pas** une section
  de `layout/sections.ts` : une section est une fonction du produit, ouverte par rôle,
  et `premiereSectionOuverte` en fait une destination d'arrivée — le compte, ouvert à
  tous, deviendrait l'arrivée de tous. Deux invariantes gardent la séparation.

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
- **La politique de sécurité du contenu du document**, qui est un en-tête, donc
  l'affaire de qui sert `frontend/dist/`. La chaîne à renvoyer est
  `CspProduction` dans `frontend/csp.ts` — ne pas la recopier, la lire, sinon les
  deux divergeront au premier `index.html` modifié. Servir le frontend et l'API
  sous la même origine est le déploiement le plus simple et évite CORS entièrement.

  ⚠️ Depuis le 3 octobre 2026, **cette politique ne déclare plus aucune origine
  externe** : les polices sont servies par KLEAN (`RGPD.md` §9.1). Conséquence pour
  cette étape — l'instance n'a besoin d'**aucun accès sortant** pour servir son
  interface, ce qui simplifie le `docker-compose.yml` et rend un déploiement derrière
  un pare-feu fermé possible sans dégrader la typographie.
- **`UseExceptionHandler`.** Aujourd'hui une exception non rattrapée rend un 500
  au corps vide hors développement, ce qui ne fuite rien mais ne laisse rien à
  corréler non plus. C'est le pendant du point de journalisation ci-dessus.

**Arbitrages du 28 octobre 2026.** Caddy sert le document et termine HTTPS, l'API
reste du JSON pur derrière lui ; journalisation intégrée à .NET, sans dépendance ;
migrations par bundle EF lancé à la main, jamais au démarrage de l'API. Trois lots :
A — l'API en production ; B — conteneurs ; C — documentation d'exploitation.

**Lot A fait.** Journal JSON, identifiant de corrélation, `UseExceptionHandler`, et
un défaut que le périmètre ne nommait pas.

⚠️ **Le défaut d'abord, parce qu'il aurait cassé le premier déploiement.** Derrière un
proxy inverse, l'API voyait l'adresse du proxy pour tout le monde. La limitation de
débit par adresse devenait donc un plafond **global** : vingt échecs de connexion de
n'importe qui bloquaient la connexion de toute l'instance. `JetonsRafraichissement.AdresseIp`
ne consignait plus que le proxy — une donnée personnelle sans plus aucun usage. Et
`UseHttpsRedirection` bouclait, le proxy ayant terminé TLS. Corrigé par
`UseForwardedHeaders`, borné aux réseaux de `Proxy:Reseaux` : croire `X-Forwarded-For`
de toute source aurait laissé chaque client choisir son adresse.

**La corrélation n'invente pas d'identifiant.** C'est le `TraceId` qu'ASP.NET ouvre pour
chaque requête et que le formateur JSON écrit déjà dans les portées : renvoyé en
`X-Correlation-Id` sur toute réponse, et dans le champ `correlation` d'un corps
d'erreur — ce qu'un utilisateur recopie, l'exploitant le cherche dans le journal.

⚠️ **Les en-têtes de sécurité manquaient sur le 500.** `UseExceptionHandler` vide les
en-têtes avant d'écrire : `EnTetesSecurite`, qui posait les siens à l'aller, les
perdait précisément sur la réponse d'erreur. Ils passent désormais par `OnStarting`.

Six tests dans `ExploitationTests`, et cinq sabotages tous rougis : sans
`UseForwardedHeaders`, confiance à toute source, en-têtes de sécurité à l'aller,
corrélation à l'aller, message d'erreur par défaut.

**Lot B fait.** `docker-compose.yml` à la racine : `postgres`, `api`, `caddy`, et
`migrer` sous le profil `outils`. `.env.example` est le seul fichier où un exploitant
écrit un secret. Éprouvé de bout en bout sur Docker Desktop : construction, migration
par le bundle, connexion à travers Caddy, cookies `Secure` renvoyés.

Quatre choix, et chacun ferme quelque chose :

- **Une seule image pour l'API et son bundle de migration.** Deux images pourraient
  diverger d'une version de schéma ; le service `migrer` reprend la même avec un autre
  point d'entrée.
- **La CSP du document est lue, jamais recopiée** : `frontend/scripts/entetes-caddy.mjs`
  charge `csp.ts` à la construction de l'image et écrit le bloc `header` de Caddy.
- **Caddy n'a pas de journal d'accès**, et le `Caddyfile` dit pourquoi : il
  consignerait l'adresse IP de chaque visiteur. Les journaux de conteneur sont
  plafonnés à cinq fichiers de 10 Mo, le pilote par défaut gardant tout sans limite.
- **L'API ne croit que Caddy** : adresse fixe `172.28.0.10` sur un sous-réseau fixé,
  et `Proxy__Reseaux__0` réduit à ce `/32`. Mesuré dans le conteneur : la session
  consigne l'adresse du client, pas celle de Caddy, et un `X-Forwarded-For: 6.6.6.6`
  forgé par le client est écrasé.

⚠️ **Deux pièges rencontrés, consignés en `CLAUDE.md` §13** : construire le bundle
exécute `Program.cs`, donc exige une chaîne de connexion et une clé JWT même sans
base ; et `dotnet ef -c` veut dire `--context`.

⚠️ Les clés de Data Protection, qui signent les jetons de réinitialisation de mot de
passe, vivent dans un volume : sans lui, un lien envoyé une minute avant une mise à jour
devenait invalide après.

L'intégration continue construit désormais les deux images : un `Dockerfile` qui
cesse de construire se découvre en poussant, pas chez l'exploitant qui met à jour.

**Lot C fait.** `DEPLOIEMENT.md` à la racine : installation de zéro, mise à jour,
sauvegarde et restauration, retour arrière, secrets, conservation, journaux. **Chaque
commande y a été jouée** sur une instance d'essai avant d'être écrite, et trois
affirmations que j'aurais écrites de mémoire se sont révélées fausses ou incomplètes :

- ⚠️ **Changer `POSTGRES_PASSWORD` dans `.env` casse l'instance**, la variable n'étant
  lue qu'à la création du volume. Mesuré : 502 partout. La procédure passe par
  `ALTER USER` d'abord.
- ⚠️ **Tourner la clé JWT ne déconnecte personne.** Mesuré : ancien jeton d'accès 401,
  rafraîchissement 200, et la session continue. Après une compromission il faut aussi
  révoquer les jetons de rafraîchissement, par une requête SQL éprouvée elle aussi.
- ⚠️ **Le retour arrière d'une migration se joue avec la nouvelle image**, l'ancienne
  ne connaissant pas le code `Down` de la migration à défaire. Éprouvé dans les deux sens.

Deux défauts trouvés en écrivant, et corrigés :

- **L'écran ne montrait pas l'identifiant de corrélation**, alors que le corps du 500
  du lot A demande de le communiquer. `api/client.ts` l'ajoute désormais au message
  d'une erreur 5xx, et seulement 5xx : le serveur le joint aussi aux 400 de validation,
  où il ne serait que du bruit. Deux tests, et la garde des 5xx est éprouvée par
  sabotage.
- **Npgsql écrivait « Error: libgssapi_krb5.so.2 » au démarrage**, cherchant Kerberos
  dans une image qui ne l'a pas. Inoffensif, mais c'est la première ligne qu'un
  exploitant aurait lue. `Gss Encryption Mode=Disable` dans la chaîne de connexion.

**Validation** : un inconnu déploie en suivant le fichier, sans aide.

⚠️ **Ce qui est éprouvé, et ce qui ne l'est pas.** Le parcours entier a été joué sur
Docker Desktop, avec `KLEAN_DOMAINE=localhost` et le certificat de l'autorité interne
de Caddy. **L'émission d'un certificat Let's Encrypt sur un vrai domaine ne l'a pas
été**, faute de domaine public, ni le déploiement par un inconnu. C'est le geste qui
reste avant de déclarer l'étape validée.

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

**Réponse obtenue le 28 octobre 2026 — des personnes, et une vraie frontière.** Le
patron voit tout ; chaque chef d'équipe encadre des agents, qui travaillent en
copropriété, et **ne voit que son équipe**. Le patron est le rôle `Administrateur`, qui
voit déjà tout dans son entreprise, comme le `Responsable` : ce n'est pas un super
administrateur de l'instance, et la frontière entre entreprises ne bouge pas.

| Question | Réponse | Conséquence |
|---|---|---|
| Le chef a-t-il une fiche agent ? | **Non** : il pilote les agents et fait des devis simples, il ne fait pas de vacations | le rattachement vise son **compte** : `Agent.ChefEquipeId` → `AspNetUsers` |
| Un agent appartient-il à plusieurs équipes ? | **Non**, une seule | une colonne, pas de table de liaison |
| Quelle vacation sans agent voit-il ? | celle dont le **titulaire** de la prestation est dans son équipe (étape 20) | sans titulaire, seuls le patron et les responsables la voient |
| Parmi qui choisit-il un remplaçant ? | **son équipe seulement** | au-delà, le patron ou un responsable pourvoit |

⚠️ Une première réponse, le même jour, donnait au chef toutes les non pourvues et toute
l'entreprise comme candidats ; le périmètre n'aurait alors été qu'une vue. Elle a été
corrigée dans la foulée : avec plusieurs chefs, chacun aurait vu les équipes des
autres. Le périmètre est donc une **frontière**, du même ordre que celle du
`ClientSyndic` — et cette étape devient un chantier de sécurité, pas un filtre de
confort.

**Ce que voit un chef d'équipe**

| Donnée | Règle |
|---|---|
| Agents | `ChefEquipeId` = son compte |
| Absences | celles de ses agents |
| Interventions, planning | celles de ses agents ; plus celles sans agent dont le titulaire de la prestation est l'un de ses agents |
| Remplacements | ceux des interventions qu'il voit |
| Candidats | ses agents — par construction, si le moteur lit le contexte filtré |
| Clients, sites, contrats, prestations | inchangé, à confirmer : données de référence, sans personne dedans |

**Mécanisme envisagé** : un quatrième axe de filtrage, comme `ClientId` pour le syndic —
`ITenantContext` expose le compte du chef quand le rôle est `ChefEquipe`, et les filtres
globaux du `DbContext` s'en servent (`CLAUDE.md` §7). Le moteur d'affectation et le
planning lisent le contexte filtré : ils n'ont rien à savoir du chef.

**Points à éprouver ou trancher avant le lot A**

- ⚠️ **`AspNetUsers` n'est pas filtrée** (`CLAUDE.md` §7). À l'écriture de
  `ChefEquipeId`, le compte visé doit être vérifié à la main : même entreprise **et**
  rôle `ChefEquipe`. Le test construira le compte intrus directement en base, comme à
  l'étape 17.
- ⚠️ **Le filtre des interventions traverse deux navigations** — l'agent, et le
  titulaire de la prestation pour les non pourvues. À mesurer : il s'applique à toute
  lecture d'intervention, moteur d'affectation compris.
- ⚠️ **La bascule laisse un chef sans équipe devant un planning vide.** Dès que la
  frontière est active, un chef ne voit rien tant que l'administrateur n'a pas rattaché
  ses agents. Livrer l'écran de rattachement avant ou avec la frontière, jamais après.
- **Qui pose le rattachement** — l'administrateur, sur la fiche de l'agent. Le champ vise
  un compte, et les comptes n'ont pas d'écran de référence : il faudra une liste des
  chefs d'équipe de l'entreprise. La parité avec les DTOs de l'étape 20 rendra le champ
  obligatoire dans la description : c'est voulu.
- **Les écritures du chef** — il modifie une intervention (`Interne`) : sur une
  vacation hors de son équipe, le filtre rend 404. Vérifier qu'aucune écriture ne lit
  hors du contexte filtré.
- **RGPD** — `ChefEquipeId` est un lien hiérarchique entre deux personnes : cartographie
  de `RGPD.md` §3, et dossier de l'agent. La frontière **réduit** ce que lit un chef,
  donc ce que le journal d'accès consigne ; le dire dans le registre.
- **Les devis** — le chef d'équipe en fait. Le module devis, adossé au catalogue de
  l'étape 21, devra lui être ouvert : à noter au cadrage du devis.

**Lots, provisoires**

- **A** — `ChefEquipeId`, migration additive, validation à l'écriture, liste des chefs
  d'équipe, tests dont l'intrus construit en base.
- **B** — la frontière : axe du chef dans `ITenantContext`, filtres globaux, tests
  d'isolation à deux chefs et deux équipes sur chaque donnée du tableau, sabotages.
- **C** — le rattachement sur la fiche de l'agent, le planning du chef, `RGPD.md`,
  `CLAUDE.md` §7. Livré **avec** B, pour la raison écrite plus haut.

**Lot A — fait le 28 octobre 2026**

- `Agent.ChefEquipeId`, clé étrangère vers `AspNetUsers` en `SetNull`, indexée pour le
  filtre du lot B. Migration `RattachementChefEquipe`, additive : une colonne nullable,
  un index, une clé étrangère.
- **Validation à chaque `POST` et `PUT`** d'un agent : le compte visé est de la même
  entreprise **et** porte le rôle `ChefEquipe`, sinon 400. Les deux conditions sont écrites
  à la main, dans une seule requête que partagent la vérification et la liste — deux
  définitions de « chef de l'entreprise » finiraient par diverger.
- **`GET /api/agents/chefs-equipe`**, administrateur seul, non paginée : identifiant, nom,
  prénom, actif — ni courriel ni téléphone. Première lecture de comptes du produit, donc
  **journalisée** en `Compte` / `Liste` : douze actions journalisées au lieu de onze.
- **Le champ est sur l'écran des agents dès ce lot**, et non au lot C : `ChefEquipeId` est
  dans `AgentCreateDto`, et un `PUT` qui l'omettrait détacherait l'agent à chaque
  enregistrement. La parité DTO ↔ écran l'impose. Un type de champ `compte` alimente la
  liste déroulante par une route plutôt que par une description, les comptes n'ayant pas
  d'écran de référence ; il n'est chargé que pour qui écrit.

Trois arbitrages, faits ici :

- **Un chef désactivé reste acceptable.** Le refuser obligerait à rattacher ailleurs toute
  l'équipe d'un chef parti avant de pouvoir corriger le téléphone de l'un de ses agents.
  La liste le propose, marqué « (désactivé) ».
- **Un lecteur voit « Rattaché — nom non communiqué »**, la liste des chefs étant réservée
  à l'administrateur. Sans cette option, la liste déroulante vide afficherait « — choisir — »
  et ferait croire l'agent sans chef.
- **L'intrus est créé par l'API, pas en base.** Un chef d'une autre entreprise est un compte
  légitime de cette entreprise : rien ne le rend incréable, contrairement aux comptes de
  l'étape 17. Les deux conditions sont éprouvées séparément, chacune par un compte qui ne
  viole qu'elle, et six sabotages font chacun rougir la série.

Reste au lot C : la colonne « Chef d'équipe » dans la liste, et le **dossier de l'agent**,
qui porte aujourd'hui le rattachement en identifiant brut, ce que l'article 15 ne tient pas
pour intelligible.

**Lots B et C — faits ensemble le 28 octobre 2026**

- **La frontière.** `ITenantContext.ChefEquipeId`, lu sur le rôle, et quatre filtres
  globaux — agents, absences, interventions, remplacements — exactement selon le tableau
  « Ce que voit un chef d'équipe ». Clients, sites, contrats et prestations restent
  inchangés, comme le tableau le proposait : aucune personne n'y est nommée.
- **Une vacation affectée appartient à l'équipe de son agent**, même si le titulaire de
  sa prestation est d'une autre équipe. Le titulaire ne départage que les vacations sans
  agent. Écrit ici parce que la réponse du 28 octobre ne tranchait pas ce cas croisé.
- **Le cycle de remplacement est ouvert au chef** — ouvrir, proposer, accepter, refuser —,
  arbitré le 28 octobre : c'est ce que dit « il choisit parmi son équipe ». Créer,
  modifier et supprimer un remplacement à la main restent à `Exploitation`.
- **Mesuré**, comme le demandait le premier risque : sur 109 200 vacations, la requête de
  la semaine passe de 0,30 ms à 0,34–0,45 ms pour le patron et 0,49–0,56 ms pour un chef.
- **Le dossier nomme le chef d'équipe** en clair, cherché à la main dans l'entreprise de
  l'agent — éprouvé par un rattachement intrus posé directement en base.
- **L'écran.** Le planning d'un chef annonce « Votre équipe », et un chef sans équipe lit
  pourquoi son planning est vide et qui y remédie. Dans les listes, un identifiant que le
  serveur ne montre pas à l'utilisateur — le titulaire d'une prestation dans une autre
  équipe — s'affiche « Hors de votre périmètre » au lieu d'un UUID.
- **Tests.** `FrontiereChefEquipeTests` : deux chefs, deux équipes, un agent sans équipe,
  dix-neuf tests sur chaque donnée du tableau, et sept sabotages — un par condition de la
  frontière, plus la lecture du rôle —, tous rouges. Quatre tests existants supposaient
  un chef voyant toute l'entreprise ou privé du cycle ; ils gardent désormais la
  frontière.

**Non fait, délibérément** : la colonne « Chef d'équipe » de la liste des agents. Pour le
chef, elle vaudrait toujours lui-même ; pour un responsable, elle demanderait d'ouvrir la
liste des chefs, réservée à l'administrateur au lot A. L'administrateur voit le
rattachement dans le formulaire. À rouvrir si un responsable en exprime le besoin.
**Faite le 4 novembre 2026, pour l'administrateur seul** — aucun droit ne change côté
serveur. Une colonne porte désormais `roles` et `comptes`, et une invariante la réserve à
des rôles d'écriture, seuls à charger la liste des comptes. Cinq sabotages, tous rouges.

**Périmètre initial** — écrit avant la réponse, conservé pour mémoire

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

# Étape 20 — Génération des interventions depuis les contrats

Numérotée après l'étape 19 sans en dépendre : celle-ci attend un pilote, celle-là
est la condition pour qu'il y en ait un. Sans elle, chaque vacation de chaque
semaine se saisit à la main, et le produit est inutilisable en conditions réelles.

**Arbitrages du 28 octobre 2026**

| Question | Décision | Raison |
|---|---|---|
| Où vit la récurrence | sur la **prestation** : jours et heure | les tâches d'un même contrat n'ont pas la même fréquence — sols trois fois par semaine, vitrerie le jeudi |
| Quel agent | le **titulaire** de la prestation, s'il est éligible ce jour-là | explicite et modifiable ; aucun chemin n'affecte un agent inéligible (`CLAUDE.md` §9) |
| Qui déclenche | **à la demande**, « Générer jusqu'au … », idempotent, douze semaines au plus | une tâche de fond parcourrait toutes les entreprises sans jeton, donc hors des filtres globaux |
| Jours fériés | **sautés**, sauf contrat marqué `InclutJoursFeries` | un hôpital travaille le 1er mai, une copropriété non |
| Contrat modifié | la génération **n'ajoute que** ; elle ne modifie ni ne supprime jamais une vacation | une suppression automatique est l'effet silencieux que ce projet a appris à éviter ; le rapport signale les vacations devenues orphelines |

`Contrat.Frequence` reste un libellé libre : c'est l'étiquette des contrats à l'écran.

**Lot A fait.** Migration `RecurrenceDesPrestations`, additive : `Jours`,
`HeureDebut` et `AgentTitulaireId` sur `Prestations`, `InclutJoursFeries` sur
`Contrats`. Les prestations existantes reçoivent `Aucun` et restent planifiées à la
main. Validation à l'écriture : un jour inconnu est cité dans le refus, des jours sans
heure sont refusés, un titulaire d'une autre entreprise est inconnu. Quatorze tests
dans `RecurrenceTests`.

⚠️ **`SetNull` sur le titulaire, à côté du `Restrict` de `Intervention.AgentId`.** Le
second protège un fait — qui a travaillé où ; le premier n'est qu'une préférence pour
l'avenir, et `Restrict` rendrait insupprimable un agent qui n'a jamais travaillé.

⚠️ **Deux sabotages ont corrigé le lot en cours de route.** `Enum.TryParse` acceptait
« 3 », « Aucun » et « Lundi, Mardi » : d'où `JoursDeLaSemaine.TryLire`. Et un défaut de
colonne retiré laissait le test vert, la conversion d'énumération d'EF relisant la
chaîne vide comme `Aucun` sans rien lever : le test lit désormais la valeur stockée.

~~⚠️ **Transitoire jusqu'au lot C**~~ — fermé au lot C. Les écrans de référence
n'envoyaient pas encore les nouveaux champs, et un `PUT` remplace la ligne entière.
Enregistrer une prestation ou un contrat depuis l'écran remettait donc à zéro sa
récurrence et son drapeau de fériés.

**Lot B fait.** `POST /api/planning/generer { jusquAu }`, rôles `Exploitation`.

- **La fenêtre** va d'aujourd'hui à `jusquAu` inclus, et jamais au-delà de douze
  semaines. Une date passée est refusée.
- **Ce qui est créé** : une vacation `Planifiee` par jour coché, à l'heure de la
  prestation, pour tout contrat actif qui couvre ce jour. Le titulaire est affecté
  seulement si `EstEligibleAsync` l'accepte ; sinon la vacation reste à pourvoir et le
  rapport en donne la raison.
- **Ce qui n'est pas créé** : un jour férié, sauf contrat marqué ; et tout créneau
  où la prestation a déjà une vacation ce jour-là, **quel que soit son statut**. Une
  annulation est une décision, et la génération suivante ne la défait pas.
- **Ce qui est signalé et laissé tel quel** : les vacations planifiées de la période
  qui ne correspondent plus à leur contrat ou à leur prestation — contrat inactif,
  hors de ses dates, jour retiré, heure changée, férié non inclus. Ce sont les
  orphelines.
- **Le rapport ne nomme aucun agent.**
- **Les jours fériés** : les onze jours nationaux, calculés à partir de Pâques par
  `JoursFeries`. L'Alsace-Moselle et l'outre-mer sont hors périmètre V1, et c'est
  écrit dans le code. **Levé le 4 novembre 2026**, avec la limite du jour UTC, par un
  `Territoire` par entreprise — décidé ce jour-là plutôt qu'un réglage d'instance, faux
  dès qu'une instance héberge deux territoires. Il fixe le fuseau d'« aujourd'hui »
  (génération, contrôle de date, prévision du stock) et ajoute ses jours : Vendredi saint
  et Saint-Étienne en Alsace-Moselle, abolition de l'esclavage à la date de chaque
  département d'outre-mer. Écran Entreprise, pastille `EN`, sans création ni
  suppression. Migration `TerritoireDeLEntreprise`, additive, `'Metropole'` par défaut.
  Onze sabotages backend et deux côté écran, tous rouges ; le premier jeu de sabotages a
  laissé vert le férié des orphelines, rattrapé par un test du territoire posé après
  coup.
- **Le dossier de l'agent** porte désormais les prestations dont il est titulaire,
  nommées par leur site. Le point ouvert du lot A est fermé.

Quarante et un tests de plus : `GenerationTests`, `JoursFeriesTests`, et un test
dans `DossierAgentTests`. Vingt sabotages joués, tous rougis.

⚠️ **Quatre choses que le lot a apprises.**

- **Le verrou consultatif.** Deux générations simultanées créaient tout en double :
  l'idempotence se lit avant d'écrire, elle ne protège pas d'une course. D'où
  `pg_advisory_xact_lock` sur l'entreprise. Une contrainte d'unicité ne pouvait pas
  le remplacer, parce que deux vacations d'une même prestation le même jour sont
  légitimes en saisie manuelle.

  Le test de concurrence est d'abord **resté vert verrou retiré**. Sans titulaire,
  quatorze vacations s'écrivent en un aller-retour, et la première requête finit
  avant que la seconde ait lu. Il porte maintenant un titulaire, ce qui élargit la
  fenêtre de course.
- **L'écriture immédiate des vacations pourvues.** Le moteur lit la base : une
  affectation encore en mémoire ne compterait pas comme occupation, et le même
  titulaire serait affecté deux fois au même créneau.
- **La mesure, au haut de la cible.** 150 agents et 150 prestations sur douze
  semaines donnent 8 850 vacations.
  - Première mesure : **47 secondes**. Le délai de 30 s coupait la réponse en cours
    d'écriture : un 200 au corps tronqué, sur un travail validé.
  - Cause : 30 de ces 47 secondes venaient de la requête de la **note d'expérience**,
    que l'éligibilité ne lit pas. `EstEligibleAsync` ne la calcule plus, et les filtres
    durs restent ceux de la liste.
  - Mesure finale : **15,7 secondes**.
  - Réglages ajoutés : un délai propre de deux minutes sur cette seule route, et
    l'annulation de la requête transmise au service. Un dépassement **défait la
    transaction** au lieu de valider en silence.
- **Une horloge substituable**, `TimeProvider`. « Douze semaines à partir
  d'aujourd'hui » ne s'éprouve pas sur un jour qui change ; voir `CLAUDE.md` §11.

**Limite assumée** : « aujourd'hui » est le jour UTC. Entre minuit et deux heures du
matin à Paris, la génération peut créer une vacation du jour qui vient de finir. Le
modèle n'a pas de fuseau par entreprise.

**Lot C fait.**

- **Les prestations** reçoivent trois champs : les jours (sept cases), l'heure de
  début et le titulaire. Les deux nouveaux types de champ sont `jours` et `heure`.
  - Les jours partent en tableau, du lundi au dimanche, quel que soit l'ordre des clics.
  - L'heure part avec ses secondes, comme un `TimeSpan`.
- **Les contrats** reçoivent le drapeau « Travaillé les jours fériés ».
- **Le planning** porte « Générer les vacations jusqu'au … » et son rapport.
  - Seuls l'administrateur et le responsable voient le bouton.
  - La date proposée est le dimanche de la quatrième semaine, et la saisie est bornée
    à douze semaines.
  - Le rapport donne un résumé, puis trois sections repliables : les vacations restées
    à pourvoir, les jours fériés sautés, les orphelines.
  - La semaine affichée est rechargée après la génération.

⚠️ **La classe de défaut du transitoire est désormais gardée.** Un test de
`descriptions.test.ts` lit les DTOs C# du dépôt. Il exige que chaque propriété d'un
`XxxCreateDto` ait son champ à l'écran, et réciproquement, sur les sept ressources.
Un champ ajouté côté serveur et oublié côté écran fait rougir ce test, au lieu d'être
remis à zéro à chaque enregistrement. Un sabotage l'a vérifié : on retire
`inclutJoursFeries` de la description, et le test rougit.

⚠️ **Le choix du titulaire coûte une lecture journalisée.** L'écran des prestations
charge `GET /api/agents` pour remplir sa liste déroulante. Chaque ouverture laisse donc
une ligne « Agent / Liste » dans le journal d'accès, comme l'écran des absences le
faisait déjà. Une route de choix qui ne rendrait que l'identifiant et le nom allégerait
le journal comme la réponse. Ce n'est pas fait.

**Fermé le 4 novembre 2026.** `GET /api/agents/choix` rend l'identifiant, le nom, le
prénom et l'état actif, sous les mêmes rôles, le même tri, la même pagination et la même
frontière du chef d'équipe que `GET /api/agents`.
- Les écrans des prestations et des absences la chargent par `routeChoix`, déclarée
  sur la description des agents. Ils ne reçoivent plus ni l'adresse ni les coordonnées
  du domicile.
- **Journalisée à part**, en `ChoixAgents / Liste`, réponse retenue parmi trois. Une
  ligne `Agent` aurait noyé les lectures de fiches ; une exemption aurait laissé sortir
  des noms sans trace, à l'inverse de la liste des chefs.
- Tests :
  - la réponse brute ne contient aucun champ du domicile ;
  - la ligne de journal est la sienne ;
  - un chef ne voit que son équipe ;
  - `Agent` et `ClientSyndic` reçoivent un 403 ;
  - treize actions journalisées.
- Quatre sabotages, tous rouges. ⚠️ Le premier est d'abord resté vert : son motif visait
  la première classe du fichier finissant par `Actif`, `ChefEquipeDto`, et le sabotage ne
  testait donc rien. Rejoué sur `AgentChoixDto`, en y ajoutant la latitude du domicile,
  il rougit.
- Vérifié dans le navigateur : les deux écrans appellent `/api/agents/choix` et plus
  `/api/agents`, et le journal consigne `ChoixAgents`. La base de développement n'a aucun
  agent : les noms dans la liste n'ont pas pu être vus à l'écran.

Non éprouvé dans un navigateur : cela demande une session sur la base de
développement. Les corps envoyés sont ceux que `valeurs.test.ts` fixe, et ce sont les
formes que `RecurrenceTests` et `GenerationTests` acceptent côté serveur.

---

# Étape 21 — Catalogue des prestations

Numérotée après l'étape 20, dont elle est le prolongement direct. Depuis l'étape 20, la
**prestation** porte la récurrence, et c'est donc elle qu'on saisit pour mettre une
entreprise en route : des dizaines de lignes, site par site — hall, escaliers, sortie
des conteneurs, rentrée des conteneurs, vitrerie, ascenseur, local poubelles. Les
retaper à chaque site est la friction du premier jour d'un pilote. Le catalogue en
fait une sélection.

Elle passe **avant** les modules stock et devis de la « Suite », et c'est la raison de
la remonter. Les deux s'attachent naturellement à un **type** de prestation —
« Escaliers » consomme tel produit, se chiffre à tel temps standard — plutôt qu'à
chaque prestation d'un site. Poser le catalogue après eux obligerait à migrer une
table peuplée, ce que ce projet a appris à éviter. Et contrairement à l'étape 19, elle
n'attend aucune information d'un pilote.

**Forme envisagée, non arbitrée**

- Une entité `TypePrestation` : `Libelle`, `DureeParDefautMinutes`, `Actif`, et
  `EntrepriseId` comme les dix autres entités métier — filtre global, forçage dans
  `SaveChangesAsync`, absente du `CreateDto` (`CLAUDE.md` §7).
- Une liste de départ proposée à l'entreprise : hall, escaliers, sortie des conteneurs,
  rentrée des conteneurs, vitrerie, ascenseur, local poubelles, parking. Chaque
  entreprise la modifie ensuite.
- Sur `Prestation`, un `TypePrestationId` facultatif. Choisir un type à l'écran
  **préremplit** le libellé et la durée, qui restent modifiables.
- Un écran de référence de plus, décrit dans `reference/descriptions.ts` — et donc
  gardé par la parité avec les DTOs posée au lot C de l'étape 20.

**À trancher avant le lot A**

| Question | Options | Penchant |
|---|---|---|
| Portée de la liste | propre à chaque entreprise, ou commune à l'instance | **par entreprise** : les métiers diffèrent, et une liste commune serait la seule donnée métier hors tenant |
| Nature du lien | clé étrangère avec libellé **copié** ; simple aide à la saisie sans lien | **clé et copie** : le stock et le devis ont besoin de la clé ; la copie empêche qu'un type renommé réécrive l'historique des vacations |
| Suppression d'un type utilisé | `Restrict`, `SetNull`, ou désactivation seule | **désactivation**, comme `Actif` sur l'agent ; à confirmer contre la règle de suppression du §10 de `CLAUDE.md` |
| Liste de départ | posée à la création de l'entreprise par l'amorçage ; bouton « importer la liste type » ; rien | à trancher : l'amorçage ne crée qu'une entreprise, les suivantes n'existent pas encore par l'API |
| Sortie et rentrée des conteneurs | deux prestations indépendantes, comme aujourd'hui ; un « modèle de paire » qui crée les deux d'un geste | ouvert. Les deux tâches vont ensemble — la veille au soir, le lendemain matin —, selon le calendrier de collecte de la commune, donc par site. Le modèle actuel les porte déjà ; la paire n'est qu'un gain de saisie |
| Jours et heure par défaut sur le type | oui ; non | **non** : la récurrence dépend du site — un hall se fait le lundi ici, le jeudi là. Le type porte ce qui est vrai partout |

**Réponses obtenues le 29 octobre 2026** — les six penchants sont retenus, et les deux
questions ouvertes sont tranchées.

| Question | Réponse | Ce que cela veut dire dans le code |
|---|---|---|
| Portée | **par entreprise** | `TypePrestation` porte `EntrepriseId` et son filtre global, comme les onze autres |
| Lien | **clé et libellé copié** | `Prestation.TypePrestationId` nullable ; le libellé est copié à la création et reste modifiable par site |
| Retrait d'un type utilisé | **désactivation** | `Actif = false` : plus proposé à la saisie, conservé par les prestations. La suppression reste possible pour un type jamais utilisé — `Restrict` sur la clé, et le 409 nomme la désactivation comme sortie (`CLAUDE.md` §10) |
| Jours et heure par défaut | **non** | le type porte le libellé et la durée par défaut, rien d'autre |
| Liste de départ | **bouton « Importer la liste type »** | geste explicite de l'administrateur, valable pour toute entreprise et non seulement la première ; **rejouable sans doublon** — un type de même libellé, à la casse près, n'est pas recréé, même désactivé |
| Conteneurs | **deux prestations indépendantes** | deux types dans la liste type, « Sortie des conteneurs » et « Rentrée des conteneurs » ; rien de nouveau dans le modèle, la génération les traite déjà |

Restent à poser au lot B, avec toi : le **contenu** de la liste type — quels libellés,
quelles durées par défaut. Une durée inventée ici deviendrait la durée de toutes les
prestations créées sans la corriger, donc celle du moteur d'affectation et de la charge
de la semaine.

**Conséquences à vérifier au cadrage**

- `RGPD.md` §3 : `TypePrestation` ne porte aucune donnée personnelle. Le dire dans la
  cartographie plutôt que le laisser supposer.
- Module stock : son entité pivot est écrite `PrestationProduit`. Avec le catalogue,
  la question devient « par type ou par prestation » — par type pour la consommation
  standard, par prestation pour l'exception d'un site. À reprendre au cadrage du stock,
  pas ici.
- Module devis : le « catalogue de prestations » qu'il annonce est celui-ci. Il y
  ajoutera ses propres règles — tarif, coefficients —, sans en créer un second.

**Lots, provisoires**

- **A** — `TypePrestation` (libellé, durée par défaut, actif), migration additive, CRUD,
  `TypePrestationId` sur `Prestation`, refus d'un type désactivé ou d'une autre
  entreprise à la saisie, tests d'isolation et de saisie.
- **B** — la route d'import de la liste type, idempotente, et son contenu, validé avec
  toi avant d'être écrit.
- **C** — écran de référence du catalogue avec le bouton d'import, et préremplissage du
  libellé et de la durée dans le formulaire des prestations.

**Lot A — fait le 29 octobre 2026**

- `TypePrestation` — libellé, durée par défaut de 1 à 1 440 minutes, actif —, clé vers
  l'entreprise en `Cascade` comme `Produit`, et treizième filtre global.
  `Prestation.TypePrestationId`, nullable, en **`Restrict`**. Migration
  `CatalogueDesPrestations`, additive.
- `/api/types-prestation`, les cinq routes : lecture par les rôles internes — le chef
  d'équipe en aura besoin pour ses devis —, écriture par l'administrateur.
- **Libellé unique dans l'entreprise, à la casse et aux espaces près**, type désactivé
  compris, en 409 : deux « Hall d'entrée » rendraient le choix illisible, et l'import du
  lot B s'y fiera pour ne rien recréer. Sur un type désactivé, le refus dit de le
  réactiver.
- **Retrait** : un type jamais utilisé se supprime ; un type utilisé est refusé en 409,
  et le message nomme la désactivation.
- **À l'enregistrement d'une prestation**, un type d'une autre entreprise est inconnu, et
  un type désactivé est refusé quand on le **choisit**, jamais quand on le garde : sinon
  changer l'heure d'une prestation obligerait à lui retirer son type.
- **L'écran du catalogue existe dès ce lot**, et le champ « Type » sur les prestations
  aussi : la parité DTO ↔ écran l'impose, comme au lot A de l'étape 19. Le lot C se réduit
  donc au bouton d'import et au préremplissage — et à la liste déroulante, qui propose
  encore les types désactivés, que le serveur refuse en français.
- **Un garde-fou de plus** : `IsolationTests` refuse désormais toute entité portant
  `EntrepriseId` sans filtre global. Il manquait, et une treizième entité filtrée était
  le moment de l'écrire.
- `TypesPrestationTests` : 16 tests, et sept sabotages, tous rouges.

**Lot B — fait le 29 octobre 2026**

- **La liste type**, dans `Services/ListeTypePrestations.cs` : dix-sept types validés avec
  toi.
  - Treize pour les parties communes de copropriété : hall, escaliers et paliers,
    ascenseur, boîtes aux lettres, local poubelles, sortie, rentrée et lavage des
    conteneurs, encombrants, caves, parking, vitrerie, abords.
  - Trois pour les bureaux : bureaux, sanitaires, espace pause.
  - Un ponctuel : remise en état.

  ⚠️ **Les durées sont des hypothèses**, et le commentaire le dit. Aucune source ne publie
  de durée par type : la profession chiffre au m² ou au forfait. Seuls « Bureaux » et
  « Sanitaires » s'appuient sur une cadence publiée. Les durées ne font que préremplir :
  elles se corrigent dans le catalogue, et chaque prestation garde la sienne.
- **`POST /api/types-prestation/importer-liste-type`**, administrateur seul.
  - Il crée les libellés absents et laisse **tels quels** ceux déjà présents, à la casse
    et aux espaces près. Une durée corrigée n'est pas écrasée, et un type désactivé n'est
    pas recréé, puisque le désactiver était un retrait.
  - Le compte rendu nomme les deux listes, `Crees` et `DejaPresents` : qui rejoue l'import
    voit que rien n'a été recréé.
- **Deux protections contre les doublons, et chacune couvre un cas différent.**
  - Un **verrou consultatif par entreprise** sérialise deux imports simultanés. Sans lui,
    l'index empêche bien le doublon, mais l'un des deux imports échoue en 500 au lieu de
    répondre « déjà présents ». C'est la forme à deux entiers, dont aucune clé ne recoupe
    celles du verrou de la génération.
  - Un **index unique `(EntrepriseId, lower(Libelle))`**, migration `LibelleDeTypeUnique`,
    additive, en SQL brut : EF ne décrit pas un index sur expression. Il tranche quand
    deux créations ou deux renommages se croisent entre la vérification et l'écriture. Ce
    refus sort en **409**, pas en 500.
- ⚠️ **Le test de création concurrente a d'abord été vert à l'envers.** Huit créations
  simultanées du même libellé ne se chevauchaient qu'à froid. Dans la série, processus
  chaud, la première finissait avant que les autres lisent : le test passait avec la
  conversion du 409 retirée. Il est remplacé par une course **provoquée** :
  1. le test insère « PARKING » dans une transaction laissée ouverte ;
  2. il attend que l'insertion du contrôleur soit bloquée sur l'index, ce que
     `pg_stat_activity` montre ;
  3. il valide sa transaction, et la requête du contrôleur doit répondre 409.
- `TypesPrestationTests` : 25 tests.
  - Sept sabotages, tous rouges et pour la bonne raison : verrou retiré, conversion du
    23505 retirée, existants non sautés, clé sensible à la casse, import ouvert aux rôles
    internes, déjà-présents non rapportés, index supprimé de `klean_test`.
  - Ceux du verrou et de la conversion ont été rejoués trois fois, à froid et à chaud.

Reste au lot C :
- le bouton d'import sur l'écran du catalogue ;
- le préremplissage du libellé et de la durée dans le formulaire des prestations ;
- le retrait des types désactivés de la liste déroulante.

**Lot C — fait le 4 novembre 2026**

Trois ajouts **déclaratifs** au motif liste-et-formulaire, et aucune ligne propre au
catalogue dans `Reference.tsx` : le jour où le stock aura besoin d'un préremplissage ou
d'un geste de liste, il les déclarera.

- **`action`** sur une description : un `POST` sans corps, offert à qui écrit, et dont
  la réponse se résume en une phrase. Le catalogue déclare « Importer la liste type ».
  - Le compte rendu passe par l'annonce, comme une création.
  - Il **nomme les types laissés tels quels** : qui rejoue l'import après avoir corrigé
    une durée doit lire que sa correction a tenu.
  - Le message de liste vide nomme le geste.
- **`preremplit`** sur un champ de référence : choisir « Hall d'entrée » remplit le
  libellé et la durée. Un champ n'est réécrit que s'il est **vide**, ou s'il porte
  encore la valeur que le **choix précédent** y avait mise.
  - Le premier bras permet de se raviser : « Hall » puis « Escaliers » remplace bien.
  - Le second protège la saisie : un libellé propre au site, tapé ou déjà enregistré,
    n'est jamais écrasé.
  - Revenir à « — choisir — » ne vide rien.
- **`seulementActifs`** : les types désactivés sortent de la liste déroulante, **sauf**
  celui que la prestation porte déjà, gardé et dit « (désactivé) ». Le retirer aussi
  afficherait « — choisir — » sur une prestation qui a un type, alors que le serveur
  garde le lien. La valeur gardée est celle de l'ouverture : qui change de type puis se
  ravise peut y revenir.
- Le champ « Type » passe **avant** le libellé et la durée : on choisit, puis on lit ce
  qui a été proposé.
- Tests : `preremplir`, `optionsProposees` et `compteRenduImport` sont extraits et
  éprouvés, plus trois invariantes de structure. Un préremplissage, un filtre ou une
  action qui désigne un champ inexistant échoue à la série au lieu de ne rien faire.
  Sept sabotages, tous rouges.
- **Vérifié dans le navigateur, en administrateur** :
  - l'import, puis son rejeu ;
  - les quatre cas du préremplissage ;
  - la disparition d'un type désactivé de la liste.

  ⚠️ **Non vérifié à l'écran** : la valeur désactivée gardée sur une prestation
  existante. La base de développement n'a aucune prestation. Le cas est couvert par la
  série de `optionsProposees`, pas par le câblage.

---

# Étape 22 — Stock

La quatrième fonction du cœur de produit (`CLAUDE.md` §1), écartée de l'étape 16 bis
parce qu'elle a ses propres notions — emplacements, mouvements, consommation, alertes —
et n'est pas un formulaire de plus. L'étape 21 a posé ce dont elle avait besoin : un
**type** de prestation, auquel attacher une consommation standard.

**Ce qui existe, relu le 4 novembre 2026** — et trois constats changent la nature des
questions.

- `Produit` : nom, référence, unité, seuil d'alerte. Écran de référence en place.
- `Stock` : produit, type d'emplacement (`Depot`, `Vehicule`, `Site`), `EmplacementId`,
  quantité. CRUD par l'API pour `Exploitation`, **aucun écran**. Base de développement :
  zéro produit, zéro stock.
- ⚠️ **Ni dépôt ni véhicule n'existent comme entités.** `EmplacementId` est un `Guid`
  libre, et le contrôleur l'accepte sans contrôle, son commentaire le dit. Un stock
  « au dépôt » pointe donc vers un identifiant que rien ne nomme et que personne ne
  pourra afficher. La décision « sans clé étrangère » a été prise quand aucun écran ne
  devait jamais montrer l'emplacement.
- ⚠️ **La quantité se saisit**, elle ne se mouvemente pas. Aucune trace de qui l'a
  changée, quand, ni pourquoi. Rien n'empêche non plus deux lignes pour le même produit
  au même emplacement.
- ⚠️ **Rien ne fait passer une vacation à `Terminee`**, sauf un `PUT` brut. Il n'y a ni
  application agent ni certification. Une consommation « déduite à chaque passage » se
  déduirait donc de passages dont personne ne sait s'ils ont eu lieu.

**À trancher avant le lot A**

| Question | Options | Penchant |
|---|---|---|
| Les emplacements | entité `Emplacement` (dépôt, véhicule) avec vraies clés étrangères ; `Guid` libre comme aujourd'hui | **entité**. Un emplacement qu'on ne peut pas nommer ne peut pas s'afficher. Le stock référence alors soit un `Emplacement`, soit un `Site`, chacun par une clé en `Restrict`, avec une contrainte « exactement l'un des deux ». Cela **révise une décision prise** (tableau en fin de fichier), et la table est vide : c'est le moment le moins cher |
| Quantité ou mouvements | quantité saisie, comme aujourd'hui ; table `MouvementStock` — entrée, sortie, transfert, inventaire | **mouvements**. « Pourquoi le stock a-t-il baissé de douze litres ? » n'a pas de réponse sans eux, et le transfert du dépôt vers un véhicule est le geste quotidien. La quantité reste stockée, mise à jour par le service dans la même transaction que le mouvement, jamais saisie. Une seule ligne par produit et par emplacement, gardée par un index unique |
| Consommation standard | par type de prestation ; par prestation ; par type, avec exception par prestation | **par type, seulement, en V1**. Dix-sept types fois quelques produits se saisissent une fois. Par prestation, c'est des dizaines de lignes par site. L'exception par prestation donnerait deux sources à la même quantité, et peut attendre qu'un site la demande. Une prestation sans type ne consomme rien, et l'écran le dit |
| Ce que fait la consommation | déduite du stock à chaque vacation `Terminee` ; **prévisionnelle** — le besoin des semaines à venir, depuis le planning | **prévisionnelle**. Vacations planifiées × consommation de leur type, comparées au stock. Le réel s'établit par l'**inventaire**, un mouvement d'ajustement. La déduction automatique attend la certification des passages : avant elle, elle inventerait une consommation |
| Les alertes | seuil fixe du produit, qui existe ; couverture en semaines de besoin prévu ; les deux | **les deux** : « sous le seuil », puis « couverture courte ». Seuil **par produit**, tous emplacements confondus, en V1. ⚠️ **Pas de 🟢🟠🔴** comme l'écrivait la Suite : la terre cuite est réservée au « non pourvu » (`CLAUDE.md` §12), et chaque état porte un **mot** et une pastille, avec les jetons existants |
| Qui écrit | `Exploitation`, comme aujourd'hui ; chef d'équipe en plus | **`Exploitation`**, le chef d'équipe lit. Le chef recharge souvent un véhicule, mais aucun lien ne rattache un véhicule à une équipe. Le lui ouvrir sans ce lien ouvrirait tout le stock de l'entreprise, ce qui ne répond pas au besoin. À rouvrir avec un pilote |

**Réponses obtenues le 4 novembre 2026** — les six penchants sont retenus.

| Question | Réponse | Ce que cela veut dire dans le code |
|---|---|---|
| Emplacements | **entité `Emplacement`** | dépôts et véhicules : nom, type, actif, `EntrepriseId` et filtre global. `Stock` porte `EmplacementId` **ou** `SiteId`, deux clés nullables en `Restrict`, et une contrainte de base « exactement l'un des deux ». `TypeEmplacement` passe sur `Emplacement` (`Depot`, `Vehicule`), et le site est l'autre branche |
| Mouvements | **par mouvements** | `MouvementStock` : nature (entrée, sortie, transfert, inventaire), produit, origine et destination, quantité, date, auteur. `Stock.Quantite` n'est plus écrite que par le service, dans la transaction du mouvement. Index unique par produit et emplacement. Le CRUD direct de la quantité disparaît |
| Consommation standard | **par type, seulement** | `ConsommationType` : type de prestation, produit, quantité par passage. Aucune surcharge par prestation en V1 |
| Effet | **prévisionnel** | besoin = vacations planifiées non annulées × consommation de leur type, sur N semaines. Aucune déduction automatique ; l'inventaire constate le réel |
| Alertes | **seuil et couverture** | « Sous le seuil » d'après `Produit.SeuilAlerte`, tous emplacements confondus ; « Couverture courte » quand le stock ne couvre pas le besoin prévu. Un mot et une pastille, avec les jetons existants |
| Droits | **`Exploitation` écrit, le chef lit** | `RolesKlean.Exploitation` sur les mouvements, les emplacements et les consommations ; lecture `Interne` |

⚠️ **Le lot A supprime des colonnes de `Stocks`** — `TypeEmplacement` et le `Guid` libre
`EmplacementId`, remplacés par deux clés réelles. C'est une opération destructrice sur une
table métier, que `CLAUDE.md` §14 soumet à accord. La table est vide en développement ; la
migration sera présentée, et une instance déjà peuplée demanderait une reprise des
lignes existantes, qu'aucune n'a aujourd'hui.

**Conséquences à vérifier au cadrage**

- **`RGPD.md`** : un mouvement porte son **auteur**, sans quoi la trace ne répond pas à
  « qui ». C'est une donnée personnelle, celle d'un collègue. Il faut l'inscrire dans la
  cartographie, avec une durée de conservation, et ne pas la journaliser : elle désigne
  qui a écrit, elle ne divulgue rien.
- **Section `ST`** : Produits, Emplacements, Stocks, et la vue des alertes. Les libellés
  des pastilles annoncés « à revoir à l'étape des Stocks » (`sections.ts`) se revoient ici.
- **Hors V1, et dit tel quel** : commande fournisseur et prix d'achat, qui relèvent de
  l'ERP hors périmètre (`CLAUDE.md` §1) ; déduction au passage, qui attend la
  certification ; stock par équipe.
- **Proposition de réapprovisionnement** : besoin prévu moins stock, par emplacement, en
  liste. C'est une lecture, pas une commande.

**Lots, provisoires**

- **A** — `Emplacement`, la refonte de `Stock` en clés réelles et index unique, les
  mouvements et leur service : entrée, sortie, transfert, inventaire. Une migration,
  présentée avant d'être appliquée.
- **B** — consommation standard par type, besoin prévisionnel sur N semaines, alertes et
  couverture.
- **C** — les écrans de la section `ST` : emplacements, stock par emplacement, saisie
  d'un mouvement, vue des alertes et du réapprovisionnement.

**Lot A — fait le 4 novembre 2026**

- **Le modèle.**
  - `Emplacement` : dépôt ou véhicule, avec nom et état actif.
  - `Stock` : `EmplacementId` **ou** `SiteId`, deux clés réelles en `Restrict`.
  - `MouvementStock` : nature, origine, destination, quantité, écart d'inventaire, date
    et auteur, ces deux derniers posés par le serveur.
  - `Produit.Actif`.
  - Deux filtres globaux de plus, quinze en tout.
- **Migration `EmplacementsEtMouvementsDeStock`.** Elle **supprime** `Stocks.TypeEmplacement`
  et rend `EmplacementId` nullable : la table était vide. Elle s'ouvre sur une garde
  qui **refuse de s'appliquer** si `Stocks` contient des lignes, en disant comment les
  reprendre, plutôt que d'échouer au milieu sur une clé étrangère.
- **Trois gardes de base** sous le service :
  - un lieu et un seul par stock ;
  - une quantité jamais négative ;
  - une ligne par produit et par lieu, par deux index uniques partiels.
- **`ServiceStock`** : un mouvement et ses quantités dans une transaction, sous un verrou
  consultatif par entreprise, espace 22. Un refus n'écrit rien.
  - Une sortie au-delà du disponible est refusée, et le message nomme l'inventaire.
  - Un inventaire fixe la quantité et garde l'écart.
  - Un emplacement ou un produit désactivé ne **reçoit** plus rien ; ce qu'il porte peut
    encore sortir et se compter.
- **Les routes.**
  - `/api/emplacements` : CRUD complet.
  - `/api/mouvements-stock` : lecture et création seulement.
  - `/api/stocks` : **lecture seule**, filtrable par produit, emplacement et site avant
    le comptage.
- **Trois refus de suppression réécrits**, parce que leur sortie se fermait :
  - produit et emplacement nomment la désactivation ;
  - un site cité par un mouvement le dit, sans promettre que vider son stock le
    débloquerait.
- **Les énumérations nouvelles sont lues par leurs noms exacts.** `Enum.TryParse`
  accepterait « 0 », donc une entrée que personne n'a choisie.
- `StockTests` : 28 tests. Dix sabotages, tous rouges. Celui du verrou a été rejoué trois
  fois, et il échoue pour la bonne raison : une sortie finit sans attendre le verrou.
  - Le test de concurrence **prend lui-même le verrou du service** : les deux sorties
    doivent l'attendre, sans quoi il échoue avant de compter.
  - Un message refusé a fait trouver une faute : « 6 litre disponible(s) ». L'unité est
    au singulier et sort de la phrase.
- **Journal d'accès** : l'invariante structurelle a attrapé `EmplacementDto`, qui porte un
  `Nom`. Il rejoint `ProduitDto` dans les exemptions motivées : un dépôt, un véhicule.
  La limite est écrite, comme pour les consignes d'un site : l'écran du lot C dira de
  nommer un véhicule par sa plaque, jamais par son conducteur.
- **Écran** : le champ « Actif » des produits, exigé par le test de parité. Le reste
  attend le lot C.
- `conservation-donnees-metier.sql` lit `SiteId` et les mouvements ; il a été rejoué sur
  `klean_test`.
- ⚠️ **Écart ouvert, dit dans `RGPD.md` §5** : les mouvements portent leur auteur, et
  aucun script n'en gère encore la conservation. **Fermé après le lot C** :
  `conservation-donnees-metier.sql` §8 rapporte les mouvements de plus de 5 ans et
  les auteurs encore identifiables, et porte leur `DELETE` en commentaire, placé
  **avant** celui des sites. Éprouvé dans une transaction annulée : deux mouvements
  vieillis de six ans ont été supprimés, et le stock n'a pas changé.

**Lot B — fait le 4 novembre 2026**

Trois réponses obtenues au début du lot :

| Question | Réponse |
|---|---|
| Horizon par défaut | **quatre semaines**, de 1 à 12 sur demande — la limite de la génération |
| Où se mesure la couverture | **par site, puis réserve** : chaque site puise dans son propre stock, les dépôts et véhicules couvrent le reste ; l'excédent d'un site ne compte pas pour un autre |
| Qui lit la prévision | **`Exploitation` seule** — défaut trouvé avant de coder : les vacations d'un chef sont filtrées sur son équipe, le stock ne l'est pas, et sa prévision aurait comparé le besoin d'une équipe au stock de l'entreprise. À rouvrir avec un rattachement des véhicules aux équipes |

- **`ConsommationType`** : type de prestation, produit, quantité par passage. Deux clés
  `Restrict`, un couple unique par index, une quantité strictement positive par
  contrainte de base. Seizième filtre global.
  - `/api/consommations-type` : CRUD, lecture `Interne`, écriture `Exploitation`,
    filtrable par type et par produit avant le comptage.
  - Un doublon rend 409 ; une saisie concurrente tombée sur l'index aussi, et non 500.
  - Un produit désactivé ne s'attache plus ; la ligne qui le portait se corrige encore.
- **`GET /api/stocks/prevision?semaines=4`**, dans `ServicePrevisionStock`. Pour chaque
  produit actif :
  - le **besoin**, c'est-à-dire les vacations `Planifiee` et `EnCours` d'aujourd'hui à
    quatre semaines, multipliées par la consommation de leur type, site par site ;
  - le **stock total** et la **réserve** ;
  - le **manque**, ce que la réserve ne couvre pas une fois chaque site servi par son
    propre stock ;
  - les deux alertes, **« Sous le seuil »** (total strictement sous `SeuilAlerte`) et
    **« Couverture courte »** (manque positif) ;
  - et, par site, ce qu'il faut y apporter : la base du réapprovisionnement du lot C.

  Les produits en alerte viennent d'abord. Les vacations sans type ne consomment rien,
  et la réponse en donne le nombre.
- **Aucune déduction** : la prévision lit le planning et le stock, elle n'écrit rien.
- **Refus de suppression** : un produit ou un type cité seulement par des consommations
  le dit, et nomme la sortie — les retirer, ou désactiver. Un produit cité par un
  mouvement, ou un type porté par une prestation, garde son refus d'avant.
- **`ServicePrevisionStock` lit `TimeProvider`**, comme la génération : « les quatre
  semaines à venir » ne s'éprouve pas sur un jour qui change.
- Migration **`ConsommationsParType`**, purement additive : une table, trois index.
- `PrevisionStockTests` : 17 tests. Dix-huit sabotages, tous rouges :
  - statut terminé compté ;
  - fin de période incluse ;
  - stock des sites versé dans la réserve ;
  - seuil large au lieu de strict ;
  - produits désactivés gardés ;
  - ordre des alertes ;
  - vacations sans type tues ;
  - borne de l'horizon ;
  - rôle de la prévision ;
  - vérification de doublon ;
  - conversion du 23505, rejouée trois fois et dans la série ;
  - produit désactivé attaché ;
  - ligne existante figée ;
  - filtre par type après le comptage ;
  - type inconnu accepté ;
  - les deux refus de suppression ;
  - filtre global retiré.
- **Aucun écran** : les consommations, la prévision et les alertes s'affichent au lot C.

**Lot C — fait le 4 novembre 2026**

La section `ST`, libellée « Stock » : Produits › Emplacements › Consommations › Stock ›
Prévision.

- **Deux écrans de référence**, sans page écrite :
  - **Emplacements** : nom, type, actif. L'aide du nom dit « la plaque ou le modèle,
    jamais le conducteur » : c'est la condition de l'exemption d'`EmplacementDto` au
    journal d'accès.
  - **Consommations** : elles descendent du catalogue. Chaque type offre « Voir les
    consommations », et la descente traverse les sections.
- **Deux écrans propres**, déclarés dans `ecrans` de la section. Une quantité ne se
  saisit pas et une prévision ne s'enregistre pas, donc ce ne sont pas des listes à
  formulaire.
  - **Stock** : les quantités par lieu, filtrables par lieu, et les derniers mouvements.
    La saisie d'un mouvement est réservée à l'`Exploitation`. Elle ne montre que les
    lieux qu'exige la nature, et n'envoie que ceux-là. Un refus du serveur s'affiche tel
    quel : il nomme l'inventaire.
  - **Prévision** : réservée à l'`Exploitation`, et l'onglet est absent pour le chef.
    - Un horizon de 1 à 12 semaines.
    - Une ligne par produit, avec un mot et une pastille par état, dans le ton du retard
      et jamais en terre cuite.
    - Un détail par site, qui dit ce qu'il faut y apporter : c'est la proposition de
      réapprovisionnement, en lecture.
    - Les vacations sans type sont dites, avec la sortie.
- **Pastilles revues** : `SI` devient `CL`, puisque la section commence par Clients.
- **Logique extraite** dans `stock/mouvements.ts` et `stock/prevision.ts`, avec 20 tests.
  Onze sabotages côté écran, tous rouges :
  - côté non exigé envoyé ;
  - lieux de l'inventaire ;
  - transfert sur place ;
  - quantité en chaîne ;
  - alerte de couverture tue ;
  - onglets sans rôles ;
  - prévision ouverte au chef ;
  - rôle d'écran hors de la section ;
  - chemin monté deux fois ;
  - accord de la phrase des vacations sans type ;
  - champ de description sans DTO.
- **Vérifié dans le navigateur**, sur un petit jeu saisi dans la base de développement :
  - la prévision rend les chiffres attendus ;
  - un transfert excessif est refusé avec le message du serveur ;
  - un inventaire passe, avec l'origine du transfert précédent restée dans le formulaire,
    et son écart s'inscrit ;
  - la descente depuis le catalogue fonctionne.
  - Un défaut vu et corrigé : la nature d'un mouvement se coupait au milieu du mot.
- **Limites vues** : le moteur des écrans de référence affichait les nombres avec un
  point (« 0.5 ») et les énumérations par leur nom brut (« Depot »). **Corrigé juste
  après** : un champ `choix` porte ses `libelles` — « Congé » s'affiche, `Conge` part —,
  une invariante exige un libellé par valeur et rien de plus, et `rendu` comme
  `etiquetteDe` écrivent les nombres à la virgule. Six sabotages, tous rouges.
  L'auteur d'un mouvement n'est pas affiché : la route des comptes est réservée à
  l'administrateur.

---

# Étape 23 — Double authentification

Placée **avant** l'écran agent, parce qu'un pilote est attendu dans un mois. Ce qu'un
compte administrateur expose : les coordonnées du domicile de chaque salarié, ses arrêts
maladie, le dossier complet de l'article 15, et la création de comptes. Aujourd'hui, un
mot de passe suffit pour tout cela. Le verrouillage et la limitation de débit freinent
qui devine un mot de passe ; ils ne font rien contre celui qui l'a, par hameçonnage ou
parce qu'il sert ailleurs.

**Ce qui existe, relu le 4 novembre 2026**

- **Identity porte déjà le mécanisme.** Il fournit la colonne
  `AspNetUsers.TwoFactorEnabled`, la table `AspNetUserTokens` (créée par la migration
  `IdentiteEtRoles`), le fournisseur TOTP enregistré par `AddDefaultTokenProviders`, et
  les codes de récupération à usage unique (`GenerateNewTwoFactorRecoveryCodesAsync`,
  `RedeemTwoFactorRecoveryCodeAsync`). Le lot A ne devrait **pas** demander de
  migration, sauf peut-être pour la parade au rejeu ci-dessous.
- ⚠️ **La connexion ignore tout second facteur.** `AuthController.Connexion` appelle
  `CheckPasswordSignInAsync`, qui ne consulte pas `TwoFactorEnabled`. Le parcours en deux
  temps de `PasswordSignInAsync` repose sur un cookie d'Identity que ce projet n'emploie
  pas (`CLAUDE.md` §10, schémas forcés sur le JWT). Il faut donc l'écrire.
- **Aucune bibliothèque ne dessine un QR code.** Ni côté serveur ni côté écran, et
  `CLAUDE.md` §3 exige de proposer et de justifier toute dépendance.
- Le flux de mot de passe oublié **ne contourne pas** le second facteur : il change le
  mot de passe, et la connexion suivante demande toujours le code. C'est le comportement
  voulu, et un test devra le dire.

**Le parcours, tel que je le propose**

1. `POST /api/auth/connexion` avec un bon mot de passe, sur un compte équipé, n'ouvre
   **aucune** session. Il répond **202** avec `{ etape: "SecondFacteur" }` et pose un
   cookie de défi : `HttpOnly`, `Secure`, `SameSite=Strict`, chemin `/api/auth`, cinq
   minutes. Ce cookie est protégé par la Data Protection et porte l'identifiant du compte,
   son `SecurityStamp` et l'échéance. Un changement de mot de passe entre les deux temps
   rend donc le défi caduc.
2. `POST /api/auth/connexion/code` avec `{ code }` ou `{ codeRecuperation }` ouvre la
   session, par `ServiceSession.OuvrirAsync`, comme aujourd'hui.
   - Un code faux rend **400**, puisque c'est le corps qui est faux (`CLAUDE.md` §10).
     L'échec est compté par `AccessFailedAsync` : les cinq échecs du verrouillage sont
     **partagés** avec le mot de passe. Sans ce partage, un million de codes à six
     chiffres se testeraient à côté du verrou.
   - Un défi expiré ou absent rend **401**, et la route rejoint `SansRafraichissement`
     côté client.
   - La route a son plafond de débit, par adresse et par compte.
3. Le second facteur se demande **à la connexion seulement**. Le rafraîchissement ne le
   redemande pas : la session absolue de quatorze jours borne déjà l'ensemble. Il n'y a
   pas de « se souvenir de cet appareil » en V1 ; ce serait un second jeton de longue
   durée à rendre révocable.
4. **L'activation** se fait depuis l'écran du compte, en deux temps. `preparer` engendre
   la clé et la rend en clair, avec l'URI `otpauth://` et le QR code. `activer { code }`
   prouve que l'application du téléphone produit le bon code, puis rend **dix codes de
   récupération, une seule fois**. L'activation régénère l'empreinte et rouvre la session
   de l'appelant, comme le changement de mot de passe : une session ouverte sans second
   facteur ne doit pas survivre à l'activation sur un autre appareil.

**À trancher avant le lot A**

| Question | Options | Penchant |
|---|---|---|
| Qui y est soumis | obligatoire pour l'administrateur et le responsable, facultative pour les autres ; obligatoire pour tous les rôles internes ; facultative pour tous | **Administrateur et Responsable obligatoires, facultative pour tous les autres comptes.** Ces deux rôles lisent les données de santé et les domiciles. Le chef d'équipe ne voit que son équipe, et l'obliger alourdit le pilote. Facultative, la double authentification ne coûte rien à qui la veut, y compris un syndic |
| Un compte obligé et pas encore équipé | équipement imposé **dans** la connexion, aucune session sans second facteur ; délai de grâce de N jours avec un bandeau ; refus de connexion | **Dans la connexion.** Le bon mot de passe d'un tel compte rend 202 `{ etape: "Equipement" }` avec le même cookie de défi, et `preparer` puis `activer` acceptent ce défi à la place d'une session. Le premier administrateur de l'amorçage passe par là à sa première connexion. Un délai de grâce est une période où l'obligation n'existe pas, et elle dure en pratique jusqu'à la fin du délai |
| Le QR code | la clé en texte seule, à recopier (32 caractères) ; **QRCoder** côté serveur (licence MIT, entièrement managé) ; un encodeur écrit à la main | **QRCoder, côté serveur, à justifier comme dépendance.** Recopier 32 caractères sur un téléphone, c'est l'erreur de saisie assurée au pilote. Un encodeur écrit à la main représente plusieurs centaines de lignes de Reed-Solomon et de masques, pour une fonction que personne ne relira. Le serveur rend la **matrice** des modules, et l'écran la dessine en `<rect>` SVG : pas de `innerHTML`, aucune politique de contenu touchée. La clé en texte reste affichée à côté, pour qui l'utilise sur le même téléphone. Absence de dépendance transitive et version à vérifier avant l'installation |
| Téléphone perdu | codes de récupération seuls ; plus une réinitialisation par un administrateur ; plus un script d'exploitation pour le dernier administrateur | **Les trois.** Un code de récupération ouvre la session une fois et se consomme. Un administrateur peut **réinitialiser** la double authentification d'un autre compte de l'entreprise, jamais la sienne : cela retire la clé et les codes, régénère l'empreinte et coupe les sessions. Le compte repasse alors par l'équipement à la connexion suivante. Pour **le dernier administrateur**, la situation ordinaire d'une entreprise de cinq personnes, aucun rattrapage par l'API n'est possible sans affaiblir la règle. Il faut un script SQL d'exploitation, documenté dans `DEPLOIEMENT.md`, à la main de qui détient la base |

⚠️ **Où tracer la réinitialisation par un administrateur** : la question reste ouverte, et
mon penchant contredit une règle écrite. Le journal d'accès ne consigne que les
divulgations (`CLAUDE.md` §7 bis, règle 5). Mais il porte déjà l'anonymisation, autre
geste d'un administrateur sur le compte d'autrui, qui n'est pas une lecture. Une nature
`ReinitialisationSecondFacteur` suivrait ce précédent. Une table de sécurité à part
serait plus juste, et c'est une entité de plus. Je penche pour le journal d'accès, la
règle 5 complétée par une exception écrite.

**Réponses obtenues le 4 novembre 2026** — les quatre penchants posés en question sont
retenus. Celui du compte pas encore équipé n'a pas été posé : il est retenu par défaut,
sauf avis contraire.

| Question | Réponse | Ce que cela veut dire dans le code |
|---|---|---|
| Qui | **Administrateur et Responsable obligés, facultative pour les autres** | une liste de rôles obligés dans `RolesKlean`, lue à la connexion ; la désactivation est refusée à un compte obligé |
| Compte pas encore équipé | **équipement dans la connexion** (par défaut) | 202 `{ etape: "Equipement" }`, puis `preparer` et `activer` acceptent le défi à la place d'une session |
| QR code | **QRCoder, côté serveur** | dépendance présentée et justifiée avant installation ; le serveur rend la matrice, l'écran dessine le SVG |
| Téléphone perdu | **codes, administrateur, script** | dix codes à usage unique ; `POST /api/auth/comptes/{id}/reinitialiser-second-facteur`, administrateur seul, jamais sur soi, cadré à la main sur `EntrepriseId` ; un script SQL pour le dernier administrateur |
| Traçage | **journal d'accès** | nature `ReinitialisationSecondFacteur`, ressource `Compte` ; la règle 5 de `CLAUDE.md` §7 bis reçoit son exception écrite, et le test qui fige la liste des actions journalisées s'allonge d'une |

**Conséquences à vérifier au cadrage**

- ⚠️ **La série de tests.** Toutes les fixtures se connectent en administrateur. Avec
  l'obligation, chacune doit s'équiper puis produire un code. Le calcul TOTP côté test
  tient en une vingtaine de lignes (RFC 6238, HMAC-SHA1), sans dépendance. Un réglage
  qui désactiverait l'obligation en test est **exclu** : un drapeau conditionnel sur un
  attribut de sécurité finit à `false` en production (`CLAUDE.md` §10).
- ⚠️ **Le rejeu d'un code.** À ma connaissance, le fournisseur d'Identity accepte le même
  code pendant toute sa fenêtre de validité, sans mémoriser le dernier pas de temps
  accepté. À vérifier au lot A. Si c'est le cas, il faut mémoriser ce pas par compte, ce
  qui demande une colonne, donc une migration additive.
- ⚠️ **Le stockage de la clé.** À ma connaissance, Identity range la clé de
  l'authentificateur et les codes de récupération **en clair** dans `AspNetUserTokens`,
  sauf protection explicite (`ProtectPersonalData`). Une copie de la base livrerait alors
  le second facteur de tous les comptes. À vérifier, puis à trancher : chiffrer par la
  Data Protection, ou dire la limite dans `RGPD.md` et `DEPLOIEMENT.md`.
- **Les deux pseudonymisations** (`CLAUDE.md` §7 bis) doivent retirer la clé et les codes
  de récupération. C'est une ligne de plus dans la relecture en base, champ par champ,
  que leurs tests font déjà.
- **`DEPLOIEMENT.md`** : l'horloge du serveur doit être synchronisée. Un écart de plus
  d'une minute environ refuse tous les codes, sans autre symptôme qu'un « code faux ». Le
  script du dernier administrateur y figure aussi.
- **Hors V1, et dit tel quel** : clés matérielles et WebAuthn, code par SMS (un service
  tiers payant, contraire à `CLAUDE.md` §2), code par courriel (le SMTP est optionnel,
  et la boîte est souvent le maillon déjà compromis).

**Lots, provisoires**

- **A** — le serveur :
  - le parcours en deux temps, le défi, l'équipement et l'activation ;
  - les codes de récupération ;
  - la réinitialisation par un administrateur et l'obligation par rôle ;
  - la dépendance QRCoder, présentée avant installation ;
  - le calcul TOTP des tests et la mise à niveau des fixtures ;
  - un sabotage par garde.
- **B** — les écrans :
  - le second temps de la connexion et l'équipement imposé ;
  - la section « Double authentification » de l'écran du compte : activer, régénérer
    les codes, désactiver quand le rôle le permet ;
  - la réinitialisation côté administrateur. ⚠️ **Aucun écran ne liste les comptes** :
    `GET /api/agents/chefs-equipe` est la seule liste, et elle est journalisée. Il faut
    une liste des comptes de l'entreprise, journalisée elle aussi puisqu'elle nomme des
    personnes. Elle servirait aussi la création de comptes, qui n'a pas d'écran non plus.
    C'est à mesurer au début du lot B, et peut-être un lot à part ;
  - l'extraction de la logique dans un module éprouvé, comme `auth/motDePasse.ts`.
- **C** — l'exploitation : le script du dernier administrateur, `DEPLOIEMENT.md`,
  `RGPD.md`, `CLAUDE.md` §6.

**Lot A — fait le 4 novembre 2026**

Trois vérifications faites au début du lot, dans le code source d'Identity .NET 10 et sur
NuGet. Les trois ont changé quelque chose, et chacune a reçu une réponse :

| Constat | Réponse |
|---|---|
| QRCoder 1.8.1 tire `System.Drawing.Common` 6.0.0, propre à Windows | **Net.Codecrete.QrCodeGenerator 3.2.1** : MIT, aucune dépendance, portage du générateur de Nayuki |
| La clé et les codes de récupération sont rangés **en clair** dans `AspNetUserTokens`, et les codes ne portent que 50 bits | **clé chiffrée, codes par empreinte** |
| `AuthenticatorTokenProvider` accepte cinq pas à la fois et ne mémorise rien : un code resservait pendant deux minutes et demie | **vérification écrite ici**, un pas de tolérance, un code ne sert qu'une fois |

- **Rien d'Identity n'est repris, sauf `TwoFactorEnabled` et la table des jetons.** La
  clé, les empreintes et le dernier pas accepté sont trois jetons du fournisseur
  `[KleanSecondFacteur]`, écrits par `UserManager` : pas de sous-classe du magasin, et
  pas de migration.
  - La clé est chiffrée par la Data Protection.
  - Les codes de récupération sont **les nôtres** : seize caractères de Crockford, soit
    80 bits. Une empreinte SHA-256 de 50 bits se casse hors ligne ; de 80, non.
  - Toute écriture passe par `UpdateAsync` d'Identity, qui compare le
    `ConcurrencyStamp`. De deux usages simultanés du même code, le second échoue donc à
    l'écriture. ⚠️ **Non éprouvé par un test** : c'est une propriété d'Identity, et le
    provoquer de façon déterministe demanderait d'intercepter son `SaveChanges`.
- **`Totp.cs`** : RFC 6238 sur HMAC-SHA1, éprouvé sur les six vecteurs de l'annexe B.
  Il lit `TimeProvider`, donc les fenêtres et l'échéance du défi se testent sur une
  horloge figée. Les tests produisent leurs codes avec `TotpDeTest`, écrit sans rien
  reprendre du serveur.
- **Le parcours.**
  - `POST /api/auth/connexion` rend **202** `{ etape }` avec un cookie de défi :
    `SecondFacteur` si le compte est équipé, `Equipement` si son rôle l'oblige.
  - `POST /api/auth/connexion/code` attend `{ code }` ou `{ codeRecuperation }`. Un échec
    compte dans le verrouillage d'Identity, **partagé avec le mot de passe** : Identity ne
    remet pas le compteur à zéro sur un bon mot de passe quand le second facteur est
    actif, et c'est éprouvé.
  - `second-facteur/preparer` et `second-facteur/activer` acceptent une session ou un défi
    d'`Equipement`, et lui seul. Un défi du second temps ne prépare pas une autre clé ;
    sans cette garde, le mot de passe seul aurait suffi à remplacer le téléphone d'un
    compte équipé.
  - `GET /api/auth/second-facteur`, `second-facteur/codes` et `second-facteur/desactiver`
    servent l'écran du compte. Les deux derniers exigent un code de l'application.
- ⚠️ **Écart au cadrage : la désactivation reste ouverte aux comptes obligés.** C'est leur
  geste « changer de téléphone » : toutes leurs sessions se ferment, et la connexion
  suivante impose l'équipement. Sans cela, un administrateur qui change de téléphone
  devait demander à un collègue de le réinitialiser.
- **Aucune session sans second facteur pour un compte obligé**, à deux endroits :
  `OnTokenValidated`, sans lecture de plus (le compte est déjà chargé, les rôles sont dans
  le jeton), et le rafraîchissement. Sans eux, une session de quatorze jours ouverte avant
  le déploiement continuait de se renouveler par mot de passe seul.
- **`POST /api/auth/comptes/{id}/reinitialiser-second-facteur`** : administrateur seul,
  jamais sur soi, cadré à la main sur l'entreprise, et journalisé en
  `Compte` / `ReinitialisationSecondFacteur`. Quatorze actions sont maintenant
  journalisées.
- **Les deux pseudonymisations** retirent l'indicateur et toutes les lignes
  `AspNetUserTokens` du compte.
- **La série de tests.**
  - La fixture équipe chaque compte obligé à sa première connexion, par les routes de
    l'écran et sur un « appareil » à part, qu'elle déconnecte aussitôt.
  - ⚠️ Avant chaque code, elle **oublie le dernier pas accepté** : un test qui connecte
    deux fois le même compte en trente secondes buterait sinon sur la garde du rejeu.
    C'est un contournement délibéré, écrit dans l'aide, et `SecondFacteurTests` ne
    l'appelle jamais.
  - Les sept séries qui appelaient la connexion à la main passent par
    `ConnexionCompleteAsync`. Une série d'exploitation se connecte en chef d'équipe : elle
    envoie ses requêtes à la main, sans second temps.
- `SecondFacteurTests` : 43 tests. **Trente-trois sabotages, tous rouges** :
  - le calcul : troncature, fenêtre de deux pas, rejeu ;
  - le défi : étape, échéance, empreinte, compte désactivé ;
  - le stockage : clé en clair, codes en clair, code réutilisable, normalisation ;
  - le verrou : échec non compté, compteur non remis, défi accepté pendant le verrou ;
  - l'équipement : activation sans code, sans révocation, sans plafond, seconde clé
    préparée sur une session ;
  - l'écran du compte : régénération et désactivation sans code, compte obligé non
    déconnecté, clé gardée au retrait ;
  - la réinitialisation : entre entreprises, sur soi, sans retrait, sans révocation, non
    journalisée ;
  - les gardes de session : connexion sans second temps, sans obligation, jeton et
    rafraîchissement sans obligation ;
  - les deux pseudonymisations.

  Le sabotage « réinitialisation sans révocation » restait couvert par le refus du
  rafraîchissement tant que la cible était un compte obligé. Un test sur un chef d'équipe
  équipé de son plein gré l'isole.
- Série complète : **609 tests verts**. Sa durée passe de 3 minutes à 6 min 25 s : chaque
  première connexion d'un compte obligé coûte maintenant six requêtes.
- ⚠️ **Écart ouvert jusqu'au lot B** : l'écran de connexion ne connaît pas le 202. Un
  administrateur ne peut donc plus se connecter depuis le navigateur. **Fermé au lot B.**
- ⚠️ **Limite assumée** : le premier équipement d'un compte obligé se fait sur le mot de
  passe seul. Qui détient ce mot de passe avant le titulaire peut équiper son propre
  téléphone. C'est le cas de tout premier enrôlement, et c'est le cas que la
  réinitialisation par un administrateur répare.

**Lot B — fait le 6 novembre 2026**

- **La connexion en trois temps**, dans `pages/Connexion.tsx` : le mot de passe ; puis le
  code, ou un code de récupération (« Téléphone perdu ? ») ; ou l'équipement imposé.
  - Un 401 au second temps ramène au mot de passe avec le message du serveur. Un 400
    laisse sur place, et vide le champ.
  - `connexion/code` rejoint `SansRafraichissement` : avant le code, il n'y a pas de
    session à rafraîchir.
- **Les codes de récupération ne s'adoptent qu'après avoir été vus.** L'activation rouvre
  bien une session côté serveur, mais adopter le profil tout de suite ferait quitter
  l'écran de connexion avant de les montrer, et ils ne se revoient jamais. D'où
  `adopterSession` dans le contexte, et une case « rangés en lieu sûr » avant de
  continuer.
- **La préparation part d'un clic, jamais du montage.** Chaque appel remplace la clé : le
  double montage de `StrictMode` en lancerait deux, et des réponses croisées montreraient
  une clé déjà remplacée, donc un QR code qui ne marche pas, sans aucune erreur.
- **Le QR code** est un `<path>` SVG construit en données (`traceQr`) : aucun
  `innerHTML`, aucune politique de contenu touchée. Il est dessiné noir sur blanc dans
  les deux thèmes, avec deux jetons que le bloc sombre ne redéfinit pas, puisqu'un QR
  inversé n'est pas lu partout. `npm run verifier` mesure leur contraste dans les deux
  thèmes.
- **L'écran du compte** porte une section « Double authentification » : l'état, les
  codes restants, et deux gestes qui exigent un code — régénérer les codes, et
  désactiver. Pour un compte obligé, ce second geste s'appelle « Changer de téléphone ».
  Un compte au rôle facultatif l'active depuis cet écran.
- **Les refus locaux** sont dans `auth/secondFacteur.ts`, avec 14 tests : six chiffres,
  seize caractères de Crockford avec les mêmes confusions ramenées que le serveur, et
  une seule des deux clés envoyée. Ce n'est pas du confort : côté serveur, une saisie
  mal formée compte dans les cinq échecs du verrou.
- Huit sabotages côté écran, tous rouges :
  - le second temps rafraîchi ;
  - cinq chiffres acceptés ;
  - O non ramené à 0 ;
  - les axes du QR inversés ;
  - la marge comptée une fois ;
  - un code de récupération envoyé comme code ;
  - une étape non reconnue ;
  - les espaces gardés.
- **Vérifié dans le navigateur**, avec le compte administrateur de développement et un
  vrai téléphone :
  - l'ancienne session est tombée au déploiement ;
  - l'équipement imposé, puis l'arrivée sur le planning ;
  - l'écran du compte affiche « Active, imposée par votre rôle », 10 codes sur 10 ;
  - le refus local d'un champ vide ;
  - la déconnexion, puis une connexion par code.
- ⚠️ **Reste ouvert** : la réinitialisation par un administrateur n'a pas d'écran. Aucune
  liste des comptes n'existe, ni pour réinitialiser, ni pour créer un compte. C'est un
  lot à part, à cadrer : la liste nomme des personnes, donc elle se journalise.

**Correctif des lots A et B — 6 novembre 2026**

Trouvé en écrivant la procédure « téléphone perdu » du lot C : **« Changer de téléphone »
n'acceptait que le code de l'application**, c'est-à-dire précisément ce que n'a plus qui a
perdu son téléphone.
- Un code de récupération ouvrait bien une session, mais aucun geste ne permettait ensuite
  de rééquiper. Chaque connexion brûlait un code, jusqu'à ce qu'un administrateur
  réinitialise le compte.
- Le dernier administrateur, lui, ne peut pas se réinitialiser lui-même : avec ses dix
  codes en main, il finissait quand même au script d'exploitation.
- Le commentaire de `Reinitialiser`, « qui est connecté a son second facteur », était faux
  pour la même raison.

Corrigé :
- `POST /api/auth/second-facteur/desactiver` accepte un code de récupération, qu'il
  consomme, avec la règle de la connexion : une saisie et une seule. La régénération des
  codes reste sur le code de l'application ; sans téléphone, ce n'est pas elle qu'il faut.
- L'écran du compte porte la même bascule qu'à la connexion, « Téléphone perdu ? Utiliser
  un code de récupération », qui masque la régénération.
- Trois tests :
  - le téléphone perdu de bout en bout, où le code de la connexion ne resert pas ;
  - les deux saisies ensemble, ou aucune ;
  - **le trousseau perdu** : la clé chiffrée par un autre trousseau rend une erreur
    serveur sans compter d'échec, et les codes de récupération suffisent à changer de
    téléphone. Aucun test n'éprouvait encore la clé indéchiffrable.
- Quatre sabotages, tous rouges :
  - le code de l'application seul ;
  - un code de récupération accepté sans vérification ;
  - la forme non contrôlée ;
  - la clé indéchiffrable lue comme absente.

**Lot C — fait le 6 novembre 2026**

- **`backend/Scripts/reinitialiser-second-facteur.sql`**, pour le dernier administrateur
  sans téléphone ni codes, le seul cas sans sortie par l'API.
  - Il fait les gestes de la route d'administration : second facteur à faux, les trois
    jetons `[KleanSecondFacteur]` effacés, `SecurityStamp` et `ConcurrencyStamp`
    régénérés, sessions révoquées.
  - En deux temps : un rapport qui nomme la personne, l'entreprise et le rôle, puis
    `-v confirmer=1`.
  - ⚠️ Un seul bloc `DO`, donc une seule instruction : un second facteur retiré aux
    sessions encore ouvertes serait le pire des états intermédiaires. Et aucun `BEGIN`,
    pour que le script s'éprouve dans une transaction annulée.
  - **Éprouvé sur `klean_test`**, dans une transaction annulée :
    - les quatre gestes, sur une adresse en casse mélangée ;
    - le compte témoin d'une autre entreprise et un jeton d'un autre fournisseur, épargnés ;
    - une adresse inconnue refusée sans rien modifier ;
    - l'annulation, qui rend tout intact.

    Il fonctionne aussi par l'entrée standard, comme le lance `docker compose exec`.
  - Il ne laisse **aucune ligne** au journal d'accès : le journal attribue chaque ligne à
    un compte, et l'exploitant n'en est pas un. `DEPLOIEMENT.md` lui demande de consigner
    le geste, et de vérifier l'identité du demandeur avant, hors de KLEAN.
- **`DEPLOIEMENT.md`** :
  - la première connexion impose d'équiper un téléphone (§2.4) ;
  - le trousseau `cles-api` se sauvegarde et se restaure, **jamais avec la base** (§4) ;
  - le tableau des volumes dit ce que sa perte coûte (§6) ;
  - une §10 : l'horloge, le téléphone perdu, le dernier administrateur, le trousseau perdu.

  ⚠️ Les commandes `tar` du trousseau **n'ont été jouées sur aucune instance Docker**, et
  le document le dit, contrairement à sa promesse d'en-tête.
- **`RGPD.md`** :
  - `AspNetUserTokens` entre dans T4, dans la cartographie et dans les durées ;
  - la nature `ReinitialisationSecondFacteur` est inscrite au journal, en T5 ;
  - la double authentification rejoint les mesures de l'article 32.
- **`docker-compose.yml`** : le commentaire du volume `cles-api` ne parlait que des liens
  de réinitialisation de mot de passe.

---

# Étape 24 — Les comptes de l'entreprise

L'étape 23 a laissé un geste sans écran : la réinitialisation du second facteur d'un
collègue n'existe que par l'API. Elle n'est pas seule. **Aucun écran ne gère les comptes**,
et un administrateur qui prépare le pilote ne peut ni voir qui a accès, ni ouvrir un
compte, ni fermer celui d'une personne partie.

**Ce qui existe, relu le 6 novembre 2026**

- **`POST /api/auth/comptes`** crée un compte, administrateur seul, sans écran. Il vérifie
  le rôle, exige un `AgentId` pour le rôle `Agent` et un `ClientId` pour `ClientSyndic`,
  et cadre les deux à la main sur l'entreprise de l'appelant.
- **`POST /api/auth/comptes/{id}/anonymiser`** et
  **`POST /api/auth/comptes/{id}/reinitialiser-second-facteur`** existent, sans écran.
- ⚠️ **Aucune route ne liste les comptes.** `GET /api/agents/chefs-equipe` est la seule
  lecture de comptes, et elle ne rend que les chefs d'équipe, pour le rattachement.
- ⚠️ **Aucune route ne désactive un compte**, ni ne le réactive. La pseudonymisation le
  fait, mais elle est irréversible ; la fixture de tests le fait en base, faute d'endpoint.
  Une personne partie garde donc son accès tant qu'on ne l'efface pas.
- ⚠️ **À la création, c'est l'administrateur qui choisit le mot de passe du collègue**, et
  il le connaît. C'est exactement ce que `CLAUDE.md` §6 dit vouloir éviter à propos du
  changement de mot de passe. Rien n'oblige à le changer à la première connexion.
- `AspNetUsers` n'est pas filtrée (`CLAUDE.md` §7) : la liste filtre à la main sur
  `EntrepriseId`, et son test construit l'intrus d'une autre entreprise directement en base.

**Ce que je propose**

1. **`GET /api/auth/comptes`**, administrateur seul, journalisé en `Compte` / `Liste`.
   L'invariante de `JournalAccesTests` l'exigera d'elle-même, puisque le DTO portera `Nom`
   et `Email`. Les colonnes : nom, prénom, adresse, rôle, actif, double authentification
   active ou non, la fiche agent ou le client rattachés, la date de création, et la date de
   pseudonymisation s'il y en a une. ⚠️ **Pas de dernière connexion** : elle n'est pas
   stockée, se déduirait de `JetonsRafraichissement`, et serait une donnée de surveillance
   des collègues à justifier dans `RGPD.md` pour un usage que personne n'a demandé.
2. **Un écran « Comptes »**, administrateur seul, rangé dans la section *Entreprise* comme
   un écran propre (`ecrans` de `sections.ts`). Ce n'est pas un écran de référence : la
   création porte un mot de passe et des règles par rôle, et les gestes de ligne sont tous
   hors CRUD. Chaque ligne porte les gestes qui existent : réinitialiser la double
   authentification, pseudonymiser — ou, pour un compte rattaché à une fiche, le lien vers
   cette fiche, comme le dit déjà le refus 409 du serveur.
3. **La création, dans le même lot.** Une liste qui ne crée rien laisse l'administrateur
   dans Swagger pour la moitié du travail.

**À trancher avant le lot A**

| Question | Options | Penchant |
|---|---|---|
| Le mot de passe initial | l'administrateur le choisit, comme aujourd'hui ; le serveur en tire un, affiché une fois, et la première connexion impose de le changer ; une invitation par courriel | **Tiré par le serveur, changé à la première connexion.** L'administrateur ne choisit rien et ne peut rien réemployer ; ce qu'il transmet ne sert qu'une fois. La connexion gagne une étape `ChangementMotDePasse`, sur le même défi que le second facteur. ⚠️ Il faut une colonne, donc une **migration additive**, à présenter avant de la jouer. L'invitation par courriel est exclue comme chemin unique : le SMTP est optionnel (`CLAUDE.md` §2) |
| Désactiver et réactiver | dans ce lot ; plus tard ; jamais, la pseudonymisation suffit | **Dans ce lot.** Une personne partie doit perdre son accès le jour même, sans effacement irréversible : son compte attribue encore les lignes du journal, et une erreur d'identifiant doit se rattraper. Désactiver régénère l'empreinte et coupe les sessions, comme la pseudonymisation ; jamais sur soi-même, jamais le dernier administrateur actif |
| Changer le rôle d'un compte | dans ce lot ; plus tard | **Plus tard.** Un changement de rôle touche l'obligation du second facteur, le rattachement des agents à un chef et la frontière du syndic. C'est un lot à lui seul. En attendant : désactiver, puis créer |
| Les comptes pseudonymisés | affichés, marqués de leur date ; masqués | **Affichés en fin de liste, marqués.** Le registre doit pouvoir montrer qu'un effacement a eu lieu, et un compte masqué serait recréé par erreur sous la même adresse — ce qu'Identity refuserait d'ailleurs, sans que l'écran sache dire pourquoi |
| Pagination | paginée comme les listes de référence ; entière, bornée par l'effectif | **Entière**, comme les candidats et les chefs d'équipe : sa taille est bornée par l'effectif et les clients syndics, quelques centaines de lignes au plus. Un filtre par rôle côté écran suffit |

**Réponses obtenues le 6 novembre 2026** — les quatre penchants posés en question sont
retenus. Celui de la pagination n'a pas été posé : il est retenu par défaut, sauf avis
contraire.

| Question | Réponse | Ce que cela veut dire dans le code |
|---|---|---|
| Mot de passe initial | **tiré par le serveur, changé à la première connexion** | `CompteCreateDto` perd `MotDePasse`, la réponse le rend une fois ; une colonne marque le mot de passe provisoire, par une migration additive présentée avant d'être jouée ; la connexion rend 202 `{ etape: "ChangementMotDePasse" }` avant le second facteur |
| Désactiver et réactiver | **dans ce lot** | deux routes administrateur, journalisées ; désactiver régénère l'empreinte et coupe les sessions ; jamais sur soi, jamais le dernier administrateur actif |
| Changer le rôle | **plus tard** | aucune route ; l'écran ne propose pas de modifier le rôle |
| Comptes pseudonymisés | **affichés en fin de liste, marqués** | la liste les rend avec `DateAnonymisation`, l'écran les trie à la fin, sans geste de ligne |
| Pagination | **entière** (par défaut) | comme `chefs-equipe` : non paginée, triée par nom, un filtre par rôle côté écran |

**Conséquences à vérifier au cadrage**

- **`RGPD.md` devient faux à deux endroits** : T4 dit « aucune route ne liste les
  comptes », et le décompte des lectures journalisées change. `CLAUDE.md` §7 bis aussi :
  « onze lectures », et `chefs-equipe` décrite comme « la seule liste de comptes ».
- **Le test qui fige les actions journalisées** s'allonge de la liste, et de la
  désactivation si elle est journalisée. Je penche pour la journaliser, sur le précédent de
  la réinitialisation du second facteur : un geste d'administrateur sur le compte d'autrui.
- **L'étape `ChangementMotDePasse`** passe avant le second facteur à la connexion : un
  compte obligé équipe son téléphone **après** avoir choisi son mot de passe. Le
  `DEPLOIEMENT.md` §2.4 change aussi : le premier administrateur de l'amorçage passerait par
  là, ce qui rendrait inutile le conseil de changer le mot de passe initial à la main.
- **Hors de ce lot, et dit tel quel** : l'écran du journal d'accès, qui n'en a pas non plus.

**Lots, provisoires**

- **A** — le serveur : la liste, la désactivation et la réactivation, le mot de passe
  provisoire et son étape de connexion, la migration présentée avant d'être jouée, un
  sabotage par garde.
- **B** — l'écran : la liste, la création avec ses règles par rôle et le mot de passe
  affiché une fois, les gestes de ligne ; l'étape de changement de mot de passe à la
  connexion ; la logique de conversion extraite dans un module éprouvé.
- **C** — les documents : `RGPD.md`, `CLAUDE.md` §6 et §7 bis, `DEPLOIEMENT.md` §2.4.

---

# Suite — non planifié

Par ordre de valeur, à cadrer le moment venu.

~~**Double authentification** pour les rôles Administrateur et Responsable.~~ Cadrée à
l'étape 23.

**RLS PostgreSQL** en défense en profondeur : si un filtre global est oublié
sur une entité future, la base refuse quand même. Ceinture et bretelles sur le
seul invariant dont une erreur est catastrophique.

**Test d'intrusion externe** avant la première mise en production réelle.

~~**Génération automatique des interventions** depuis les contrats.~~ Cadrée et
en cours : voir l'étape 20.

**Écran agent mobile (PWA).** Une action par écran, cibles 56 px. Voir son
intervention → consignes → démarrer → checklist → anomalie avec photo →
terminer. Suppose les entités `Checklist` et `Anomalie`. Suppose aussi un
`GET /api/planning/moi` : le planning général est réservé aux rôles internes
parce que sa grille nomme tous les agents de l'entreprise, et le filtre
multi-tenant ne restreint pas cette colonne.

~~**Module stock.**~~ Cadré à l'étape 22. L'entité pivot `PrestationProduit` qui
figurait ici y devient une consommation **par type de prestation**, et les alertes
🟢🟠🔴 y perdent leur rouge, réservé au « non pourvu ».

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

**Module devis** sur le catalogue de prestations de l'étape 21, **sans IA** : des règles
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
| ~~`Stock.EmplacementId`~~ | ~~Sans clé étrangère~~ → **révisée à l'étape 22** : entité `Emplacement` et deux clés réelles, « exactement l'une des deux » | La décision d'origine supposait qu'aucun écran n'afficherait l'emplacement. Un dépôt qu'on ne peut pas nommer ne peut pas s'afficher, et un identifiant faux était accepté sans contrôle. Révisée quand la table était vide |
| Écriture inter-tenant | `EntrepriseId` forcé sans condition, retiré des `CreateDto` | Le mécanisme conditionnel laissait passer un identifiant forgé valide, donc l'écriture chez un concurrent |
| Pagination | `?page&taille` plafonnée, réponse enveloppée | Onze endpoints renvoyaient tout : épuisement de ressources, et forme à figer avant d'écrire les écrans |
| Base de test | PostgreSQL dédiée `klean_test` | Le fournisseur InMemory ne reproduit ni les filtres globaux, ni les contraintes, ni le refus des `DateTime` non datés — ce qu'on veut justement tester |
| Cadrage `ChefEquipe` | Par le verbe à l'étape 8, par le périmètre à l'étape 19 | Le modèle dépend d'une information métier — périmètre en personnes ou en bâtiments — qu'un client donne et qu'un raisonnement ne produit pas. Deviner, c'est risquer une migration sur une table peuplée |
| Périmètre du chef d'équipe | Des **personnes** : `Agent.ChefEquipeId` vers son compte, une équipe par agent. Une **frontière**, comme le syndic | Réponse du 28 octobre 2026. Le patron voit tout, chaque chef ne voit que son équipe : agents, absences, planning, candidats. Une vacation sans agent appartient à l'équipe du titulaire de sa prestation |
| `Absence.Motif` | Interdit sur `Maladie`, conservé sur les autres types | Le champ est indispensable pour `Autre` et utile pour `Indisponibilite` : le supprimer pousserait à écrire le motif ailleurs. Restreindre l'accès par rôle serait plus faible — ça conserve la donnée de santé en ajoutant une porte devant, alors que la minimisation dit de ne pas la collecter. En AGPL auto-hébergé, le produit doit être sûr par défaut : aucun DPO en aval ne relira le schéma |
| SMTP | Optionnel ; absent = 503, incomplet = échec au démarrage | `CLAUDE.md` §2 interdit d'imposer un service tiers. Mais une configuration à moitié faite est une erreur, et elle doit se voir au démarrage, pas au premier mot de passe oublié |
| Limitation de débit | Par adresse **et** par compte | Le verrouillage d'Identity est par compte : il ne ralentit pas d'un essai sur cent comptes, qui est la forme réelle du bourrage d'identifiants |
| Compteur de débit | En mémoire de processus | Un compteur partagé supposerait un service tiers. Limite assumée : derrière plusieurs instances, la limite est par instance |
| Tentative refusée | Non comptée | Sinon réessayer prolonge indéfiniment le blocage, et la limitation devient un verrouillage définitif |
| Mot de passe oublié | 202 quoi qu'il arrive | Distinguer ferait de l'endpoint un vérificateur d'adresses ouvert à tous |
| Règles de mot de passe | Validées par Identity seule, pas par `[MinLength]` | La validation de modèle passe avant le contrôleur et renvoie de l'anglais que le descripteur français ne peut pas atteindre ; et deux règles pour une contrainte finissent par diverger |
| Interfaces de service | `ITenantContext` et `IServiceCourriel`, pas d'autre | Substitution nécessaire, pas décorative. Une classe concrète qui « n'envoie pas vraiment » ferait dépendre la production d'un réglage |
| Jeton d'accès | 15 min, cookie `HttpOnly` `Secure` `SameSite=Strict` | Le `localStorage` exposait un porteur de 8 h à la première XSS. La durée courte borne la fenêtre si le contrôle de révocation était un jour contourné |
| En-tête `Authorization` | Plus accepté | Aucun jeton n'est remis au client, donc personne ne peut s'en servir ; un second chemin d'authentification est un second chemin à auditer |
| Révocation | `Actif` et `SecurityStamp` à chaque requête | Une lecture par clé primaire contre une révocation immédiate. Sans elle, désactiver un compte le laissait actif jusqu'à l'expiration du jeton |
| Jeton de rafraîchissement | Opaque, persisté par empreinte SHA-256 | Révocable, contrairement à un JWT signé. Une fuite de la table ne permet pas de rejouer les sessions |
| Rejeu d'un jeton consommé | Coupe toutes les sessions du compte | Sans cette réaction, la rotation ne protège de rien : un attaquant qui a copié un jeton s'en sert en parallèle du porteur légitime |
| Durée de session | 14 jours absolus, non prolongés par la rotation | Une échéance repoussée à chaque usage ne finit jamais |
| CSRF | Double soumission, sur les écritures portant un cookie de session | `SameSite=Strict` bloque l'essentiel mais dépend du navigateur. Inutile sans cookie : rien à détourner |
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
