# CLAUDE.md — KLEAN

Manuel opératoire du dépôt. Claude Code lit ce fichier au début de chaque
session. Il décrit le produit, la pile imposée, les conventions à respecter
et les pièges connus.

Le dépôt part de zéro. La feuille de route est dans `SPEC.md`.

---

## 1. Le produit

KLEAN est un logiciel libre de gestion opérationnelle pour les entreprises de
propreté — nettoyage de bureaux et de copropriétés. Cible : 5 à 150 agents,
plusieurs dizaines de sites.

Quatre fonctions au cœur du produit :

1. Planning hebdomadaire des agents
2. Remplacement d'un agent absent, avec candidats classés par score
3. Gestion opérationnelle des stocks (dépôt, véhicule, site)
4. Certification des passages, comme preuve pour le client

Le parcours critique, dans l'ordre de priorité :
`Contrat → Intervention → Planning → Absence → Remplacement → Agent →
Certification → Stock → Rapport`

Ce qui est volontairement **hors périmètre** : comptabilité, paie, CRM
commercial, ERP financier, facturation complexe.

Le principe fondamental : **toutes les données sont connectées**. Une absence
déclenche une recherche de remplaçant, qui modifie le planning, qui change
les besoins en produits. C'est cette interconnexion qui distingue KLEAN d'un
empilement de modules.

---

## 2. Licence et positionnement

**AGPL v3.** Choix délibéré : l'outil doit rester gratuit et disponible pour
les entreprises, et quiconque l'exploite en service doit publier ses
modifications. Ce n'est pas MIT : la permissivité maximale permettrait à un
éditeur de revendre KLEAN sans rien reverser, ce qui contredirait
l'intention.

Conséquences pratiques sur le code :

- Aucun secret ne doit jamais entrer dans le dépôt. Tout passe par les
  *user secrets* .NET en développement, par variables d'environnement en
  production.
- `appsettings.example.json` est le modèle versionné, avec des valeurs de
  remplacement explicites. `appsettings.json` ne contient aucune chaîne de
  connexion ni clé.
- Le `README.md` doit permettre à un inconnu de faire tourner le projet en
  suivant les commandes telles quelles.
- Pas de dépendance propriétaire, pas de service tiers obligatoire.

---

## 3. Pile technique imposée

| Couche | Choix | Notes |
|---|---|---|
| Backend | ASP.NET Core, .NET 10, C# | `backend/Klean.Api.csproj` |
| ORM | Entity Framework Core 10 | migrations dans `backend/Migrations/` |
| Base | PostgreSQL 16 ou plus | base `klean`, port 5432 |
| Auth | ASP.NET Core Identity + JWT | 5 rôles, multi-tenant par `EntrepriseId` |
| Frontend | React 19 + TypeScript + Vite | `frontend/`, CSS écrit à la main |
| Routage | react-router-dom | seule dépendance frontend autorisée |

**Aucune bibliothèque de composants**, pas de Tailwind, pas de gestionnaire
d'état, pas de date-fns. Le CSS utilise les variables de
`frontend/src/styles/tokens.css`. Les utilitaires de date sont écrits à la
main dans `frontend/src/utils/dates.ts`.

Toute dépendance supplémentaire doit être proposée et justifiée avant
installation.

---

## 4. Arborescence cible

```
KLEAN/
├── CLAUDE.md, SPEC.md, README.md, LICENSE
├── backend/
│   ├── Models/          11 entités métier + Utilisateur + RolesKlean
│   ├── Data/            KleanDbContext (filtres multi-tenant)
│   ├── DTOs/            un fichier par entité, XxxDto + XxxCreateDto
│   ├── Controllers/     un par entité + Auth + Planning
│   ├── Services/        AffectationService, RemplacementService,
│   │                    PlanningService, JwtService, AmorcageService,
│   │                    TenantContext
│   ├── Migrations/
│   └── Scripts/         SQL de maintenance
└── frontend/src/
    ├── api/             wrappers fetch typés
    ├── auth/            AuthContext, RouteProtegee
    ├── composants/      un .tsx + un .css par composant
    ├── layout/          Navigation, Disposition
    ├── pages/           un fichier par écran
    ├── styles/          tokens.css
    ├── types/           interfaces TypeScript
    └── utils/           dates.ts
```

