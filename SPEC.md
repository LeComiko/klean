# SPEC.md — KLEAN

Feuille de route. Douze étapes, dans l'ordre. Chacune se termine par un build
vert, une validation fonctionnelle et un commit.

Les conventions, la pile et les pièges sont dans `CLAUDE.md`.

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
- `EntrepriseId` (Guid, requis) sur les onze entités dès le départ, **sans**
  clé étrangère sur les sept qui ne l'ont pas naturellement
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

# Étape 6 — Protection et multi-tenant

**Périmètre**
- `[Authorize]` sur les onze contrôleurs métier (pas sur `AuthController`)
- `Services/ITenantContext.cs` et `TenantContext.cs`
- Filtres globaux dans `OnModelCreating` — voir le piège du cache de modèle
- `SaveChangesAsync` surchargé : affectation automatique d'`EntrepriseId`
- `AmorcageService` appelle `DesactiverFiltrage()` en premier

**Validation** — deux tests, les deux obligatoires :
1. Un administrateur ne voit **aucune** donnée d'une autre entreprise.
2. Un POST avec `entrepriseId` à `00000000-...` crée l'enregistrement avec
   l'`EntrepriseId` réel du token, pas celui envoyé.

---

# Étape 7 — Moteur d'affectation

**Périmètre**
- `Services/AffectationService.cs` : filtres durs puis score pondéré
  (voir `CLAUDE.md` §8)
- `DTOs/CandidatDto.cs` : les trois notes **et** les données brutes
- `GET /api/interventions/{id}/candidats`
- `Program.cs` : `AddScoped<AffectationService>()`

**Validation** — avec le jeu de données de l'étape 11 : un agent proche et
expérimenté sort en tête ; un agent en absence n'apparaît pas du tout.

---

# Étape 8 — Cycle de remplacement

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

# Étape 9 — Endpoint planning

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

# Étape 10 — Frontend, socle

**Périmètre**
- `src/styles/tokens.css` : toutes les variables de `CLAUDE.md` §12,
  thème clair et sombre, reset minimal
- Google Fonts : Figtree et IBM Plex Mono dans `index.html`
- `src/api/client.ts` : wrapper fetch typé, token depuis `localStorage`
  (clé `klean_token`), purge et redirection sur 401
- `src/auth/` : `AuthContext` avec restauration de session, `RouteProtegee`
- `src/pages/Connexion.tsx`
- `src/layout/Navigation.tsx` : barre latérale 64 px, pastilles K, PL, TB,
  SI, AG, ST, déconnexion
- `src/layout/Disposition.tsx` et les routes dans `App.tsx`
- `src/composants/EnTetePage.tsx`

**Validation** : connexion, persistance après F5, navigation entre sections,
contraste suffisant sur les pastilles inactives.

---

# Étape 11 — Écran de planning

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

# Étape 12 — Panneau de remplacement

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

# Suite — non planifié

Par ordre de valeur, à cadrer le moment venu.

**Restrictions par rôle.** Le Client/Syndic ne voit que les sites de son
client, en lecture seule. Priorité haute : seul accès externe.

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
| Token | localStorage, 8 h, sans refresh | Compromis pour un outil interne ; à durcir avant exposition publique |
| Couleur de marque | Ardoise `#3F4A54` | Le secteur est saturé de bleu `#2563EB` |
| Blocs de planning | Une seule couleur | Une couleur par agent devient illisible au-delà de dix agents et brûle la palette |
| Compétences | Hors V1 du moteur | L'entité n'existe pas ; ne pas bloquer le moteur dessus |
| Remplacement automatique | Non implémenté | Sans notifications ni application agent, il n'y a personne à contacter |
| `Stock.EmplacementId` | Sans clé étrangère | Peut pointer vers un dépôt, un véhicule ou un site |
| Linter frontend | oxlint, pas ESLint | Couvre `exhaustive-deps` et `jsx-a11y`, les deux familles utiles ici ; une devDep au lieu de six, cohérent avec la discipline de dépendances. Limite acceptée : pas de règle personnalisée en JavaScript |
