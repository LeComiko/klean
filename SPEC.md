# SPEC.md — KLEAN

Feuille de route. Dix-neuf étapes, dans l'ordre. Chacune se termine par un
build vert, une validation fonctionnelle et un commit.

Les conventions, la pile et les pièges sont dans `CLAUDE.md`.

> **Renumérotation.** La cible a changé : d'outil interne, KLEAN devient un
> produit mis à disposition d'entreprises tierces. Les étapes 1 à 5 sont
> livrées et inchangées. L'étape 6 est réécrite — elle corrigeait une faille,
> voir son avertissement. Trois étapes de solidité sont insérées après elle, et
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
Les filtres de l'étape 6 séparent les entreprises entre elles ; ils ne séparent
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

# Étape 6 — Isolation multi-tenant

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

**Validation** — trois tests, les trois obligatoires :
1. Un administrateur ne voit **aucune** donnée d'une autre entreprise.
2. Un POST sans `entrepriseId` crée l'enregistrement avec celui du jeton.
3. Un POST portant l'`EntrepriseId` **réel** d'une autre entreprise ne crée
   rien dans cette entreprise. C'est le cas que le test d'origine ratait.

---

# Étape 7 — Socle de tests et intégration continue

Les critères de validation des étapes suivantes sont déjà rédigés comme des cas
de test — « détection 1 remplacement, idempotence 0 au second appel, accepter
sur `Recherche` → 400 ». Les exécuter à la main ne protège de rien : la
vérification disparaît avec le terminal. Cette étape existe pour que l'étape 11
ne puisse pas casser l'isolation de l'étape 6 en silence.

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
- Les trois tests de l'étape 6 convertis, en premier.
- Intégration continue : `dotnet build`, `dotnet test`, `npm run build`,
  `npm run lint` sur chaque poussée. Une suite que personne n'exécute ne vaut
  rien.

**Validation** : `dotnet test` vert, les trois tests d'isolation passent, et
un quatrième — retirer un filtre global — **échoue**. Le harnais doit prouver
qu'il détecte, pas seulement qu'il tourne.

---

# Étape 8 — Autorisations, périmètres et pagination

Le tenant isole les entreprises entre elles. Il n'isole rien **à l'intérieur**
d'une entreprise, et `ClientSyndic` est le seul accès externe du système. Une
seule passe sur les onze contrôleurs, puisque autorisations et pagination
touchent les mêmes fichiers.

**Périmètre**
- Matrice des droits — une ligne par rôle, une colonne par ressource, verbe par
  verbe — écrite **dans ce fichier** avant d'écrire une ligne de code
- `[Authorize(Roles = ...)]` par endpoint, conformément à la matrice
- **Second axe d'isolation** : un compte `ClientSyndic` ne voit que les sites de
  son `ClientId`, en lecture seule. Filtre appliqué côté requête, jamais en
  confiance dans le contrôleur.
- `PUT /api/remplacements/{id}` : interdire la modification de `Statut`. Le
  CRUD brut de l'étape 4 laisse forcer `Accepte` et contourne la machine à
  états de l'étape 10.
- Pagination sur les onze `Lister()` : `?page=1&taille=50`, plafond de taille,
  réponse enveloppée `{ elements, total, page, taille }`. Sans plafond,
  `GET /api/interventions` est un vecteur d'épuisement de ressources.

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

**Validation** — avec le jeu de données de l'étape 11 : un agent proche et
expérimenté sort en tête ; un agent en absence n'apparaît pas du tout.

---

# Étape 10 — Cycle de remplacement

**Périmètre**
- `Services/RemplacementService.cs` : `DetecterImpactsAsync`,
  `ProposerAsync`, `AccepterAsync`, `RefuserAsync`
- `DTOs/RemplacementActionDto.cs` : `ProposerDto { Guid AgentId }`
- `AbsencesController` : détection à la création + endpoint de rattrapage
  `POST /api/absences/{id}/detecter-impacts`
- `RemplacementsController` : `candidats`, `proposer`, `accepter`, `refuser`

**Validation** — scénario complet, chaque point vérifié :
détection (1 remplacement) · idempotence (0 au second appel) · candidats
classés · `accepter` sur statut `Recherche` → **400** · proposition →
`Propose` avec score · refus → retour `Recherche` **sans réaffecter
l'intervention** · proposition puis acceptation → l'intervention **change
réellement d'agent** · proposition après acceptation → 400 · id inexistant
→ **404**.

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

**Validation** : avec le jeu de test, 4 agents, 17 vacations, 34 heures,
3 non pourvues.

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
terminer. Suppose les entités `Checklist` et `Anomalie`.

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
| Linter frontend | oxlint, pas ESLint | Couvre `exhaustive-deps` et `jsx-a11y`, les deux familles utiles ici ; une devDep au lieu de six, cohérent avec la discipline de dépendances. Limite acceptée : pas de règle personnalisée en JavaScript |