---

## 5. Modèle de données

Onze entités métier, toutes avec un `Id` de type `Guid` généré côté
application et une colonne `EntrepriseId` (dénormalisée, voir §7).

```
Entreprise ─┬─ Client ─── Site ─── Contrat ─── Prestation ─── Intervention
            ├─ Agent ─── Absence                                    │
            └─ Produit ─── Stock                    Remplacement ───┘
```

| Entité | Champs notables |
|---|---|
| Entreprise | RaisonSociale, Siret, DateCreation |
| Client | Type (enum `TypeClient`), coordonnées |
| Site | Latitude, Longitude, Consignes |
| Contrat | DateDebut, DateFin (nullable), Frequence, Actif |
| Prestation | DureeEstimeeMinutes |
| Agent | Latitude, Longitude (nullable), DateEmbauche, Actif |
| Intervention | DatePrevue, HeurePrevue (TimeSpan), AgentId (nullable), Statut (enum) |
| Absence | Type (enum), DateDebut, DateFin, Motif |
| Remplacement | Statut (enum), Score (nullable), AgentRemplacantId (nullable) |
| Produit | Unite, SeuilAlerte |
| Stock | TypeEmplacement (enum), EmplacementId (sans clé étrangère, voulu) |

Enums :
- `TypeClient` : EntrepriseCliente, Syndic, GestionnaireImmobilier,
  Copropriete, Autre
- `StatutIntervention` : Planifiee, EnCours, Terminee, NonCertifiee, Annulee
- `TypeAbsence` : Maladie, Conge, Indisponibilite, Retard, Autre
- `StatutRemplacement` : Recherche, Propose, Accepte, Refuse, Annule
- `TypeEmplacement` : Depot, Vehicule, Site

Tous les enums sont stockés **en texte** via `HasConversion<string>()`.
Les DTOs les exposent en `string` et utilisent `Enum.TryParse` avec un
`BadRequest` explicite si la valeur est invalide.

---

## 6. Authentification

- Identity avec `Utilisateur : IdentityUser<Guid>`, portant `EntrepriseId`
  (obligatoire), `AgentId` et `ClientId` (nullables selon le rôle).
- JWT signé HMAC-SHA256, 8 heures, pas de *refresh token*.
- Le token porte la revendication **`entrepriseId`** : c'est elle qui
  alimente le multi-tenant.
- Cinq rôles dans une classe statique `RolesKlean` : Administrateur,
  Responsable, ChefEquipe, Agent, ClientSyndic.
- Amorçage au démarrage : crée les rôles, puis une entreprise et un compte
  administrateur si aucun utilisateur n'existe. Lit
  `Amorcage:EmailAdmin`, `Amorcage:MotDePasseAdmin`,
  `Amorcage:RaisonSocialeEntreprise`.
- Règles de mot de passe : 10 caractères minimum, majuscule, minuscule,
  chiffre. Pas de caractère spécial exigé. Verrouillage après 5 échecs
  pendant 15 minutes.
- `UserName` doit être renseigné avec la même valeur que `Email` à chaque
  création de compte, sinon Identity refuse.

Tous les contrôleurs métier portent `[Authorize]`. Les restrictions par rôle
viennent plus tard dans la feuille de route.

---

## 7. Multi-tenant

Mécanisme en trois parties, toutes nécessaires :

1. **`ITenantContext` / `TenantContext`** lit la revendication `entrepriseId`
   du token via `IHttpContextAccessor`. Expose aussi `DesactiverFiltrage()`,
   utilisé par l'amorçage qui doit créer la première entreprise avant
   qu'aucun tenant n'existe.

2. **Filtres globaux EF Core**, **onze** au total :
   ```csharp
   modelBuilder.Entity<X>().HasQueryFilter(e =>
       !TenantFiltrageActif || TenantEntrepriseId == null
       || e.EntrepriseId == TenantEntrepriseId);
   ```
   où `TenantFiltrageActif` et `TenantEntrepriseId` sont des **propriétés
   privées d'instance du DbContext**. Voir le piège §10.

   Dix filtres sur les entités portant `EntrepriseId`, **plus un sur
   `Entreprise` elle-même**, sur son propre `Id` :
   ```csharp
   modelBuilder.Entity<Entreprise>().HasQueryFilter(e =>
       !TenantFiltrageActif || TenantEntrepriseId == null
       || e.Id == TenantEntrepriseId);
   ```
   ⚠️ Elle ne porte pas de colonne `EntrepriseId`, son identifiant **est** le
   tenant — d'où l'oubli facile. Sans ce filtre, `GET /api/entreprises` expose
   la raison sociale et le SIRET de toutes les entreprises de l'instance.

   ⚠️ **`AspNetUsers` n'est pas filtrée**, et ne doit pas l'être : Identity
   interroge les comptes hors de toute requête authentifiée — la connexion, par
   définition — et sa validation d'unicité d'adresse repose sur des requêtes non
   filtrées. Conséquence : tout endpoint listant ou cherchant des comptes doit
   filtrer à la main sur `EntrepriseId`.

   L'échappatoire `TenantEntrepriseId == null` n'est sûre **que parce que** les
   onze contrôleurs métier portent `[Authorize]`. Retirer cet attribut sur un
   seul d'entre eux ouvrirait l'accès anonyme à toutes les entreprises.

3. **`SaveChangesAsync` surchargé** : **force** `EntrepriseId` à la valeur du
   jeton sur toute entité à l'état `Added`, **sans condition**. Et
   `EntrepriseId` ne figure dans aucun `XxxCreateDto` : sa seule source
   légitime est le jeton.

   Le retrait d'`EntrepriseId` des `CreateDto` est la garde **principale** : le
   liant ignore une propriété qui n'existe pas. Le forçage inconditionnel en est
   la redondance, pour le cas où une valeur atteindrait l'entité autrement. Les
   deux sont testées séparément.

   ⚠️ Une version antérieure de ce paragraphe n'écrasait la valeur que si elle
   valait `Guid.Empty`, et en concluait qu'un utilisateur ne pouvait pas créer
   de données pour une autre entreprise même en forgeant la requête. La
   conclusion ne découlait pas du mécanisme : un `POST` portant l'identifiant
   **réel** d'une autre entreprise n'était pas écrasé, et la ligne était créée
   dans le tenant de cette entreprise. Les filtres de lecture empêchaient de la
   relire ; l'écriture inter-tenant passait. Ne jamais conditionner cette
   affectation.

`EntrepriseId` est **dénormalisé** sur les sept entités qui ne l'ont pas
naturellement (Site, Contrat, Prestation, Intervention, Absence,
Remplacement, Stock). Choix assumé : évite trois jointures par ligne sur la
vue planning. La cohérence est garantie applicativement, sans clé étrangère.

---

## 8. Moteur d'affectation

`AffectationService.GetCandidatsAsync(interventionId)` retourne les agents
candidats classés par score décroissant.

**Filtres durs** (l'agent n'apparaît pas du tout) :
- `Actif == false`
- une absence couvre le **jour** de l'intervention. Comparaison au jour et non
  à la minute : une absence saisie « du 7 au 9 » arrive avec les heures à zéro,
  et un chevauchement strict laisserait l'agent candidat le 9 à 8 h 30.
- l'agent est déjà affecté à une intervention chevauchante (statuts
  `Planifiee` ou `EnCours` uniquement)
- repos légal : une intervention d'un **autre jour** se termine moins de 11 h
  avant, ou commence moins de 11 h après. Tous les statuts comptent sauf
  `Annulee` : une vacation terminée hier soir a bel et bien été travaillée.

**Score**, moyenne pondérée de trois notes sur 100 :

| Critère | Poids | Normalisation |
|---|---|---|
| Expérience du site | 35 | `min(passages, 10) / 10 × 100` |
| Distance | 30 | `max(0, (30 − km) / 30 × 100)`, Haversine R=6371 km, 50 si coordonnées absentes |
| Charge de la semaine | 35 | `max(0, (35 − heures) / 35 × 100)` |

Le `CandidatDto` expose les trois notes **et** les données brutes (km,
passages, heures) : le responsable doit comprendre le classement, pas le
subir.

Les compétences sont volontairement absentes de la V1 — l'entité dédiée
n'existe pas.

**Expérience du site** ne compte que les passages **antérieurs** à
l'intervention, tous statuts sauf `Annulee` : un agent déjà planifié trois fois
la semaine prochaine ne connaît pas encore le site, et compter ces passages
reviendrait à récompenser le fait d'être déjà planifié.

**Charge de la semaine** se calcule du lundi au dimanche, sur les durées
estimées des prestations, tous statuts sauf `Annulee`.

⚠️ **Correction d'une faille de la version précédente de cette section.** Le
repos de 11 h y était écrit sans restriction de jour. Appliqué tel quel, il
interdit à un agent d'enchaîner deux sites dans la même matinée : une vacation
à 11 h tombe à 30 minutes de la fin de celle de 8 h 30, donc à moins de 11 h,
donc l'agent disparaît de la liste. Sur un planning réellement rempli la liste
des candidats revenait quasiment toujours vide — le symptôme que cette section
annonçait, dont la cause était la règle elle-même et non le jeu de données. Le
repos quotidien de l'article L3131-1 s'intercale entre deux **journées** de
travail, pas entre deux tâches d'une même journée : deux vacations du même jour
sont exemptées, seul le chevauchement les sépare. Le test
`Agent_affecte_le_meme_jour_hors_chevauchement_reste_candidat` garde cette
exemption ; sans elle, il échoue.

⚠️ La liste des candidats n'est **pas paginée**, contrairement aux onze listes
de l'étape 8 : sa taille est bornée par le nombre d'agents actifs de
l'entreprise, pas par la requête, et tronquer un classement le rend faux.

**Les filtres durs gardent aussi les écritures.** Le moteur ne sert pas
seulement à proposer : `EstEligibleAsync(agentId, creneau, interventionAExclure)`
est appelé par `POST` et `PUT /api/interventions` dès que la requête porte un
`agentId`, et refuse en 400. Trois conséquences à connaître :

1. Le moteur raisonne sur un **`Creneau`** — site, coordonnées, jour, heure,
   durée — et non sur un identifiant d'intervention. Sans cela le `POST` serait
   hors de portée : à l'instant où l'on juge l'agent, la vacation n'a pas encore
   d'identifiant.
2. `interventionAExclure` est indispensable sur le `PUT` : sans elle,
   l'intervention se compte elle-même comme occupation et son propre titulaire
   est exclu par chevauchement avec sa propre vacation.
3. La vérification porte sur l'horaire **demandé**, et s'applique à chaque
   écriture portant un agent — pas seulement quand l'agent change. Déplacer une
   vacation en gardant le même agent peut tout autant la rendre illégale.
   Conséquence assumée : une ligne déjà illégale ne peut pas être réenregistrée
   telle quelle ; il faut d'abord la libérer en envoyant `agentId` à null.

Pas de drapeau de forçage. Une échappatoire qui existe devient le chemin normal
dès la première fois qu'elle dépanne.

---

## 9. Cycle de remplacement

Machine à états stricte, gardée côté service :

```
Recherche ──proposer──► Propose ──accepter──► Accepte
    ▲                      │
    └──────refuser─────────┘
Refuse ──proposer──► Propose
```

- `DetecterImpactsAsync(absenceId)` crée un `Remplacement` en statut
  `Recherche` pour chaque intervention `Planifiee` de l'agent absent dont le
  **jour** tombe dans l'absence. Comparaison au jour, comme les filtres durs du
  §8. Idempotent : ne recrée pas si un remplacement `Recherche`, `Propose` ou
  `Accepte` existe déjà sur la même intervention. Appelée automatiquement à la
  création d'une absence, et rejouable par
  `POST /api/absences/{id}/detecter-impacts`.
- `ProposerAsync` **refuse** un agent écarté par un filtre dur, au lieu de le
  proposer avec un score nul. Sans cette garde les filtres durs seraient
  décoratifs : il suffirait de proposer à la main l'agent en arrêt maladie. Le
  score est celui que le moteur calcule à cet instant, jamais celui que le
  client envoie.
- `AccepterAsync` **revérifie** l'éligibilité avant d'écrire : entre la
  proposition et l'acceptation, une absence a pu être saisie.
- Un refus ne réaffecte rien et ne déclenche aucune cascade : chaque
  proposition est une action humaine explicite. Il ramène en `Recherche` et
  efface l'agent — le service ne produit donc jamais le statut `Refuse`, mais
  l'accepte en entrée puisque le CRUD brut peut l'avoir posé. Limite assumée de
  la V1 : sans table d'historique, un refus ne laisse pas de trace et rien
  n'empêche de reproposer aussitôt le même agent.
- Toute transition invalide retourne 400 avec un message explicite ; une
  ressource inexistante retourne 404. La distinction doit être testée.

⚠️ **Correction d'une affirmation fausse de la version précédente de cette
section.** Elle indiquait qu'`AccepterAsync` était « **le seul endroit du code**
où une intervention change d'agent ». C'est faux : `PUT /api/interventions/{id}`
écrit `AgentId` après avoir seulement vérifié que l'agent existe. Vérifié
empiriquement — un agent en arrêt maladie ce jour-là, absent de la liste des
candidats, est accepté par le `PUT` avec un `204`. Les filtres durs du moteur
d'affectation sont donc contournables par une requête légitime d'un
`Responsable`. `AccepterAsync` est le seul endroit **du cycle de remplacement**
qui change l'agent ; ce n'est pas le seul du code.

**Corrigé à l'étape 10 bis**, sans geler `AgentId` : la réaffectation directe
reste un geste normal d'exploitation — « Marie prend le site de Paul mardi »,
sans absence de Paul — mais elle passe désormais par les mêmes filtres durs. La
garantie n'est plus « un seul chemin écrit l'agent », elle est **« aucun chemin
n'affecte un agent inéligible »**, ce qui est à la fois plus vrai et plus utile.
Voir §8, *les filtres durs gardent aussi les écritures*.

Pas de mode « remplacement automatique » : sans notifications ni application
agent, il n'y a personne à contacter.

---

## 10. Pièges connus

À lire avant d'écrire du code. Chacun a déjà coûté du temps sur ce projet.

**DateTime et PostgreSQL.** Toutes les colonnes de date sont en
`timestamp with time zone`. Npgsql refuse tout `DateTime` dont le `Kind` est
`Unspecified`. Chaque `DateTime` envoyé à une requête EF Core doit être
construit ainsi :
```csharp
DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc)
```

**Filtres globaux et cache du modèle.** `OnModelCreating` ne s'exécute
**qu'une fois** par type de DbContext, puis le modèle est mis en cache. Un
filtre qui capture une variable locale fige l'instance du premier DbContext
créé — celui de l'amorçage, sur lequel le filtrage est désactivé. Résultat :
le filtre laisse tout passer, silencieusement. Les filtres doivent traverser
des propriétés d'instance du DbContext.

**Identity impose son schéma cookie.** `AddIdentity` enregistre des schémas
par défaut qui redirigent vers `/Account/Login` au lieu de retourner 401.
`AddAuthentication` doit fixer explicitement les trois propriétés
`DefaultAuthenticateScheme`, `DefaultChallengeScheme` et `DefaultScheme` sur
`JwtBearerDefaults.AuthenticationScheme`.

**`RequireUniqueEmail`** appartient à `IdentityOptions.User`, pas à
`IdentityOptions.SignIn`, et vaut `false` par défaut.

**Swagger et OpenAPI 2.x.** `AddSecurityRequirement` a changé de signature ;
la documentation en ligne est périmée. `AddSecurityDefinition` seul suffit
pour faire apparaître le bouton Authorize. Ne pas s'acharner dessus : c'est
du confort de test, pas une fonctionnalité.

**Exécutable verrouillé.** `dotnet build` échoue avec `MSB3027` si l'API
tourne encore. Préférer `dotnet watch run` pendant le développement.

**`WebApplicationFactory` et le modèle d'hébergement minimal.** Les
instructions de plus haut niveau de `Program.cs` s'exécutent — donc lisent
`builder.Configuration` — **avant** que les délégués `ConfigureAppConfiguration`
de la fabrique ne s'appliquent. Écraser une chaîne de connexion par
`AddInMemoryCollection` arrive donc trop tard, échoue **en silence**, et la
série de tests vise la base de développement, qu'elle vide entre chaque test.
Les valeurs de test doivent passer par des **variables d'environnement**, posées
avant la construction de l'hôte. Et la fabrique doit vérifier la base
effectivement atteinte avant la première écriture : c'est le seul endroit du
projet où une erreur de configuration détruit des données.

**`Microsoft.OpenApi` 2.0 a supprimé le namespace `.Models`.**
`OpenApiSecurityScheme`, `SecuritySchemeType` et `ParameterLocation` sont
désormais directement dans `Microsoft.OpenApi`. Un `using
Microsoft.OpenApi.Models;` donne un CS0234 qui ressemble à une référence
d'assembly manquante alors que le paquet est bien là.

**`ValueGeneratedNever` et les clés d'Identity.** La boucle du `DbContext` qui
force la génération des `Guid` côté application ne doit **jamais** attraper les
entités d'Identity, `Utilisateur` compris : ni `UserManager` ni `RoleManager`
ne posent l'`Id`, c'est le générateur d'EF qui le fait. Les basculer en `Never`
leur donne toutes `Guid.Empty`, donc une collision de clé primaire au deuxième
enregistrement créé — et l'amorçage échoue sur le deuxième rôle. Filtrer la
boucle sur le namespace des entités métier, et exclure `Utilisateur`.

**`dotnet-ef` et Homebrew.** L'outil global s'installe sans erreur mais
refuse de démarrer : Homebrew place le runtime dans son Cellar, l'apphost le
cherche à l'emplacement standard, et le message parle d'un runtime manquant
alors qu'il est présent. Exporter `DOTNET_ROOT` avant toute commande `ef` :
```bash
export DOTNET_ROOT=/opt/homebrew/opt/dotnet/libexec
```

**Encodage des scripts SQL.** Sous PowerShell, `Set-Content -Encoding UTF8`
ajoute un BOM que `psql` ne comprend pas. Utiliser `-Encoding ASCII`, et
éviter les emojis dans les commentaires SQL.

---

## 11. Conventions de code

**Nommage en français** pour tout le domaine métier : entités, propriétés,
DTOs, services, composants React, classes CSS, noms de fichiers. Les
mots-clés techniques restent en anglais (`async`, `Task`, `useState`).

**Backend**
- Un DTO par entité, dans un seul fichier : `XxxDto` (lecture, avec `Id`) et
  `XxxCreateDto` (création et mise à jour, sans `Id`).
- Les DTOs n'exposent **jamais** les propriétés de navigation — uniquement
  les champs scalaires et les clés étrangères.
- Contrôleurs : `[ApiController]`, `[Route("api/xxx")]`, `[Authorize]`,
  injection du `KleanDbContext` par constructeur, cinq endpoints CRUD.
- Toutes les méthodes `async`, `AsNoTracking()` sur les lectures pures.
- Services : classes concrètes, **pas d'interface**, pas de repository, pas
  d'AutoMapper, pas de logging ajouté.
- Ne jamais contourner les filtres multi-tenant avec `IgnoreQueryFilters()`.

**Frontend**
- Un `.tsx` et un `.css` par composant, côte à côte.
- `import type` pour les types (projet en `verbatimModuleSyntax`).
- Aucune couleur en dur : uniquement les variables de `tokens.css`.
- Trois états explicites sur tout appel réseau : chargement, erreur, données.
- Le wrapper `src/api/client.ts` ajoute le token et purge la session sur 401.
- Les composants de présentation reçoivent leurs données en props ; c'est la
  page qui orchestre les appels API.

---

## 12. Système d'interface

Un seul système de couleur et de typographie, deux densités : desktop
exploitation (dense, clavier) et mobile agent (une action par écran, cibles
56 px). Les valeurs ci-dessous vont dans
`frontend/src/styles/tokens.css` en variables CSS.

**Couleurs.** La marque est une **ardoise chaude `#3F4A54`**, pas un bleu :
elle remplit des dizaines de blocs de planning sans fatiguer, et laisse la
seule couleur saturée du système — la **terre cuite `#9C4221`** — au signal
« non pourvu ». Aucun bleu `#2563EB`, aucune couleur par agent.

| Rôle | Clair | Sombre |
|---|---|---|
| brand | `#3F4A54` | `#5E6E7A` |
| brand-strong | `#2C353D` | `#49555F` |
| brand-tint | `#E8EBEE` | `#1E252A` |
| alerte / focus | `#9C4221` | `#E07A55` |
| retard | `#8A5A00` | `#D9A33A` |
| realise | `#2F6B45` | `#6BB283` |
| canvas | `#F6F5F2` | `#101110` |
| surface | `#FFFFFF` | `#191A18` |
| surface-2 | `#FBFAF8` | `#212320` |
| border | `#E3E0D9` | `#2E302B` |
| border-fort | `#CFCBC2` | `#454841` |
| texte | `#1B1A17` | `#EDECE7` |
| texte-2 | `#6E6B64` | `#A3A099` |
| texte-3 | `#8A867E` | `#7C7A73` |
| hachure | `#D8D4CC` | — |

**Typographie.** Figtree pour l'interface, IBM Plex Mono pour **tout chiffre
comparé en colonne** — heures, km, scores, quantités. La chasse fixe aligne
verticalement sans effort. Chargées depuis Google Fonts.

Échelle (taille / interligne / graisse) : 32/36/700 titre d'écran ·
20/26/600 titre de section · 16/24/500 corps mobile · 14/20/400 corps
desktop · 13/18/500 ligne de tableau · 12/16/500 bloc de planning ·
11/14/600 étiquette.

Plancher mobile : 16 px pour le corps, 22 px pour l'information principale.
Jamais 11–12 px sur mobile.

**Formes.** Espacement base 4 (4, 8, 12, 16, 24, 32, 48). Rayons : 4 badge,
6 bloc et champ, 10 carte, 999 pilule. Lignes desktop : 34 px compacte,
44 px confortable. Cible tactile mobile 56 px, bouton primaire 64 px.
Carte : fond surface, bordure 1 px, rayon 10, padding 24.

**Règles non négociables**
- **Aucune ombre portée.** La hiérarchie passe par la bordure et le fond.
- **La couleur ne porte jamais seule une information.** Chaque statut =
  pastille + mot. Le trait pointillé et le motif hachuré sont les deux
  redondances non colorées.
- **Focus clavier jamais supprimé** : contour 2 px `#9C4221`, décalage 2 px.
- Contraste WCAG AA minimum sur tout texte, y compris les éléments de
  navigation inactifs.

---

## 13. Commandes

```bash
# Backend — laisser tourner dans son terminal
cd backend && dotnet watch run        # http://localhost:5137

# Frontend — second terminal
cd frontend && npm run dev            # http://localhost:5173

# Vérifications
cd backend && dotnet build
cd frontend && npm run build          # tsc -b && vite build

# Migrations
cd backend
dotnet ef migrations add NomDeLaMigration
dotnet ef database update
```

Le proxy Vite redirige `/api` vers `http://localhost:5137`. Le frontend
n'appelle donc jamais l'URL absolue du backend.

---

## 14. Règles de travail

**Périmètre.** Annoncer les fichiers touchés avant de commencer, et s'y
tenir. Si une décision semble nécessaire hors du périmètre annoncé,
s'arrêter et demander plutôt que trancher seul.

**Vérification.** Un fichier n'existe que s'il est sur le disque. Toujours
finir une étape par un build vert.

**Migrations.** Ne jamais lancer `dotnet ef migrations add` ni
`dotnet ef database update` sans validation explicite. Présenter d'abord la
migration générée pour relecture : aucune opération destructrice
(`DropTable`, `DropColumn`, `AlterColumn`) ne doit toucher les tables métier
sans accord.

**Secrets.** Ne jamais tenter d'accéder aux user secrets, de deviner un mot
de passe, ni de lire les fichiers de configuration sensibles. Si des
identifiants sont nécessaires, les demander.

**Git.** Committer après chaque étape validée de `SPEC.md`, avec un message
décrivant le changement. Vérifier `git status` avant : seuls les fichiers
attendus doivent apparaître.

**Honnêteté technique.** Sur une erreur mal comprise, dire qu'on ne sait pas
plutôt que d'inventer une justification plausible. Les explications
rétrospectives fausses coûtent plus cher que l'aveu d'incertitude.
