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
  Conséquence concrète : la configuration **SMTP est optionnelle**. Son absence
  n'empêche pas KLEAN de démarrer, elle désactive le seul parcours qui en dépend
  — la réinitialisation de mot de passe, qui répond alors 503 avec la marche à
  suivre. En revanche une configuration **incomplète** fait échouer le démarrage :
  « pas configuré » est un choix d'exploitation, « configuré à moitié » est une
  erreur, et la découvrir quand un responsable a perdu son mot de passe est le
  pire moment.

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
├── CLAUDE.md, SPEC.md, README.md, RGPD.md, DEPLOIEMENT.md, LICENSE
├── docker-compose.yml   postgres, api, migrer (profil outils), caddy
├── .env.example         le seul endroit où un exploitant écrit un secret
├── backend/
│   ├── Dockerfile       l'API ET son bundle de migration, une seule image
│   ├── Models/          20 entités métier + AccesJournalise + Utilisateur
│   │                    + RolesKlean
│   ├── Data/            KleanDbContext (filtres multi-tenant), BasculeRoleApplicatif
│   │                    (la connexion sous la RLS), FabriqueDeConception (les
│   │                    migrations, sans Program.cs)
│   ├── DTOs/            un fichier par entité, XxxDto + XxxCreateDto
│   ├── Controllers/     un par entité + Auth + SecondFacteur + Planning
│   ├── Services/        AffectationService, RemplacementService,
│   │                    PlanningService, ServiceGeneration, JoursFeries,
│   │                    ServiceStock, ServicePrevisionStock,
│   │                    ServicePassage, MetadonneesJpeg,
│   │                    JwtService, ServiceSession, ServiceSecondFacteur, Totp,
│   │                    AmorcageService, TenantContext, LimiteurDebit,
│   │                    ServiceCourrielSmtp, DescripteurErreursFrancais,
│   │                    ListeTypePrestations
│   ├── Middleware/      VerificationCsrf, EnTetesSecurite, LimiteCorpsRequete,
│   │                    IdentifiantCorrelation
│   ├── Filtres/         JournaliserAcces — filtre d'action MVC, pas un middleware
│   ├── Migrations/
│   └── Scripts/         jeu de démonstration, contrat-ecrans-reference.py,
│                        et les cinq SQL de conservation — purge-journal-acces,
│                        purge-jetons-rafraichissement, purge-pointages,
│                        conservation-donnees-metier, degradation-absences-maladie
│                        (RGPD.md §5) ; et reinitialiser-second-facteur, pour le
│                        dernier administrateur
└── frontend/
    ├── Dockerfile       construit dist/, puis l'image Caddy qui le sert
    ├── Caddyfile        HTTPS, document, relais /api — sans journal d'accès
    ├── csp.ts           les deux politiques de sécurité du contenu
    ├── public/polices/  Figtree et IBM Plex Mono, servies en propre (RGPD.md §9.1)
    ├── vite.config.ts   serveur https, proxy /api, en-têtes
    ├── scripts/         verifier-interface.mjs, tests.mjs, et entetes-caddy.mjs,
    │                    qui lit csp.ts pour Caddy au lieu de la recopier
    └── src/
        ├── api/         wrappers fetch typés, client.ts, ressources.ts, + leurs séries
        ├── auth/        AuthContext, RouteProtegee, motDePasse.ts
        ├── comptes/     comptes.ts — l'état, les gestes et la création d'un compte
        ├── composants/  un .tsx + un .css par composant
        ├── layout/      Navigation, Disposition, sections.ts
        ├── pages/       un fichier par écran, plus Reference.tsx, Compte.tsx — le sien —
        │                et Comptes.tsx — ceux de l'entreprise ; MaJournee.tsx pour
        │                l'agent, Anomalies.tsx pour les rôles internes
        ├── passage/     passage.ts — gestes, preuves, checklist —, photo.ts — la réduction
        ├── reference/   descriptions.ts, valeurs.ts — le motif liste-et-formulaire
        ├── stock/       mouvements.ts, prevision.ts — la saisie et la lecture du stock
        ├── styles/      tokens.css
        ├── types/       interfaces TypeScript
        └── utils/       dates.ts, dates.test.ts
```

**Trois sources uniques, et aucune ne se recopie.** `layout/sections.ts` porte la
navigation, les routes et la destination d'arrivée ; `reference/descriptions.ts`
porte les écrans de données de référence — libellés, colonnes, champs, droits ;
`styles/tokens.css` porte tout le système d'interface de §12. Chacune est éprouvée
par une série qui vérifie ses invariantes, parce qu'une divergence y prend une
forme silencieuse : une pastille dont la route n'est pas gardée, un bouton qui
répond 403, un jeton mal orthographié qu'aucun navigateur ne signale.

---

## 5. Modèle de données

Vingt entités métier, toutes avec un `Id` de type `Guid` généré côté
application et une colonne `EntrepriseId` (dénormalisée, voir §7). `TypePrestation` est
le catalogue de l'étape 21 ; `Emplacement`, `MouvementStock` et `ConsommationType` sont
le stock de l'étape 22 ; `Pointage`, `PointControle`, `ControleEffectue`, `Anomalie` et
`PhotoAnomalie` sont le passage de l'agent de l'étape 25 (§9 ter).

Une seizième entité existe et n'est **pas** métier : `AccesJournalise`, le journal
d'accès aux données personnelles de §7 bis. Elle suit les mêmes règles — `Guid`
applicatif, `EntrepriseId` dénormalisé, filtre global — mais elle ne décrit rien du
nettoyage de bureaux : elle rend compte de l'usage des vingt autres.

```
Entreprise ─┬─ Client ─── Site ─── Contrat ─── Prestation ─── Intervention
            ├─ TypePrestation ────────────────────┘                 │
            │        └─ ConsommationType ─┐                         │
            ├─ Agent ─── Absence          │                         │
            ├─ Emplacement ─┐             │         Remplacement ───┘
            └─ Produit ─────┴─────────────┴─ Stock, MouvementStock — lieu : emplacement ou site

Intervention ─┬─ Pointage              début et fin, sans position
              ├─ ControleEffectue ···· PointControle ─── TypePrestation
              │                        (SetNull : le libellé est copié)
              └─ Anomalie ─── PhotoAnomalie
```

| Entité | Champs notables |
|---|---|
| Entreprise | RaisonSociale, Siret, DateCreation, Territoire (enum) — le fuseau d'« aujourd'hui » et le calendrier des jours fériés |
| Client | Type (enum `TypeClient`), coordonnées, DateCreation |
| Site | Latitude, Longitude, Consignes, DateCreation |
| Contrat | DateDebut, DateFin (nullable), Frequence (libellé libre), Actif, InclutJoursFeries |
| Prestation | DureeEstimeeMinutes, Jours (enum à drapeaux), HeureDebut (nullable), AgentTitulaireId (nullable, `SetNull`), TypePrestationId (nullable, `Restrict`) |
| TypePrestation | Libelle (unique dans l'entreprise, à la casse près : vérifié par le contrôleur **et** par l'index sur `lower("Libelle")`, en SQL brut), DureeParDefautMinutes, Actif — ni jours ni heure, qui dépendent du site |
| Agent | Latitude, Longitude (nullable), DateEmbauche, Actif, ChefEquipeId (nullable, `SetNull`, vise un **compte**) |
| Intervention | DatePrevue, HeurePrevue (TimeSpan), AgentId (nullable), Statut (enum) |
| Absence | Type (enum), DateDebut, DateFin, Motif |
| Remplacement | Statut (enum), Score (nullable), AgentRemplacantId (nullable) |
| Produit | Unite, SeuilAlerte, Actif |
| Emplacement | Nom, Type (enum `TypeEmplacement`), Actif — dépôt ou véhicule ; le site n'en est pas un |
| Stock | EmplacementId **ou** SiteId, deux clés `Restrict`, l'une exactement ; Quantite, jamais saisie, jamais négative |
| MouvementStock | Nature (enum), origine et destination (emplacement ou site), Quantite, Ecart (inventaire), DateMouvement, AuteurId — ni modifiable ni supprimable |
| ConsommationType | TypePrestationId, ProduitId (deux clés `Restrict`, un couple unique), QuantiteParPassage — sert la prévision, jamais une déduction |
| Pointage | InterventionId (`Restrict`), Moment (enum), Horodatage du serveur, Preuve (enum), DistanceMetres et PrecisionMetres (nullables), AuteurId — **aucune colonne de position** ; un début et une fin au plus par vacation, par index unique |
| PointControle | TypePrestationId (`Restrict`), Libelle, Ordre, Actif — la checklist d'un type |
| ControleEffectue | InterventionId (`Restrict`), PointControleId (nullable, `SetNull`), Libelle **copié**, Ordre, Fait — écrit à *Terminer*, jamais modifié |
| Anomalie | InterventionId (`Restrict`), Description (texte libre, 1 000), Statut (enum), DateSignalement, AuteurId, DateTraitement et TraiteeParId (nullables) |
| PhotoAnomalie | AnomalieId (`Cascade`), Contenu (`bytea`, JPEG sans métadonnées), Taille, DateAjout — trois au plus par anomalie |

Enums :
- `TypeClient` : EntrepriseCliente, Syndic, GestionnaireImmobilier,
  Copropriete, Autre
- `StatutIntervention` : Planifiee, EnCours, Terminee, NonCertifiee, Annulee
- `TypeAbsence` : Maladie, Conge, Indisponibilite, Retard, Autre
- `StatutRemplacement` : Recherche, Propose, Accepte, Refuse, Annule
- `TypeEmplacement` : Depot, Vehicule — `Site` en est sorti à l'étape 22, le site
  étant visé par sa propre clé
- `NatureMouvement` : Entree, Sortie, Transfert, Inventaire
- `Territoire` : Metropole, AlsaceMoselle, Guadeloupe, Martinique, Guyane, Reunion,
  Mayotte — lu par ses noms exacts, et sans défaut à l'écriture
- `JoursSemaine`, **à drapeaux** : Aucun, Lundi … Dimanche
- `MomentPointage` : Debut, Fin
- `PreuvePointage` : SurSite, HorsRayon, Imprecise, NonLocalisee
- `StatutAnomalie` : Ouverte, Traitee

Tous les enums sont stockés **en texte** via `HasConversion<string>()`.
Les DTOs les exposent en `string` et utilisent `Enum.TryParse` avec un
`BadRequest` explicite si la valeur est invalide.

⚠️ **`Enum.TryParse` accepte un nombre** : « 0 » y vaut la première valeur. Les deux
énumérations de l'étape 22, `TypeEmplacement` et `NatureMouvement`, sont donc lues par
leurs **noms exacts** (`Enum.GetNames<T>().Contains`) : une entrée de stock que personne
n'a choisie ne passe pas. Les plus anciennes gardent `Enum.TryParse`, et ne sont pas
reprises sans motif.

⚠️ **`JoursSemaine` fait exception sur deux points.** Le DTO l'expose en **tableau**
de noms — `["Lundi", "Jeudi"]`, ce que cochent des cases —, et il est lu par
`JoursDeLaSemaine.TryLire`, **jamais** par `Enum.TryParse`, qui accepterait « 3 »,
« Aucun » et « Lundi, Mardi » en une seule chaîne : trois récurrences que personne
n'a cochées. Trois sabotages l'ont montré.

⚠️ **La conversion d'énumération d'EF ne lève pas sur une valeur illisible** : une
chaîne vide en base est relue comme la valeur par défaut, en silence. Un test qui
vérifie un défaut de colonne par la lecture via l'API reste donc vert sans ce défaut.
Il faut lire la valeur **stockée**.

`Contrat.Frequence` reste un **libellé libre** : c'est l'étiquette des contrats dans
les écrans de référence (§11), et ce que lit un humain. Ce que la génération lit est
`Prestation.Jours`.

---

## 6. Authentification

- Identity avec `Utilisateur : IdentityUser<Guid>`, portant `EntrepriseId`
  (obligatoire), `AgentId` et `ClientId` (nullables selon le rôle).
- JWT signé HMAC-SHA256, **quinze minutes**, livré en **cookie `HttpOnly`
  `Secure` `SameSite=Strict`**. Aucun jeton ne traverse le corps d'une réponse,
  et l'en-tête `Authorization` n'est plus accepté : un seul chemin
  d'authentification, donc un seul à auditer.
- **Jeton de rafraîchissement** opaque de 256 bits, persisté par empreinte
  SHA-256 dans `JetonsRafraichissement`, donc **révocable** — ce qu'un JWT signé
  ne peut pas être. Rotation à chaque usage ; un jeton rejoué coupe toutes les
  sessions du compte. Session absolue de quatorze jours, que la rotation ne
  prolonge pas.
- **Révocation immédiate** : `Actif` et `SecurityStamp` sont vérifiés à **chaque
  requête** dans `OnTokenValidated`, au prix d'une lecture par clé primaire sur
  `AspNetUsers`. Sans cela, désactiver un compte le laissait pleinement actif
  jusqu'à l'expiration du jeton.
- **Protection CSRF** par double soumission, conséquence directe du cookie :
  `VerificationCsrf` exige l'en-tête `X-Klean-CSRF` égal au cookie `klean_csrf`
  sur toute écriture accompagnée d'un cookie de session. Les lectures et la
  connexion en sont dispensées.
- Le token porte la revendication **`entrepriseId`** : c'est elle qui
  alimente le multi-tenant.
- Cinq rôles dans une classe statique `RolesKlean` : Administrateur,
  Responsable, ChefEquipe, Agent, ClientSyndic.
- Amorçage au démarrage : crée les rôles, puis une entreprise et un compte
  administrateur si aucun utilisateur n'existe. Lit
  `Amorcage:EmailAdmin`, `Amorcage:MotDePasseAdmin`,
  `Amorcage:RaisonSocialeEntreprise`. Ce compte est marqué **provisoire**, comme tout
  compte créé : son mot de passe a été écrit dans un fichier, et la première connexion
  le fait remplacer (voir *Comptes de l'entreprise* plus bas). ⚠️ Non éprouvé par un
  test — la série ne peut pas offrir une base sans aucun compte.
- Règles de mot de passe : 10 caractères minimum, majuscule, minuscule,
  chiffre. Pas de caractère spécial exigé. Verrouillage après 5 échecs
  pendant 15 minutes.

  ⚠️ **Aucun `[MinLength]` sur les DTOs de mot de passe.** La validation de
  modèle d'ASP.NET s'exécute avant le contrôleur, donc avant Identity, et renvoie
  son propre message **en anglais** que `DescripteurErreursFrancais` ne peut pas
  atteindre. Elle dupliquerait en plus une règle dont Identity est l'autorité.
  Identity valide, et parle français.
- **Limitation de débit** par `LimiteurDebit`, fenêtre glissante, **par adresse
  et par compte** sur `connexion`, `mot-de-passe-oublie` et `reinitialiser`.
  Plafonds dans `PlafondsDebit`. Le verrouillage d'Identity est par compte : il
  ne ralentit en rien le bourrage d'identifiants, un essai sur cent comptes.
  Une tentative refusée n'est pas comptée, sinon réessayer prolongerait
  indéfiniment le blocage. Compteur en mémoire de processus : derrière plusieurs
  instances, la limite est par instance.
- **Réinitialisation de mot de passe** : `POST /api/auth/mot-de-passe-oublie`
  puis `POST /api/auth/reinitialiser`. Jeton Identity à usage unique, une heure.
  La demande répond **202 quoi qu'il arrive** — compte inexistant, désactivé ou
  valide — sinon l'endpoint devient un vérificateur d'adresses ouvert à tous. La
  réussite régénère le `SecurityStamp` (donc tue les jetons d'accès) **et**
  révoque toutes les sessions de rafraîchissement.
- **Changement de mot de passe par son propriétaire** :
  `POST /api/auth/changer-mot-de-passe`, ouvert à tout compte connecté. Le mot de
  passe **actuel** est la preuve d'identité, donc aucun courriel n'est requis : c'est
  le seul parcours de mot de passe qui fonctionne sur une instance sans SMTP, et il
  évite qu'un administrateur pose lui-même celui d'un collègue — il le connaîtrait.

  **L'appareil qui change le mot de passe reste connecté, tous les autres tombent.**
  Identity régénère le `SecurityStamp`, ce qui périme instantanément les jetons
  d'accès du compte ; les sessions de rafraîchissement sont révoquées en plus ; puis
  une session neuve est ouverte pour l'appelant. C'est une ré-authentification, pas
  une prolongation silencieuse — et renvoyer l'utilisateur à l'écran de connexion
  pour retaper le mot de passe qu'il vient de choisir n'apporterait rien, qui tient
  cette session la tenant déjà.

  ⚠️ **L'ordre des deux gestes est impératif : révoquer, puis rouvrir.** Dans l'autre
  sens, le jeton de rafraîchissement tout juste émis est révoqué par le geste suivant,
  et l'utilisateur tombe au premier renouvellement — quinze minutes plus tard, donc
  très loin de sa cause. Éprouvé par un sabotage.

  ⚠️ **Un mot de passe actuel faux rend 400, jamais 401**, et ce n'est pas une
  préférence de style : voir le piège correspondant en §10.

  ⚠️ **Le plafond de débit y est nécessaire malgré l'authentification.** L'endpoint
  vérifie le mot de passe actuel, donc c'est un oracle, et le verrouillage d'Identity
  ne s'y applique pas — `ChangePasswordAsync` ne compte aucun échec. Sans plafond, qui
  tient une session volée y éprouve des mots de passe sans limite ; non pas pour
  entrer, il est déjà entré, mais pour en apprendre un sans doute réemployé ailleurs.
- **Déconnexion de tous les appareils** : `POST /api/auth/deconnexion-totale`,
  qui exige un jeton d'accès valide — contrairement à `deconnexion`, tolérante par
  nécessité. Elle régénère le `SecurityStamp` **et** révoque toutes les sessions de
  rafraîchissement.

  C'est la fermeture de la seule brèche assumée du cookie de 15 minutes : le jeton
  d'accès est signé, donc rien ne le rappelle. Elle ne coûte **aucune lecture
  supplémentaire par requête**, parce que la vérification existait déjà —
  `OnTokenValidated` compare l'empreinte de sécurité à chaque appel. Il manquait le
  geste qui change l'empreinte.

  ⚠️ Elle tue aussi le jeton qui a servi à l'appeler. C'est voulu : « tous les
  appareils » inclut celui-ci, et les cookies sont effacés dans la même réponse.
- **Double authentification** (étape 23) : codes TOTP à six chiffres, **obligatoire**
  pour `Administrateur` et `Responsable` (`RolesKlean.ObligesAuSecondFacteur`),
  facultative pour les autres. `ServiceSecondFacteur` et `SecondFacteurController`.
  - **La connexion se fait en deux temps.** Un bon mot de passe sur un compte équipé, ou
    obligé, rend **202** `{ etape: "SecondFacteur" | "Equipement" }` et un cookie de défi
    de cinq minutes (`klean_defi`, chemin `/api/auth`, chiffré, lié à l'empreinte de
    sécurité). `POST /api/auth/connexion/code` ouvre ensuite la session. Le compte obligé
    non équipé s'équipe **dans** la connexion : `preparer` et `activer` acceptent ce défi
    à la place d'une session.
  - **Aucune session sans second facteur pour un rôle obligé** : `OnTokenValidated` et le
    rafraîchissement le refusent, sans lecture de plus.
  - ⚠️ **Rien de l'authentificateur d'Identity n'est employé**, et ce n'est pas un oubli :
    il range la clé et les codes en clair, et accepte un code cinq pas durant, autant de
    fois qu'on veut. Ici, la clé est chiffrée par la Data Protection, dont le trousseau
    vit hors de la base ; les codes de récupération, sur 80 bits, ne sont gardés que par
    empreinte ; et un code ne sert qu'une fois, à un pas près de tolérance. Le tout tient
    en trois jetons du fournisseur `[KleanSecondFacteur]` dans `AspNetUserTokens`.
  - ⚠️ **Un code faux compte dans le verrouillage, avec ceux du mot de passe** : cinq en
    tout. Sans ce partage, le million de codes s'essaierait à côté du verrou.
  - ⚠️ **Perdre le trousseau de la Data Protection, c'est perdre toutes les clés** : le
    volume `cles-api` se sauvegarde aussi souvent que la base, mais **jamais avec elle** —
    les deux ensemble livrent toutes les clés. Une clé indéchiffrable lève une erreur
    qui nomme la cause, plutôt que de répondre « code faux » jusqu'au verrouillage, et
    sans compter d'échec.
  - **« Changer de téléphone » accepte un code de récupération**, et c'est son cas
    principal : qui change de téléphone a souvent perdu l'ancien. Sur le code de
    l'application seul, il se connectait par ses codes sans jamais pouvoir rééquiper.
    Les codes, gardés par empreinte, survivent aussi à la perte du trousseau.
  - **Le dernier administrateur sans téléphone ni codes n'a aucune sortie par l'API**,
    délibérément : une route qui retire le second facteur sans code est ce que chercherait
    un voleur de mot de passe. `Scripts/reinitialiser-second-facteur.sql` fait en base les
    gestes de la route d'administration, rapport d'abord, `-v confirmer=1` ensuite, en un
    seul bloc `DO` pour qu'aucun état intermédiaire ne subsiste. Il ne laisse aucune ligne
    au journal d'accès. Procédure : `DEPLOIEMENT.md` §10.
  - ⚠️ **L'horloge du serveur doit être synchronisée** : au-delà d'une minute d'écart
    environ, aucun code ne passe, et chaque refus avance le verrouillage.
  - En test, la fixture équipe les comptes obligés à leur première connexion et produit
    les codes avec `TotpDeTest`, écrit sans rien reprendre de `Totp`.
- **Comptes de l'entreprise** (étape 24) : `GET /api/auth/comptes` les liste, et
  l'écran *Comptes* de la section Entreprise les gère. Tout est réservé à
  l'administrateur et cadré à la main sur `EntrepriseId` (§7).
  - **Personne ne choisit le mot de passe d'un autre.** `POST /api/auth/comptes` ne reçoit
    aucun mot de passe : le serveur en tire un de seize caractères, sans caractères
    ambigus, le rend **une seule fois** dans `CompteCreeDto`, et marque le compte
    `MotDePasseProvisoire`. L'administrateur le transmet ; il ne le connaît que jusqu'à la
    première connexion.
  - **La première connexion le fait remplacer.** Un bon mot de passe sur un compte
    provisoire rend 202 `{ etape: "ChangementMotDePasse" }`, sur le même défi que le second
    facteur, puis `POST /api/auth/connexion/mot-de-passe` enchaîne sur l'équipement, le
    code ou la session. Le provisoire lui-même y est refusé. Le mot de passe oublié lève
    aussi le drapeau : la personne l'a choisi.
  - ⚠️ **Cette étape passe avant le second facteur**, et l'ordre n'est pas libre : changer
    le mot de passe régénère l'empreinte de sécurité, que le défi porte. Dans l'autre sens,
    le défi du code serait périmé par le geste qui le précède.
  - ⚠️ **La route refuse le défi d'une autre étape.** Sans cette garde, qui tient le mot de
    passe d'un compte équipé le remplacerait sans jamais saisir de code.
  - ⚠️ **Le drapeau est levé avant `ResetPasswordAsync`**, qui enregistre l'utilisateur
    entier : les deux changements partent dans la même écriture, et aucun état « mot de
    passe choisi, toujours provisoire » ne subsiste.
  - **Fermer et rouvrir** : `POST /api/auth/comptes/{id}/desactiver` et `/reactiver`,
    journalisés (§7 bis). Fermer pose `Actif = false`, régénère l'empreinte **et** révoque
    les sessions — les trois, pour la raison du §10 sur les jetons de rafraîchissement.
    Jamais sur soi-même, et un compte pseudonymisé ne se rouvre pas : 409, qui nomme la
    sortie, créer un compte neuf. Une garde « dernier administrateur » serait inatteignable
    — l'appelant en est un, actif — et n'est donc pas écrite (voir §7 bis, la
    pseudonymisation d'un compte).
  - ⚠️ **Les deux rendent le compte, et non 204** : le journal ne consigne qu'une réponse
    porteuse d'un corps. Un 204 n'aurait laissé aucune ligne, sans aucune erreur.
  - Le rôle ne se change pas : aucune route ne le permet, et l'écran ne le propose pas.
- **Messages d'Identity en français** via `DescripteurErreursFrancais` : les
  descriptions traversent l'API telles quelles, `AuthController` les recopie dans
  ses `BadRequest`, et l'écran n'a aucun moyen de traduire un texte libre.
- `UserName` doit être renseigné avec la même valeur que `Email` à chaque
  création de compte, sinon Identity refuse.

Tous les contrôleurs métier portent `[Authorize]`. Les restrictions par rôle
viennent plus tard dans la feuille de route.

---

## 6 bis. Surface HTTP

Numérotée « bis » pour ne pas décaler les §7 à §14, référencés depuis des
dizaines de commentaires du code.

Tout est câblé dans `Program.cs`, et **l'ordre du pipeline est la
fonctionnalité** : chaque ligne déplacée change ce qui est protégé sans rien
casser de visible.

```
UseForwardedHeaders             si Proxy:Reseaux est renseigné, et EN PREMIER
IdentifiantCorrelation          avant le gestionnaire : l'identifiant part sur le 500
UseExceptionHandler             500 en problem+json, rien de l'exception
UseHsts + UseHttpsRedirection   hors développement seulement
UseRequestTimeouts
LimiteCorpsRequete              avant toute lecture de corps
UseCors(« klean »)              avant la session : une origine interdite n'atteint rien
UseSwagger + UseSwaggerUI       développement seulement
EnTetesSecurite                 APRÈS Swagger, délibérément
VerificationCsrf
UseAuthentication / UseAuthorization
```

**Derrière un proxy inverse, `Proxy:Reseaux` est obligatoire, et il ne doit contenir
que le proxy.** Sans lui, l'API voit l'adresse du proxy pour tout le monde : la
limitation de débit par adresse devient un plafond **global** — un attaquant qui
épuise celui de la connexion bloque toute l'instance —, `AdresseIp` des sessions ne
consigne plus que le proxy, et `UseHttpsRedirection` boucle, le proxy ayant terminé
TLS. L'excès inverse est pire : croire `X-Forwarded-For` de toute source laisse chaque
client choisir son adresse, donc sortir du plafond à volonté. D'où une liste de
réseaux, vide par défaut, qui **remplace** la confiance par défaut en la boucle locale,
et un seul saut retenu. Jamais `ASPNETCORE_FORWARDEDHEADERS_ENABLED`, qui croit tout
le monde. Éprouvé par `ExploitationTests`, dans les deux sens.

⚠️ **Tout en-tête de réponse se pose dans `OnStarting`, pas à l'aller.**
`UseExceptionHandler` vide les en-têtes avant d'écrire son 500 : `EnTetesSecurite`
et `IdentifiantCorrelation` posés à l'aller manqueraient précisément sur la réponse
d'erreur. Deux sabotages l'ont montré.

**`EnTetesSecurite` est enregistré après `UseSwaggerUI`.** L'interface de Swagger
a besoin de script et de style en ligne, que la politique interdit ; elle
court-circuite le pipeline, donc ses réponses n'atteignent jamais le middleware.
L'ordre suffit, aucune exception de chemin à écrire.

**Il y a deux politiques de sécurité du contenu, et il ne faut jamais recopier
l'une sur l'autre.**

| | Où | Contenu |
|---|---|---|
| API | `Middleware/EnTetesSecurite.cs` | `default-src 'none'` et rien d'autre |
| Document | `frontend/csp.ts` | `'self'` sur `script-src`, `style-src`, `font-src`, `connect-src` ; `img-src 'self' data: blob:` |

L'API ne rend que du JSON — et les photos d'anomalie, en `image/jpeg` : rien ne doit
jamais être chargé depuis une de ses réponses. Le document, lui, charge une feuille de style, un script et quatre
polices — tous servis par lui-même. Appliquer la politique de l'API au document
bloquerait les trois, et le défaut **ne se voit pas au développement**, où le cache
du navigateur garde les polices et où la politique servie est plus permissive.

⚠️ **Aucune des deux politiques ne déclare d'origine externe, et il ne faut pas en
réintroduire.** `style-src` et `font-src` ont porté `fonts.googleapis.com` et
`fonts.gstatic.com` jusqu'au 3 octobre 2026, parce qu'`index.html` chargeait les
polices chez Google. Cela transmettait l'adresse IP de chaque visiteur de chaque
instance de KLEAN à un tiers, sans que l'exploitant l'ait choisi, et contredisait le
§2 — « pas de service tiers obligatoire ». Les fichiers sont maintenant dans
`frontend/public/polices/` (RGPD.md §9.1), et `npm run verifier` échoue sur toute
origine externe réapparue dans `csp.ts` ou `index.html` : **le rappel est mécanique,
pas verbal.**

**`blob:` dans `img-src` n'est pas une origine externe**, et le vérificateur l'accepte :
c'est une URL que le navigateur fabrique pour un objet déjà en mémoire. Elle sert depuis
l'étape 25 à l'aperçu d'une photo avant son envoi, et à l'affichage de chaque photo
d'anomalie. ⚠️ Une photo ne se pose **jamais** par son URL d'API dans un `<img>` : le
navigateur enverrait bien les cookies, mais un jeton d'accès expiré y donnerait une image
cassée, sans rafraîchissement, `api/client.ts` étant le seul à savoir rafraîchir. Elle
passe par `api.getBlob`, puis par `URL.createObjectURL`, révoquée au démontage.

`frontend/csp.ts` porte les deux variantes, et elles ne diffèrent désormais plus que
par `'unsafe-inline'` et `ws:`, qu'exige le serveur de Vite — le préambule de
rafraîchissement de React est un script en ligne, le remplacement de module à chaud
injecte des `<style>`, et il dialogue par WebSocket. C'est toute la raison de la
séparation : une politique unique aurait dû affaiblir la production pour le confort du
développement. `npm run preview` sert la variante de production — le seul endroit où
l'on constate qu'elle ne bloque rien avant de déployer.

**CORS** : politique nommée, origines lues dans `Cors:Origines`, **vide par
défaut**. Une origine déclarée obtient `AllowCredentials`, indispensable puisque
l'authentification passe par cookie — et incompatible avec une origine jokers, ce
qu'ASP.NET refuse de lui-même.

⚠️ Les cookies sont `SameSite=Strict`. Déclarer une origine ne suffit donc **pas**
à faire fonctionner un frontend hébergé sur un autre domaine enregistrable : le
navigateur n'enverrait aucun cookie. Le multi-origine n'est viable qu'entre
sous-domaines d'un même domaine.

**Les quatre filtres de parenté**, ajoutés au lot B de l'étape 16 bis : `?clientId=`
sur `GET /api/sites`, `?siteId=` sur les contrats, `?contratId=` sur les prestations,
`?agentId=` sur les absences. Ils portent la descente de niveau des écrans de
référence — « les sites de ce client ». Trois règles, et chacune ferme un défaut muet :

- **Le filtre entre avant le comptage.** `Total` est le nombre de lignes du filtre, pas
  de la table : sinon l'écran annonce quarante sites au-dessus d'une liste qui en
  montre trois, et la pagination propose des pages vides.
- **Un parent inconnu rend une page vide, pas un 404.** Une liste filtrée sur rien
  reste une liste. Un 404 obligerait l'écran à distinguer « ce client n'existe pas » de
  « ce client n'a pas de site », alors que la seconde réponse est la bonne dans les
  deux cas. Un identifiant appartenant à une autre entreprise tombe dans le même cas,
  les filtres globaux s'appliquant d'abord.
- ⚠️ **Un paramètre vide vaut absence de filtre**, et c'est mesuré : `?clientId=` sur un
  `Guid?` est lié à `null` par ASP.NET, pas refusé. Le serveur rend donc la liste
  entière. L'écran doit lire ce paramètre de la même façon — `filtreDe` dans
  `reference/valeurs.ts` — sinon il affiche « Sites de … » au-dessus de tous les sites
  de l'entreprise. Un identifiant **illisible**, lui, est bien refusé en 400.

**Deux gardes sur la taille du corps, et les deux sont nécessaires.**
`LimiteCorpsRequete` refuse sur `Content-Length`, avant toute lecture et quel que
soit le serveur ; la limite de Kestrel arrête un corps en fragments, qui n'annonce
pas sa taille. Voir le piège correspondant en §10.

## 7. Multi-tenant

Mécanisme en quatre parties. Les trois premières sont du code, et toutes nécessaires ;
la quatrième est la base, qui refuse quand l'une d'elles manque :

1. **`ITenantContext` / `TenantContext`** lit la revendication `entrepriseId`
   du token via `IHttpContextAccessor`. Expose aussi `DesactiverFiltrage()`,
   utilisé par l'amorçage qui doit créer la première entreprise avant
   qu'aucun tenant n'existe.

2. **Filtres globaux EF Core**, **vingt et un** au total :
   ```csharp
   modelBuilder.Entity<X>().HasQueryFilter(e =>
       !TenantFiltrageActif || TenantEntrepriseId == null
       || e.EntrepriseId == TenantEntrepriseId);
   ```
   où `TenantFiltrageActif` et `TenantEntrepriseId` sont des **propriétés
   privées d'instance du DbContext**. Voir le piège §10.

   Dix-neuf filtres sur les entités métier portant `EntrepriseId`, un vingtième sur
   `AccesJournalise` (§7 bis), **plus un sur `Entreprise` elle-même**, sur son
   propre `Id`. Depuis l'étape 21, `IsolationTests` refuse toute entité portant
   `EntrepriseId` sans filtre : une entité oubliée ne casse rien de visible, elle se lit
   seulement d'une entreprise à l'autre. Celui d'`Entreprise` :
   ```csharp
   modelBuilder.Entity<Entreprise>().HasQueryFilter(e =>
       !TenantFiltrageActif || TenantEntrepriseId == null
       || e.Id == TenantEntrepriseId);
   ```
   ⚠️ Elle ne porte pas de colonne `EntrepriseId`, son identifiant **est** le
   tenant — d'où l'oubli facile. Sans ce filtre, `GET /api/entreprises` expose
   la raison sociale et le SIRET de toutes les entreprises de l'instance.

   ⚠️ **`JetonsRafraichissement` n'est pas filtrée non plus**, et ne porte pas de
   colonne `EntrepriseId`. Le rafraîchissement s'exécute **hors requête
   authentifiée** — le jeton d'accès vient justement d'expirer — donc aucun tenant
   n'est disponible pour filtrer. Le cadrage est assuré par la recherche sur une
   empreinte de 256 bits, qui n'est pas devinable. L'entreprise se lit sur
   l'utilisateur rattaché.

   ⚠️ **`AspNetUsers` n'est pas filtrée**, et ne doit pas l'être : Identity
   interroge les comptes hors de toute requête authentifiée — la connexion, par
   définition — et sa validation d'unicité d'adresse repose sur des requêtes non
   filtrées. Conséquence : tout endpoint listant ou cherchant des comptes doit
   filtrer à la main sur `EntrepriseId`.

   L'échappatoire `TenantEntrepriseId == null` n'est sûre **que parce que** les
   contrôleurs métier portent tous `[Authorize]`. Retirer cet attribut sur un
   seul d'entre eux ouvrait, jusqu'à l'étape 26, l'accès anonyme à toutes les
   entreprises. **La base ferme désormais ce trou** (point 4) : sans entreprise dans
   le jeton, elle ne rend aucune ligne. L'attribut reste obligatoire — il porte les
   rôles, et `AspNetUsers` n'est pas sous la RLS.

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

4. **La sécurité au niveau des lignes de PostgreSQL** (étape 26). La migration
   `SecuriteAuNiveauDesLignes` pose une politique sur les vingt tables qui portent
   `EntrepriseId`, `AspNetUsers` exceptée, et sur `Entreprises` par son `Id` :
   ```sql
   "EntrepriseId" = nullif(current_setting('klean.entreprise', true), '')::uuid
   ```
   en lecture comme en écriture (`USING` et `WITH CHECK`). `Data/BasculeRoleApplicatif.cs`,
   un intercepteur de connexion, pose à chaque ouverture l'entreprise du jeton dans
   `klean.entreprise` — vide sans elle, jamais omise, sinon une connexion du pool
   garderait celle de la requête précédente —, puis `SET ROLE klean_app` : un rôle
   sans connexion, sans droits de structure, sans `BYPASSRLS`. Les filtres d'EF
   restent la garde **principale** ; la base est le filet du jour où l'un manque.

   ⚠️ **Le contournement veut dire « rester propriétaire », et rien d'autre.** Sous
   `DesactiverFiltrage` — l'amorçage, la fixture de test —, et pour un contexte
   construit hors injection — les migrations —, la connexion revient au rôle de
   connexion, propriétaire des tables. Pas de `FORCE ROW LEVEL SECURITY`, donc le
   propriétaire n'est pas soumis. **Aucune valeur du réglage n'ouvre tout** : un code
   fautif n'a rien à poser pour sortir de la RLS en restant `klean_app`.

   ⚠️ **Un filet contre un filtre oublié, pas un moindre privilège** : une injection
   SQL pourrait jouer `RESET ROLE`. Et **l'entreprise seule** : les axes client, agent
   et équipe restent aux filtres d'EF.

   ⚠️ **Une table qui porte `EntrepriseId` reçoit sa politique dans la migration qui
   la crée.** Les privilèges par défaut lui donnent les droits de `klean_app` sans
   qu'on y pense, pas la politique : `SecuriteDesLignesTests` compare le catalogue de
   la base aux tables qui portent la colonne, et rougit sur l'oubli — comme
   `IsolationTests` sur le modèle.

   Hors de la RLS, et c'est écrit dans la migration : `AspNetUsers`,
   `JetonsRafraichissement`, les autres tables d'Identity, et `__EFMigrationsHistory`,
   où `klean_app` n'a aucun droit.

`EntrepriseId` est **dénormalisé** sur les quatorze entités qui ne l'ont pas
naturellement (Site, Contrat, Prestation, Intervention, Absence,
Remplacement, Stock, MouvementStock, ConsommationType, Pointage, PointControle,
ControleEffectue, Anomalie, PhotoAnomalie). Choix assumé : évite trois jointures par ligne sur la
vue planning. La cohérence est garantie applicativement, sans clé étrangère.

### L'équipe d'un chef — quatrième axe

Depuis l'étape 19, `ITenantContext.ChefEquipeId` vaut l'identifiant du compte quand il
porte le rôle `ChefEquipe`. Quatre filtres globaux s'en servent, et quatre autres depuis
l'étape 25 par leurs sous-requêtes, et c'est une
**frontière**, du même ordre que celle du syndic : avec plusieurs chefs, une fuite
montrerait à chacun les équipes des autres.

| Entité | Le chef voit |
|---|---|
| `Agent` | ceux dont `ChefEquipeId` est son compte |
| `Absence` | celles de ces agents |
| `Intervention` | celles de ces agents ; et, **sans agent**, celles dont le titulaire de la prestation est l'un d'eux |
| `Remplacement` | ceux des interventions qu'il voit — la règle n'est pas recopiée, le filtre des interventions s'applique à la sous-requête |
| `Pointage`, `ControleEffectue`, `Anomalie` | ceux des interventions qu'il voit, de la même façon (étape 25) |
| `PhotoAnomalie` | celles des anomalies qu'il voit — deux sous-requêtes en chaîne |

⚠️ **Les quatre filtres du passage excluent aussi le syndic**, par `TenantClientId == null` :
le filtre des interventions ne restreint pas le client, et le syndic n'a aucune route du
passage avant un portail. Aucune requête ne pouvant l'atteindre, la clause est éprouvée
par un test qui lit le `DbContext` avec un tenant de syndic.

Clients, sites, contrats et prestations restent ceux de l'entreprise : aucune personne
n'y est nommée, seulement l'identifiant d'un titulaire, que l'écran affiche « Hors de
votre périmètre » quand il n'est pas dans l'équipe.

Quatre choses à savoir avant d'y toucher :

- ⚠️ **Une vacation affectée appartient à l'équipe de son agent, pas du titulaire.** Le
  titulaire ne départage que les vacations sans agent. Dans l'autre sens, un chef verrait
  le travail d'un agent qu'il n'encadre pas.
- **Le moteur d'affectation, le planning et le cycle de remplacement n'en savent rien.**
  Ils lisent les ensembles filtrés : les candidats d'un chef sont son équipe par
  construction, et un agent d'une autre équipe est « inconnu » à la proposition comme au
  `PUT`. Les filtres durs n'y perdent rien : toutes les vacations et les absences d'un
  agent de l'équipe restent visibles à son chef.
- **Lu sur le rôle, pas sur une revendication** : l'équipe est lue en base à chaque
  requête, donc un rattachement prend effet sans renouveler le jeton.
- **Les conditions sont des sous-requêtes `Any`, jamais des navigations**, et le coût en
  est mesuré dans le `DbContext`. Voir aussi le piège de §10 sur les paramètres nuls.

Le rattachement lui-même — `Agent.ChefEquipeId` vers `AspNetUsers` — est vérifié à la
main à chaque écriture, même entreprise **et** rôle `ChefEquipe`, `AspNetUsers` n'étant
pas filtrée.

---

## 7 bis. Données personnelles : journal d'accès et pseudonymisation

Numérotée « bis » pour la même raison que §6 bis : ne pas décaler des sections
référencées depuis des dizaines de commentaires du code.

Le produit détient, sur des salariés, des noms, des adresses, **les coordonnées GPS
de leur domicile** et des arrêts maladie. `AccesJournalise` consigne qui a lu quoi.

**`RGPD.md` est le document de référence**, écrit au lot D de l'étape 17 : registre des
traitements, cartographie table par table, durées de conservation, droits des personnes,
modèle de note d'information aux agents, et les écarts constatés. Cette section-ci décrit
le **mécanisme** ; `RGPD.md` décrit le **traitement**. Trois points à en retenir avant
d'écrire du code ici :

- Une colonne ajoutée à une table du §3 de `RGPD.md` change la cartographie. Une colonne
  ajoutée à `Clients` qui ne soit pas une coordonnée professionnelle fait tomber
  l'exemption de `ClientDto` — la condition est écrite, elle n'a pas à être rediscutée.
- **Une donnée pseudonymisée reste une donnée personnelle**, donc elle garde une durée de
  conservation. C'est pour cela que le mot compte (§8.1 de `RGPD.md`).
- **Un seul mécanisme de conservation existe** — la purge du journal. Les neuf autres
  durées sont déclaratives, et c'est inscrit noir sur blanc plutôt que supposé.

**Douze lectures sont journalisées, et deux ne s'en doutaient pas.** C'est le fait
central de ce mécanisme : chercher « les routes `/api/agents` et `/api/absences` »
donne un journal qui a l'air complet et qui a deux trous.

| Action | Ressource | Ce qu'elle divulgue |
|---|---|---|
| `GET /api/agents` et `/{id}` | `Agent` | nom, adresse, coordonnées du domicile |
| `GET /api/absences` et `/{id}` | `Absence` | type, dates, motif |
| `GET /api/interventions/{id}/candidats` | `Candidats` | nom, prénom, **distance au domicile** |
| `GET /api/remplacements/{id}/candidats` | `Candidats` | la même chose, par une seconde route |
| `GET /api/planning` | `Planning` | nom de **tous** les agents, et la cause « Agent absent » |
| `GET /api/journal-acces` | `JournalAcces` | le journal lui-même |
| `GET /api/agents/{id}/dossier` | `Agent` | **tout**, en une réponse — nature `Export` |
| `GET /api/agents/chefs-equipe` | `Compte` | nom et prénom des chefs d'équipe, pour le rattachement |
| `GET /api/agents/choix` | `ChoixAgents` | nom et prénom des agents, pour les listes déroulantes |
| `GET /api/auth/comptes` | `Compte` | adresse, nom, prénom, rôle et état de **tous** les comptes de l'entreprise |

⚠️ **`ChoixAgents` est une ressource à part, et ni une exemption ni une ligne `Agent`.**
Les écrans des prestations et des absences la chargent à chaque ouverture : journalisée
en `Agent`, elle noierait les vraies lectures de fiches ; exemptée, des noms de collègues
sortiraient sans trace, alors que la liste des chefs, qui ne rend que des noms elle aussi,
est journalisée. Distincte, le bruit se filtre. Elle existe pour la **minimisation** :
ces écrans recevaient l'adresse et les coordonnées du domicile de chaque agent par
`GET /api/agents`, pour n'afficher qu'un nom. Une ressource qui porte des données
personnelles au-delà de son étiquette déclare sa `routeChoix` (§11).

`CandidatDto` expose `DistanceKm`, dérivée des coordonnées du domicile — exactement
la donnée que `SPEC.md` signale comme relevant de la doctrine CNIL. Et
`PlanningNonPourvueDto.Cause` vaut littéralement « Agent absent » : **le planning
divulgue une absence** sans qu'on lise la liste des absences.

**Six règles, et chacune ferme quelque chose.**

1. **Le journal ne contient jamais ce qui a été lu.** Ni nom, ni adresse, ni motif,
   ni liste d'identifiants. Pour une lecture unitaire, l'identifiant de la ligne ;
   pour une liste, le **nombre** de lignes. Un journal qui recopie ce qu'il surveille
   devient une seconde base de données personnelles, moins protégée que la première,
   et double le risque qu'il existe pour documenter.
2. **Le compte porte sur les `Elements`, jamais sur `Total`.** Le journal dit ce qui
   est sorti, pas ce qui existe : confondre les deux ferait écrire « a lu 150 agents »
   sur une page de 25.
3. **Aucune adresse IP.** Donnée personnelle de plus, dont la conservation
   demanderait sa propre justification, et qui n'ajoute rien à « qui a lu quoi » dans
   un produit où chaque compte est nominatif.
4. ⚠️ **L'échec d'écriture du journal fait échouer la réponse** — aucun `try/catch`
   dans `JournaliserAccesAttribute`, et c'est la décision, pas un oubli. Un journal
   qu'on contourne en cassant son écriture n'en est pas un. Le coût est faible parce
   que le journal vit dans la **même base** que les données : si l'on a pu lire les
   agents, on peut insérer une ligne ; un échec signale une dérive de schéma ou un
   disque plein, pas une panne passagère sans rapport. Pour la même raison, **aucune
   file d'attente** — une écriture différée rendrait l'échec invisible.
5. **Seules les divulgations sont consignées.** Un 403, un 404, une écriture ne
   laissent rien : rien n'est sorti. Journaliser les tentatives serait un journal de
   sécurité, légitime mais différent — mêlé à celui-ci, le sondage d'un compte mal
   cadré noierait les divulgations réelles. **Quatre exceptions écrites**, des gestes
   d'administrateur sur le compte d'autrui : l'anonymisation ; la réinitialisation du
   second facteur (étape 23), qui en abaisse la protection jusqu'au prochain équipement ;
   et la fermeture et la réouverture d'un compte (étape 24), natures `Desactivation` et
   `Reactivation`, qui retirent puis rendent un accès.
6. **Lecture seule, administrateur seul, purge hors de l'application.**
   `JournalAccesController` n'expose que `GET` : un journal que l'API permet d'élaguer
   ne prouve rien, il suffirait d'un compte administrateur compromis. La conservation
   est de **douze mois**, par `Scripts/purge-journal-acces.sql`. Ouvert au
   `Responsable` ou au `ChefEquipe`, le journal deviendrait un outil de surveillance
   des collègues — ce qu'un journal RGPD n'est pas censé être.

**Le branchement est un filtre d'action par attribut**, et ce qui tient n'est pas
l'attribut, c'est l'invariante : `JournalAccesTests` énumère par réflexion toutes les
actions de contrôleur, descend dans leurs types de retour jusqu'aux DTOs, et exige
l'attribut sur toute action **`GET`** dès qu'un DTO atteint porte `Nom`, `Prenom`,
`Adresse`, `Email`, `Telephone`, `Latitude`, `Longitude` ou `Motif`.

⚠️ **Les `GET` seulement, et c'est de fond.** Une écriture renvoie ce que l'appelant
vient de fournir : `POST /api/agents` rend l'`AgentDto` qu'on lui a donné, et le
journaliser consignerait « a lu les données qu'il a lui-même écrites ». Ce que la
restriction laisse passer, et qu'il faut savoir : un futur `POST` rendant des données
d'autrui — une recherche par POST, un export — échapperait au filet.

⚠️ La règle est **structurelle et à défaut fermé** : un DTO créé demain avec un `Nom`
tombe dans le filet sans que personne y pense, et s'en dispenser demande de l'inscrire
dans une liste d'exemptions motivées. Six y figurent aujourd'hui, et leurs raisons
diffèrent :

| Exempté | Raison |
|---|---|
| `ClientDto`, `SiteDto` | coordonnées d'une **personne morale** — entreprise, syndic, copropriété |
| `ProfilDto` | l'identité de **l'appelant lui-même** ; un journal de « qui a lu son propre nom » est du bruit |
| `ProduitDto` | le `Nom` d'un produit est « Détergent sols » |
| `EmplacementDto` | le `Nom` d'un dépôt ou d'un véhicule ; nommer par la plaque, jamais par le conducteur, est à dire à l'écran |
| `VacationAgentDto` | la journée de **l'appelant lui-même**, comme `ProfilDto` ; l'`Adresse` qu'elle porte est celle d'un site |

⚠️ **Les anomalies et leurs photos ne sont pas journalisées, et le filet ne le sait pas.**
`AnomalieDto` porte une `Description` en texte libre, que le filet ne cherche pas — il ne
connaît que `Motif` — ; `LirePhoto` rend des octets, sans DTO à inspecter. L'absence de
journal est donc une décision écrite, en `RGPD.md` §8.4, avec la condition qui la ferait
tomber : relire cette page avant d'ajouter à une anomalie un champ qui nomme quelqu'un.

⚠️ `ClientDto` reste discutable : le contact d'une petite copropriété est souvent une
personne physique. La question appartient au lot de la cartographie des données ; elle
est consignée dans le test, pas tranchée. Et `ProduitDto` montre le prix de la règle —
`Nom` est un signal grossier. Le raffiner ne marche pas : exiger `Prenom` manquerait
`AbsenceDto`, qui ne porte que `Motif` ; exiger `Nom` **et** `Adresse` rattraperait
`ClientDto`. Une règle grossière dont chaque exception est écrite vaut mieux qu'une
règle fine qui laisse passer en silence.

Un second test garde l'excès symétrique — les dix-sept actions journalisées sont
exactement celles attendues, parce qu'un journal bruyant ne se lit plus.

**`Divulgation` et `Compter` sont `public`, pour être éprouvées directement.** Même
raison que l'extraction de `reference/valeurs.ts` côté écran (§11) : c'est la seule
partie du filtre qui écrit une donnée **fausse** au lieu de ne rien écrire, et un
compte erroné ne lève aucune erreur. Plusieurs de leurs branches gardent des formes
qu'aucune action ne produit aujourd'hui — notamment qu'une `string`, étant un
`IEnumerable<char>`, vaut **une** ligne et non cent trente-quatre. Trois sabotages y
sont restés verts avant que ces tests existent.

**Coût assumé** : une insertion synchrone par lecture sur ces douze endpoints.

⚠️ **Dix-sept attributs pour douze lectures** : deux sont les `Anonymiser`, de l'agent et
du compte, qui ne sont pas des lectures mais des **écritures irréversibles** — nature
`Anonymisation` ; le troisième est la réinitialisation du second facteur d'un autre
compte — nature `ReinitialisationSecondFacteur` ; les deux derniers sont la fermeture et
la réouverture d'un compte — natures `Desactivation` et `Reactivation`. Et
`Dossier` porte la nature `Export` plutôt que `Lecture`, parce que sortir le dossier
complet d'un salarié n'est pas le même geste que consulter sa fiche : les confondre
ferait disparaître l'export dans le bruit des consultations.

### Le dossier d'un agent

`GET /api/agents/{id}/dossier`, administrateur seul, journalisé en `NatureAcces.Export`.
Il sert l'accès de l'article 15 et la portabilité de l'article 20 ; le détail de son
contenu et de ses exclusions est en `RGPD.md` §6. Quatre choses à savoir avant d'y
toucher :

- ⚠️ **Il est borné autant que complet, et les bornes sont testées sur le corps brut.**
  Ni empreinte de mot de passe, ni `SecurityStamp`, ni identité des personnes ayant
  consulté la fiche — seulement leur nombre par nature d'accès, parce que les lignes du
  journal nomment des **collègues** et que l'article 15(4) écarte l'atteinte aux droits
  d'autrui. L'assertion porte sur la réponse sérialisée et non sur le DTO : une propriété
  ajoutée demain n'apparaîtrait dans aucune assertion typée, et partirait pourtant.
- ⚠️ **Les interventions y portent le nom du site et sa ville, pas des identifiants.**
  L'article 15 exige une forme intelligible, et une liste de `PrestationId` en UUID ne
  dit rien à un salarié qui demande où il a travaillé. C'est la seule raison pour
  laquelle `InterventionDuDossierDto` existe à côté d'`InterventionDto`.
- ⚠️ **Chaque vacation porte son passage** depuis `RGPD.md` §9.8 : ses pointages, sa
  checklist déclarée et ses anomalies, rangés sous elle. **Les photos n'y sont que
  comptées, et qui a traité une anomalie n'y figure pas** : une photo peut montrer un
  tiers, et qui a traité est un collègue — l'article 15(4), comme pour les lecteurs de la
  fiche. Une table ajoutée au passage demain manquerait au dossier sans qu'aucun test ne
  rougisse : c'est ainsi que ces trois-là avaient manqué.
- ⚠️ **Le compte lié est cadré à la main sur `EntrepriseId`**, `AspNetUsers` n'étant pas
  filtrée (§7). Le test construit le compte intrus **directement en base**, sous les
  validations qui le rendraient incréable par l'API — même raison qu'au lot B de
  l'étape 17.

### Les cinq scripts de conservation

`RGPD.md` §5 porte les durées ; `backend/Scripts/` porte les gestes. **Trois seulement
suppriment**, et la distinction est dans le script lui-même :

| Script | Durée | Opération |
|---|---|---|
| `purge-journal-acces.sql` | 12 mois | `DELETE` décommentable |
| `purge-jetons-rafraichissement.sql` | 30 jours après mort du jeton | `DELETE` décommentable |
| `purge-pointages.sql` | 1 an | rapport, puis `DELETE` sur `-v confirmer=1` — ordonnançable sans modification |
| `conservation-donnees-metier.sql` | 3 mois à 5 ans selon la table | **rapport seul**, `DELETE` commenté |
| `degradation-absences-maladie.sql` | 3 mois | **rapport seul**, `UPDATE` commenté |

⚠️ **Dans un script SQL de conservation, une borne s'écrit `date < now() - interval`, jamais
`now() - date > interval`.** Les deux ont l'air équivalentes. La seconde compare deux
intervalles, et PostgreSQL y compte un mois pour trente jours : « plus d'un an » y commence
à 360 jours. Rencontré en éprouvant `purge-pointages.sql`, sur la requête de contrôle.

⚠️ Les deux derniers ne suppriment rien tant qu'un humain ne décommente pas, et ce n'est
pas de la timidité : les durées de `RGPD.md` §5 sont des propositions non validées
juridiquement, et une purge de données métier est irréversible.

⚠️ **L'ordre des `DELETE` est une contrainte, pas une préférence de présentation.** Six
clés de la chaîne d'exploitation sont en `Restrict`, deux de plus sur l'agent —
`Intervention.AgentId` et `Remplacement.AgentRemplacantId` —, et trois sur la vacation
depuis l'étape 25 : pointages, checklist, anomalies. Le `DELETE` des vacations à cinq ans
porte donc trois `NOT EXISTS`, et une anomalie encore ouverte retient la sienne. Un `DELETE` dans le mauvais
ordre échoue **à mi-parcours**, après avoir supprimé ce qui précédait. D'où la colonne
`supprimable_maintenant` du rapport, qui distingue une ligne hors délai d'une ligne hors
délai mais bloquée par un enfant encore dans le sien : le script est à **rejouer**, pas
à forcer.

⚠️ **La dégradation des arrêts maladie n'est pas une suppression, et c'est le point.**
`Remplacement.AbsenceId` est en `Cascade` : supprimer l'absence emporterait les
remplacements qu'elle a déclenchés. L'`UPDATE` vers `Indisponibilite` conserve l'agent,
les dates et toute la chaîne — ce dont l'historique a besoin est **qu'il y avait un
trou**, pas pourquoi. Vérifié en base, dans une transaction annulée : l'`UPDATE`
préserve le remplacement que le `DELETE` équivalent détruit.

### Pseudonymisation d'un agent

`POST /api/agents/{id}/anonymiser`, administrateur seul, irréversible, journalisée en
`NatureAcces.Anonymisation`.

**Le conflit qu'elle résout.** `Restrict` sur `Intervention.AgentId` rend un agent
impossible à supprimer dès qu'il a travaillé. C'est le bon choix pour l'intégrité du
planning — une suppression emporterait des vacations réalisées, donc la preuve de
passages facturés — et c'est incompatible avec une demande d'effacement. Remplacer les
données identifiantes en conservant la ligne satisfait les deux.

⚠️ **C'est une pseudonymisation, article 4(5), et non une anonymisation, article 4(1).**
L'`Id` survit puisque les interventions en dépendent : la ligne reste rattachable à une
personne par quiconque détiendrait une correspondance externe. La route s'appelle
`/anonymiser` parce que c'est l'intention de l'exploitant ; le registre des traitements
devra être exact. Ne pas confondre les deux dans la documentation juridique.

**La personne est nommée en trois endroits, et n'en traiter qu'un donne un effacement
qui n'efface pas :**

| Où | Ce qui est fait |
|---|---|
| `Agents` | nom et prénom remplacés, courriel, téléphone, adresse et coordonnées à `null`, `Actif = false`, `DateAnonymisation` posée |
| `AspNetUsers`, si un compte est rattaché | identité remplacée, adresse substituée sur un domaine non routable, `Actif = false`, `SecurityStamp` régénéré, sessions révoquées |
| `Absences` | le `Motif` en texte libre effacé — la seule donnée non structurée du modèle |

⚠️ **Les coordonnées passent à `null`, jamais à `0`.** Un zéro passe le
`[Range(-90, 90)]`, ne lève rien, place l'agent dans le golfe de Guinée et fausse
silencieusement toutes les notes de distance ; `null` donne la note neutre de 50, qui est
la réponse honnête à « on ne sait plus où il habite » (§8).

⚠️ **Le `SecurityStamp` est ce qui périme le jeton d'accès**, pas `Actif = false` seul —
celui-ci laisserait quinze minutes. Et révoquer le jeton de rafraîchissement ne suffit
pas non plus : les deux gestes sont nécessaires, pour la raison exposée en §10.

⚠️ **`AspNetUsers` n'étant pas filtrée par le multi-tenant (§7), le filtre sur
`EntrepriseId` est écrit à la main dans le service, et il est obligatoire.** Il est
aujourd'hui une défense en profondeur : `POST /api/auth/comptes` vérifie déjà que
l'`AgentId` appartient à l'entreprise de l'appelant, donc un compte portant l'`AgentId`
d'un autre tenant n'est pas créable par l'API. Le retrait de cette vérification rendrait
le filtre indispensable — et le test qui l'éprouve construit donc le compte intrus
**directement en base**, sous les validations : une défense en profondeur qu'aucun test
n'exerce est une ligne qu'on peut retirer sans rien casser, ce qu'un sabotage a montré.

⚠️ **Les trois gardes du compte se couvrent l'une l'autre, et un test par l'API ne les
distingue pas.** `OnTokenValidated` refuse une requête dont le compte est inactif **ou**
dont l'empreinte a changé ; et la connexion échoue par la seule substitution de
l'adresse. Retirer `Actif = false`, ou le `SecurityStamp`, ou le remplacement du nom,
laissait donc la série verte — trois sabotages passés inaperçus. Ce qui les attrape est
une relecture **en base** du compte après l'opération, champ par champ. Même leçon
qu'au §10 sur le témoin de déconnexion totale : quand deux gardes couvrent le même cas,
aucune n'est éprouvée tant qu'on ne les sépare pas.

⚠️ Et une assertion sur un champ **déjà vide** ne vérifie rien : `Assert.Null(PhoneNumber)`
passait quoi qu'il arrive, le compte de test étant créé sans téléphone. Il faut poser la
valeur avant de vérifier qu'elle disparaît.

**Le compte rendu énumère ce qui subsiste**, et c'est sa moitié la plus utile : qui
répond à une demande d'effacement doit savoir ce qu'il peut promettre. Y figurent
l'identifiant technique, les interventions passées, et — dit en toutes lettres — que les
absences de type `Maladie` conservent leur type et leurs dates même après effacement du
motif, ce qui **reste une donnée de santé**. La supprimer demanderait d'effacer
l'absence, ce qui casserait l'historique des remplacements qui en découlent : c'est une
question de durée de conservation, pas de pseudonymisation.

**Une seconde tentative est refusée en 409 en disant la date.** Sur un geste
irréversible, une réussite silencieuse masquerait une erreur d'identifiant. C'est aussi
pourquoi `DateAnonymisation` est une date et non un drapeau : elle répond à la question
du registre — quand cet effacement a-t-il été exécuté.

⚠️ **Jamais la fiche de l'agent rattaché au dernier administrateur actif**, et c'est le
seul endroit du produit où cette garde est atteignable. `POST /api/auth/comptes` exige un
`AgentId` pour le rôle `Agent` et ne l'interdit à aucun autre : un compte
`Administrateur` peut donc porter une fiche agent, ce qui est le cas normal dans une
entreprise de cinq personnes où le responsable fait aussi le ménage. Anonymiser cette
fiche neutralise son compte, et l'entreprise se retrouve sans aucun compte capable d'en
créer un — `POST /api/auth/comptes` étant réservé à l'administrateur, il n'existe aucun
rattrapage par l'API.

### Pseudonymisation d'un compte sans fiche agent

`POST /api/auth/comptes/{id}/anonymiser`, administrateur seul, irréversible, journalisée
en `RessourceAccedee.Compte` / `NatureAcces.Anonymisation`. C'est le dernier chemin
d'effacement du produit : un compte `Responsable`, `Administrateur` ou `ClientSyndic`
porte `AgentId = null`, donc la pseudonymisation d'agent ne le touchait pas, et aucune
route ne permettait d'effacer ses données — ni même de le désactiver, ce que
`/desactiver` fait depuis l'étape 24 (§6).

Mêmes gestes que sur le compte d'un agent — identité remplacée, adresse substituée sur
`anonymise.invalid`, `Actif = false`, empreinte régénérée, sessions révoquées — plus
`DateAnonymisation`, posée par les **deux** chemins pour que le registre réponde quel que
soit celui qui a servi.

**Quatre refus en 409, et chacun nomme sa sortie.** Déjà anonymisé en disant la date ;
rattaché à un agent, en donnant la route exacte de l'agent, parce qu'elle seule traite
les trois endroits où la personne est nommée ; son propre compte ; et le cas du dernier
administrateur, traité sur l'autre route. Un test vérifie que chaque sortie nommée est
franchissable.

⚠️ **Ce qui subsiste est l'exact symétrique des interventions d'un agent.**
`AccesJournalise` attribue chaque lecture de données personnelles à un `UtilisateurId` :
supprimer le compte rendrait illisible le journal qui existe pour rendre des comptes, y
compris les lignes documentant les accès d'autres personnes. C'est pourquoi l'identifiant
survit, donc pourquoi c'est une pseudonymisation.

⚠️ **Une garde inatteignable ne fait rougir aucun sabotage, parce qu'elle n'a aucun cas à
traiter.** J'avais posé le refus « dernier administrateur » sur cette route : l'appelant
étant nécessairement un administrateur actif, si la cible en est un autre, ils sont deux
— la garde ne pouvait se déclencher que pour cible = appelant, cas que le refus de
s'effacer soi-même traite déjà. C'est donc **ce** refus qui porte l'invariante « une
entreprise conserve toujours au moins un administrateur actif ». Leçon générale : un
sabotage resté vert ne dit pas toujours qu'un test manque — parfois il dit que le code
est mort.

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

4. **`EstEligibleAsync` ne calcule pas la note d'expérience.** L'éligibilité est
   l'appartenance à la liste ; la note ne fait qu'ordonner. Sa requête, les passages
   antérieurs sur le site, coûtait à elle seule 30 des 47 secondes d'une génération
   de 8 850 vacations, et son coût croît avec l'historique. Les filtres durs restent
   ceux de la liste, sans copie. Les scores calculés sur ce chemin sont faux et n'en
   sortent jamais.

Pas de drapeau de forçage. Une échappatoire qui existe devient le chemin normal
dès la première fois qu'elle dépanne.

### La génération des vacations

`POST /api/planning/generer { jusquAu }` est le **troisième chemin qui écrit un agent**,
après le CRUD et le cycle de remplacement. Il obéit à la même règle : le titulaire de la
prestation n'est affecté que si `EstEligibleAsync` l'accepte. Sinon la vacation est créée
sans agent, et le rapport en donne la raison. Les arbitrages et leurs motifs sont dans
`SPEC.md`, étape 20.

Quatre choses à savoir avant d'y toucher :

- ⚠️ **Chaque vacation pourvue est écrite aussitôt**, dans la transaction. Le moteur lit
  la base : une affectation encore en mémoire ne compterait ni comme chevauchement ni
  comme repos pour la vacation suivante du même agent. L'ordre chronologique de la
  boucle est donc une condition, pas un confort d'affichage.
- ⚠️ **Un verrou consultatif par entreprise** sérialise deux générations simultanées.
  L'idempotence se lit avant d'écrire, elle ne protège pas d'un double clic. Aucune
  contrainte d'unicité ne peut le remplacer : deux vacations d'une même prestation le
  même jour sont légitimes en saisie manuelle.
- **Elle n'ajoute que.** Une vacation existante n'est jamais modifiée ni supprimée,
  et une vacation **annulée** occupe son créneau : la génération suivante ne la recrée
  pas. Ce qui ne correspond plus à sa prestation est signalé comme orpheline, et laissé
  tel quel.
- **Son rapport ne nomme aucun agent.** La cause d'une vacation non pourvue mêle les
  quatre filtres durs, comme le refus de l'affectation : elle ne divulgue donc pas une
  absence, et la route n'a pas à être journalisée (§7 bis).
- **« Aujourd'hui » et les jours fériés sont ceux du territoire de l'entreprise**
  (`Territoires`, `JoursFeries`). ⚠️ C'était le jour UTC jusqu'au 4 novembre 2026 : à
  Paris entre minuit et deux heures, la génération partait de la veille. La prévision du
  stock lit le même jour. Les fuseaux sont résolus **au démarrage** : un serveur sans
  `tzdata` refuse de démarrer plutôt que de répondre 500 à la première génération.
  Un territoire posé après coup ne supprime rien : le férié déjà généré devient une
  orpheline.

---

## 9. Cycle de remplacement

Machine à états stricte, gardée côté service :

```
Recherche ──proposer──► Propose ──accepter──► Accepte
    ▲                      │
    └──────refuser─────────┘
Refuse ──proposer──► Propose
```

**Qui le conduit.** Ouvrir, proposer, accepter et refuser sont ouverts aux trois rôles
internes depuis l'étape 19. Le chef d'équipe pourvoit les vacations de **son équipe**,
parmi **ses agents**, et c'est la frontière de §7 qui le cadre, pas une vérification
écrite dans le service. Créer, modifier et supprimer un remplacement à la main restent à
`Exploitation`.

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
- `OuvrirAsync` / `POST /api/interventions/{id}/remplacement` rend le
  remplacement actif d'une vacation, ou l'ouvre en `Recherche` s'il n'y en a pas :
  **200** dans le premier cas, **201** dans le second, même corps. C'est l'entrée
  de l'écran de planning, qui part d'une vacation et ignore si un remplacement
  existe — celui d'un agent absent a été posé par `DetecterImpactsAsync`, celui
  d'une vacation jamais affectée n'existe pas.

  L'unicité repose sur `StatutsActifs`, la même liste que la détection d'impacts :
  deux règles d'unicité finiraient par diverger. Un remplacement `Refuse` ou
  `Annule` est de l'historique et n'empêche pas d'en ouvrir un nouveau.

  ⚠️ L'existant est cherché **avant** le contrôle de statut de l'intervention. Un
  remplacement déjà ouvert doit rester consultable même si la vacation a été
  annulée entre-temps ; l'ordre inverse cacherait des données au lieu de refuser
  une création.
- `AccepterAsync` **revérifie** l'éligibilité avant d'écrire : entre la
  proposition et l'acceptation, une absence a pu être saisie.

  ⚠️ **Son échec laisse un cul-de-sac, et l'appelant doit le défaire.** Le
  remplacement reste en `Propose`, et `ProposerAsync` n'accepte que `Recherche` ou
  `Refuse` en entrée : plus aucun agent ne peut être proposé sur cette vacation,
  définitivement. Tout code qui enchaîne proposer puis accepter doit appeler
  `refuser` sur l'échec, ce qui ramène en `Recherche`, avant de remonter l'erreur.
  C'est ce que fait `src/pages/Planning.tsx`, et la seule raison pour laquelle
  `refuser` figure dans son enveloppe d'API alors que l'écran n'a pas de bouton
  de refus.
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

Pas de mode « remplacement automatique » : sans notifications, il n'y a personne à
contacter. *Ma journée* (étape 25) ne change rien à cela : elle montre à l'agent ses
vacations du jour, elle ne lui propose rien et ne lui demande aucune réponse.

---

## 9 bis. Stock

Numérotée « bis » pour la même raison que §6 bis. Les arbitrages et leurs motifs sont
dans `SPEC.md`, étape 22.

**Une quantité ne change que par un mouvement.** `ServiceStock.EnregistrerAsync` est la
seule écriture de `Stock.Quantite` : `/api/stocks` est en lecture seule, et
`/api/mouvements-stock` n'a ni `PUT` ni `DELETE`. Une erreur se corrige par un mouvement
de plus, en général un inventaire. Quatre choses à savoir avant d'y toucher :

- ⚠️ **Un mouvement refusé n'écrit rien**, ni le mouvement ni une quantité. Tout se joue
  dans une transaction, et une ligne de stock créée pour la destination n'est
  enregistrée qu'avec le mouvement qui la justifie.
- ⚠️ **Un verrou consultatif par entreprise sérialise les mouvements**, espace 22, forme
  à deux entiers. Sans lui, deux sorties simultanées liraient la même quantité, et la
  seconde écraserait la première. La contrainte « quantité ≥ 0 » ne le remplace pas :
  elle voit un nombre négatif, pas une écriture perdue.
- **Trois gardes de base sous le service** : un lieu et un seul par stock, une quantité
  jamais négative, une ligne par produit et par lieu. `StockTests` les éprouve par SQL
  direct, sous le contrôleur.
- **Rien ne se supprime dès qu'un mouvement le cite** : ni produit, ni emplacement, ni
  site. Produit et emplacement se **désactivent** — ils ne reçoivent plus rien, et ce
  qu'ils portent peut encore sortir. Un site n'a pas d'état actif ; sans contrat, il ne
  génère plus rien, et le refus le dit plutôt que de promettre une sortie qui ne
  débloquerait rien.

**La prévision lit, elle ne déduit rien.** `GET /api/stocks/prevision?semaines=4` compte
les vacations `Planifiee` et `EnCours` d'aujourd'hui à quatre semaines, les multiplie par
la consommation de leur type (`ConsommationType`), et compare au stock. Aucune quantité ne
baisse au passage d'un agent. La certification existe depuis l'étape 25 (§9 ter), mais la
déduction en est **restée dehors**, délibérément : elle se cadrera à part. Quatre choses à
savoir avant d'y toucher :

- ⚠️ **Le stock d'un site ne couvre que ce site.** Chaque site puise d'abord dans le sien ;
  ce qu'il lui manque, la **réserve** — dépôts et véhicules — doit le couvrir.
  Additionner tout le stock laisserait l'excédent d'un site masquer le manque d'un autre.
  Le seuil d'alerte, lui, se lit tous lieux confondus.
- ⚠️ **`Exploitation` seule**, alors que le chef d'équipe lit le stock. Ses vacations sont
  filtrées sur son équipe (§7), le stock ne l'est pas : sa prévision comparerait le besoin
  d'une équipe au stock de l'entreprise, et annoncerait une couverture fausse.
- **Une vacation sans type ne consomme rien**, et la réponse en donne le nombre : un besoin
  sous-estimé en silence ressemble à un stock suffisant.
- **Un produit désactivé sort de la prévision.** Il ne se réapprovisionne plus ;
  l'annoncer en couverture courte pousserait à racheter ce qu'on a décidé d'arrêter. Pour
  la même raison, on ne lui attache plus de consommation.

## 9 ter. Passage de l'agent

Numérotée « ter » pour la même raison que §6 bis. Les arbitrages et leurs motifs sont dans
`SPEC.md`, étape 25 ; le traitement, dans `RGPD.md`, T7.

**Le passage prouve une présence, et ne garde aucune position.** `ServicePassage` porte
`GET /api/interventions/ma-journee`, `POST …/demarrer` et `POST …/terminer`, rôle `Agent`
seul. Chaque geste reçoit la position du téléphone, ou son absence ; le serveur en déduit la
distance au site, tranche la preuve, l'écrit dans `Pointages`, puis **jette les
coordonnées**. Deux pointages `SurSite` donnent `Terminee`, tout autre couple
`NonCertifiee`. Cinq choses à savoir avant d'y toucher :

- ⚠️ **Aucune colonne de `Pointages` ne peut porter une position**, et un test fige la liste
  de ses colonnes. Ajouter une latitude « pour le diagnostic » changerait le traitement, sa
  durée et l'AIPD : c'est une décision de registre, pas de code.
- ⚠️ **La preuve se tranche dans cet ordre** : hors du rayon même en accordant la marge
  d'erreur, sinon imprécise au-delà de 100 m, sinon sur site dans 200 m. Inverser les deux
  premiers ferait dire « imprécise » d'un point à dix kilomètres. Rayon et précision sont
  des constantes, à éprouver sur le terrain avant d'en faire un réglage.
- ⚠️ **Seul l'agent affecté pointe, et le cadrage est écrit deux fois** : par le filtre
  global des vacations, et à la main dans le service. Retirer l'un laisse la série verte,
  retirer les deux la fait rougir ; les tests ont donc été séparés pour que chacun soit
  éprouvé seul — même leçon qu'au §7 bis sur les gardes qui se couvrent.
- **`Terminee` et `EnCours` ne naissent que du pointage.** `POST` et `PUT` ne posent que
  `Planifiee`, `Annulee` et `NonCertifiee` — « fait, sans preuve ». Une vacation pointée est
  **figée** : `PUT` et `DELETE` la refusent en 409, et la base en `Restrict` sous eux.
- **La checklist est un compte rendu, jamais une condition.** *Terminer* reçoit
  `pointsFaits` ; chaque point actif du type est enregistré, fait ou non, avec **son libellé
  du moment**, et un point étranger fait refuser toute la fin avant la moindre écriture.

**Les anomalies et leurs photos.** Signalées par l'agent affecté pendant le passage, listées
pour les rôles internes, « Traitée » une seule fois. Trois photos au plus, en corps brut
`image/jpeg`, sous le plafond d'un mégaoctet.

⚠️ **`MetadonneesJpeg` est la garde, et l'écran n'en est qu'une minimisation.** Le
réencodage par le canevas ne recopie aucune métadonnée, mais une requête forgée n'y passe
pas : le serveur retire APP1 à APP15 et les commentaires, en lisant aussi les données de
l'image, parce qu'un EXIF peut se glisser entre deux balayages d'un JPEG progressif. Ce qui
n'est pas un JPEG lisible est refusé. Le test part d'une photo qui porte une position et la
cherche **en base**.

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

**`JwtBearerEvents.OnMessageReceived` ne ferme pas l'en-tête `Authorization`.**
Poser `contexte.Token` depuis un cookie ne suffit pas : si la valeur est vide ou
nulle, `JwtBearerHandler` **retombe sur l'en-tête `Authorization`** juste après.
Un délégué qui lit un cookie absent laisse donc le chemin `Bearer` grand ouvert,
silencieusement. Il faut court-circuiter explicitement :
```csharp
if (string.IsNullOrEmpty(jeton)) { contexte.NoResult(); return Task.CompletedTask; }
```
Trouvé par le test `L_entete_Authorization_n_est_plus_accepte`, après avoir
affirmé l'inverse.

**Extensions d'import : Vite résout, Node non.** Le code applicatif écrit
`import … from '../types/auth'` sans extension, ce que Vite résout et ce que
`node --test` **refuse** — `ERR_MODULE_NOT_FOUND`, sur un fichier qui existe. Tout
module chargé par une série de tests doit donc porter l'extension `.ts`
explicitement, **et tous ceux qu'il importe à son tour**. `allowImportingTsExtensions`
est déjà activé dans les trois projets TypeScript, l'extension explicite ne coûte
rien côté Vite.

Le cas ne s'était jamais présenté avant l'étape 16 bis parce que les deux modules
éprouvés — `utils/dates.ts` et `api/client.ts` — n'importaient aucun autre module
applicatif. Le premier qui l'a fait, `layout/sections.ts`, a échoué immédiatement.
Le message d'erreur ne dit pas que l'extension manque, il dit que le module est
introuvable : la fausse piste naturelle est le chemin, pas l'extension.

⚠️ Conséquence à garder en tête avant d'ajouter une série : rendre un module
testable peut demander d'ajouter l'extension sur toute une chaîne d'imports.
Convertir l'ensemble du code applicatif aux extensions explicites supprimerait la
classe entière de surprises « marche sous Vite, échoue sous Node » — ce n'est pas
fait.

**Dates en JavaScript : deux pièges symétriques, et une intégration continue en
UTC est aveugle aux deux.** Le serveur envoie les jours à minuit **UTC**
(`2026-09-07T00:00:00Z`), et l'écran raisonne en heure locale.

- `date.toISOString().slice(0, 10)` sur un minuit **local** convertit en UTC
  d'abord : à Paris en septembre (UTC+2) il rend le **6**. Faux à tout décalage
  **positif**. `versChaineIso` lit les composantes locales à la main.
- `new Date('2026-09-07')` — ou `new Date(horodatageDuServeur)` suivi de
  `getDate()` — est lu comme de l'UTC, donc tombe la veille au soir à tout
  décalage **négatif** : à Los Angeles, la grille commence le **6**. `jourIso`
  extrait la sous-chaîne, `depuisChaineIso` construit une date locale.

Les deux sabotages ont été joués. Le premier donne dix assertions rouges à Paris
et **zéro en UTC comme en Amérique** ; le second décale les cinq colonnes du
planning en Amérique et **ne montre rien à Paris ni en UTC**. Une série de tests
qui ne tourne que sous `TZ=UTC` valide donc un code faux dans les deux sens.
Éprouver `frontend/src/utils/dates.ts` dans plusieurs fuseaux, pas seulement
celui de la machine.

**Cookies `Secure` et `http://localhost` au navigateur.** Les trois cookies de
session portent `Secure`. Chrome (depuis la 89) et Firefox traitent `localhost`
comme une origine sûre et les acceptent en clair ; **Safari non**, il les jette
sans rien dire. Le symptôme est déroutant et n'accuse jamais le cookie : la
connexion **réussit** — le profil vient du corps de la réponse —, la navigation
**fonctionne** — elle est en mémoire —, et le premier rechargement de page
déconnecte, parce que `GET /api/auth/moi` est le premier appel qui exige
réellement le retour d'un cookie. Chercher le défaut dans `AuthContext` est la
fausse piste naturelle.

Diagnostic sans outils de développement : la journalisation met
`Microsoft.AspNetCore` à `Warning`, donc aucune trace de requête, **mais les
requêtes SQL sont là**. Un appel authentifié laisse forcément un
`SELECT … FROM "AspNetUsers" … WHERE a."Id" = @p` — la vérification
`Actif`/`SecurityStamp` de chaque requête. Son absence prouve qu'aucun cookie
n'est arrivé ; l'absence de lecture de `JetonsRafraichissement` prouve que le
cookie de rafraîchissement n'est pas arrivé non plus.

**Réglé à l'étape 16 : le serveur de développement sert la page en https.**
`frontend/vite.config.ts` lit le certificat de développement .NET exporté en PEM
dans `frontend/certs/`, non versionné. Voir le README, section *Certificat de
développement*.

Deux choses à ne pas refaire, parce qu'elles ont été essayées :

- **Rendre l'attribut `Secure` dépendant de l'environnement**, dans
  `ServiceSession`. Cela oblige à affaiblir `SessionTests`, donc à supprimer la
  seule garde qui protège cet attribut, pour un confort de développement. Un
  drapeau conditionnel sur un attribut de sécurité finit à `false` en production.
- **Retirer l'attribut au passage du proxy**, ce qu'a fait ce fichier entre les
  étapes 13 et 16. Cela masque l'effet sans traiter la cause, et fait différer le
  développement de la production exactement là où c'est le plus coûteux. Au
  passage : le faire par `replace(/secure/gi, '')` mutile une valeur de cookie
  contenant « insecure » et un chemin nommé `/secure` — il fallait découper sur
  `;` et comparer chaque attribut exactement.

Le saut proxy → backend reste **en clair**, et c'est correct : ce qui décide du
sort d'un cookie `Secure` est l'origine que voit le navigateur, et ce saut-là est
en boucle locale sur la même machine.

**Cookies `Secure` et `CookieContainer` en test.** `CookieContainer` respecte
l'attribut `Secure` : sur une adresse de base en `http`, il stocke les cookies et
ne les renvoie **jamais**. Toute la série échoue alors en 401 sans que rien
n'indique pourquoi. La fabrique de test doit donner une `BaseAddress` en
`https` — `TestServer` ignore le transport, le schéma ne coûte rien.

**Une cascade décidée et gardée par un test reste une cascade.** Jusqu'à l'étape
16 bis, toute la chaîne du modèle était en `DeleteBehavior.Cascade` : mesuré contre
le serveur, un `DELETE /api/clients/{id}` sur un client portant un site effaçait le
client, le site, son contrat, sa prestation **et** son intervention, en un appel,
avec un **204**. Un test nommé
`Suppression_client_emporte_ses_sites_en_cascade` l'affirmait et passait — ce
n'était donc pas un oubli, c'était un choix documenté.

Ce qui a changé n'est pas la lecture du risque, c'est le **contexte** : l'étape 16 bis
a fait de ce `DELETE` un bouton dans une liste, là où il n'existait que dans Swagger.
Corrigé par la migration `SuppressionRestreinteSurLaChaineExploitation`, qui pose
`Restrict` sur les six clés de la chaîne d'exploitation. Trois choses à en retenir :

- **La règle du modèle tient en une phrase, et elle se lit dans le `DbContext` :**
  *aucune suppression n'efface en silence une ligne que l'utilisateur gère sur son
  propre écran.* Elle donne elle-même ses deux exceptions — le rattachement au
  tenant, qu'aucun endpoint n'expose depuis le retrait de `DELETE /api/entreprises`,
  et `Remplacement`, qui est de l'état dérivé qu'aucun écran ne liste.
- **Un `Restrict` dont le refus ne nomme pas la sortie est un cul-de-sac.** Les six
  contrôleurs concernés comptent leurs enfants et disent par où passer. La friction
  n'a de valeur que si elle est franchissable ; un sabotage qui vidait le message de
  sa marche à suivre est passé inaperçu une première fois, parce que le test ne
  vérifiait que le compte.
- **Une parade d'interface devient un mensonge quand le fond est corrigé.** Un champ
  `cascade` des descriptions annonçait ce que la suppression emportait ; il était
  juste, puis il a cessé de l'être. Retiré. Vérifier, à chaque correction de fond,
  ce qu'elle rend faux en amont.

**La validation de modèle d'ASP.NET répond en anglais, et pas seulement sur les
mots de passe.** Le piège de §6 vaut pour **tous** les DTOs : `[Required]` et
`[MaxLength]` produisent « The field Nom must be a string or array type with a
maximum length of '200' », qui traverse l'API tel quel et qu'aucun écran ne peut
traduire. La parade côté interface est de rendre ce refus inatteignable —
`required` et `maxLength` sur la saisie, que le navigateur applique dans sa propre
langue. D'où `longueurMax` dans chaque description, et la série qui vérifie qu'un
champ de texte ne peut pas l'oublier.

**La surface HTTP ne s'éprouve pas comme le métier, et quatre choses la rendent
verte à l'envers.** Chacune a été rencontrée à l'étape 16. Le point commun : un
défaut de surface n'a **aucun symptôme fonctionnel**, donc un test mal ciblé
passe, et rien d'autre ne le rattrapera.

- **`TestServer` ignore les options de Kestrel.** Une limite posée par
  `ConfigureKestrel` — taille maximale de corps, notamment — n'existe pas sous
  `WebApplicationFactory` : la série a vu un corps de deux mégaoctets lu et lié en
  entier, puis refusé par un `[MaxLength(200)]`, c'est-à-dire **après**
  l'allocation que la limite existe pour éviter. D'où
  `Middleware/LimiteCorpsRequete.cs`, qui refuse sur `Content-Length` quel que
  soit le serveur. Kestrel reste nécessaire pour un corps en fragments, qui
  n'annonce pas sa taille ; aucune des deux gardes ne couvre le cas de l'autre.
- **HSTS exclut `localhost` par défaut**, ainsi que `127.0.0.1` et `[::1]`. Une
  assertion d'absence sur un hôte nommé `localhost` réussit donc toujours, que
  `UseHsts` soit là ou non. L'éprouver demande un autre nom d'hôte — `TestServer`
  accepte n'importe lequel.
- **`UseHttpsRedirection` ne redirige rien en silence** s'il ne connaît pas le
  port de destination : il le cherche dans les adresses d'écoute, n'en trouve pas
  sous `TestServer`, journalise un avertissement et laisse passer en clair. Poser
  `HttpsRedirectionOptions.HttpsPort` explicitement.
- **Révoquer un jeton de rafraîchissement ne périme pas le jeton d'accès.** C'est
  toute la différence entre les deux, et elle se retourne contre le test qui
  l'oublie : un témoin « l'autre compte fonctionne encore » écrit sur
  `GET /api/auth/moi` reste vert même si ses sessions ont été coupées, parce que
  son jeton d'accès est signé et vaut quinze minutes. Le témoin doit porter sur
  `POST /api/auth/rafraichir`.

**Un gris de marque n'est pas une couleur de texte, et le thème clair le cache.**
`--brand-strong` mesure **12,47:1** sur `--surface` en thème clair — irréprochable — et
**2,29:1** en thème sombre. Sur `--brand-tint` : **10,42:1** puis **2,03:1**. La cause
est structurelle : `brand` et `brand-strong` sont des **fonds**, et leurs valeurs
sombres sont claires tandis que les fonds sombres sont sombres. Tout texte peint avec
l'un d'eux s'effondre au basculement de thème, et aucune relecture de CSS ne le montre.

Le piège a été posé **deux fois dans la même séance**, au lot B de l'étape 16 bis, sur
deux liens différents — il paraît chaque fois « le ton un peu plus marqué » que
demande un lien. Les deux sont repassés en `--texte`, distingués par la graisse et le
soulignement, ce que §12 prescrit pour un niveau d'information de plus. Les quatre
paires sont désormais mesurées par `npm run verifier`, dans la liste des combinaisons
interdites : le rappel est chiffré, pas verbal.

Les seules couleurs de texte qui tiennent AA sur `brand-tint` sont `--texte` et
`--alerte` — et `--alerte` est réservée au « non pourvu » et au focus.

**Masquer un champ demande deux moitiés, et la seconde est la moins évidente.** Le
motif d'une absence `Maladie` disparaît du formulaire (`masquerSi` dans la
description), **et** `corpsDe` l'envoie à `null`. Sans la seconde : saisir un motif
puis basculer le type sur « Maladie » enverrait ce texte, le serveur refuserait en
parlant de donnée de santé, et l'écran n'afficherait plus le champ dont le refus parle
— la donnée ayant de surcroît quitté le navigateur pour rien. Et la clé doit rester
présente à `null`, pas être omise : un `PUT` qui l'omet laisse en place le motif déjà
enregistré au lieu de l'effacer. Vérifié contre le serveur.

**Une donnée personnelle ne sort pas par la route qui porte son nom.** Pour
journaliser « qui a lu les données d'agents », le réflexe est de chercher
`/api/agents`. Mesuré sur ce projet : deux endpoints de plus les divulguent, et aucun
ne porte « agent » dans son chemin. `GET /api/planning` nomme **tous** les agents de
l'entreprise et sa cause « Agent absent » révèle une absence ; les deux routes de
candidats exposent nom, prénom et la distance au domicile. Un journal construit sur
les chemins aurait eu deux trous et l'air complet.

La parade qui tient n'est pas la vigilance mais la **réflexion sur les types** : on
part des DTOs et des propriétés qui rendent une personne identifiable, pas des URL.
Voir §7 bis.

**Un délai de requête dépassé ne défait rien de lui-même.** `UseRequestTimeouts`
déclenche le jeton d'annulation de la requête, rien de plus. Un service qui ne le lit
pas va au bout, valide sa transaction, puis la réponse est coupée en cours d'écriture.
Mesuré sur la génération : un **200** au corps JSON tronqué au premier tampon, sur un
travail entièrement validé. Toute opération longue doit recevoir le `CancellationToken`
de l'action et le passer à chaque appel EF. Un dépassement défait alors la transaction,
et l'appelant reçoit un 504 sur un travail qui n'a pas eu lieu.

**Un test de concurrence sans fenêtre de course est vert à l'envers.** Deux requêtes
lancées ensemble ne se chevauchent pas forcément : si la première écrit tout en un
aller-retour, elle finit avant que la seconde ait lu. Le test de double génération est
resté vert, verrou retiré, jusqu'à ce qu'on élargisse la fenêtre. Un tel test ne vaut
qu'après un sabotage qui le fait rougir, **rejoué plusieurs fois**.

⚠️ **Et rejoué dans la série, pas seulement seul.** La fenêtre dépend aussi de la chaleur
du processus. Au lot B de l'étape 21, huit créations simultanées du même libellé se
chevauchaient quand le test tournait seul, à froid : le sabotage rougissait. Dans la
série, processus chaud, la première finissait avant que les autres lisent, et le même
sabotage restait vert.

La parade est de **provoquer** la course plutôt que de l'espérer :
1. le test écrit la ligne concurrente dans une transaction laissée ouverte ;
2. il attend que la requête soit bloquée sur l'index, ce que `pg_stat_activity` montre
   par `wait_event_type = 'Lock'` ;
3. il valide sa transaction.

Voir `TypesPrestationTests`. ⚠️ L'attente de l'étape 2 se lit depuis une
`PorteeAuProprietaire` : sous `klean_app`, `pg_stat_activity` ne montre ni la requête
ni l'attente des autres sessions, et le test attend un blocage qu'il ne verra jamais.

**Un superutilisateur ignore la RLS, même forcée.** Jusqu'à l'étape 26, l'API se
connectait en superutilisateur dans les trois environnements — le compte du système en
développement et en test, `klean`, le `POSTGRES_USER` de l'image, en production : une
politique posée alors n'aurait rien gardé, nulle part, sans aucun symptôme. D'où le
`SET ROLE` de §7, et le test qui lit `current_user` sous une requête authentifiée : un
intercepteur retiré rendrait la RLS inerte en silence. Pour la même raison, un `psql`
ouvert à la main voit **tout** — il est propriétaire. Pour voir ce que voit l'API :
`SET ROLE klean_app; SELECT set_config('klean.entreprise', '<id>', false);`.

**Une portée de test qui touche la base sous l'API passe par `PorteeAuProprietaire()`.**
Une portée ouverte par `Fabrique.Services.CreateScope()` n'a pas de requête, donc pas
d'entreprise, et le filtrage y reste actif : sa connexion passe sous `klean_app` avec un
réglage vide. Elle ne lit **aucune ligne** et toute écriture y est refusée en `42501`.
Onze portées ouvertes ainsi dans quatre classes sont restées invisibles tant que la base
ne gardait rien, puis ont fait vingt-deux échecs à la première série sous la RLS.

**Une borne de conservation s'écrit `date < now() - interval`, jamais
`now() - date > interval`.** La seconde compare deux intervalles, et PostgreSQL y compte
un mois pour trente jours : « plus d'un an » commence à 360 jours. Détail en §7 bis.

**EF ne retire pas d'un filtre global la branche d'un paramètre nul.** Un filtre écrit
`Axe == null || EXISTS (…)` ne devient pas `TRUE` quand l'axe est nul : EF émet
`(@p OR EXISTS …)` pour tout le monde, et la sous-requête reste dans le SQL. J'avais
affirmé l'inverse dans un commentaire de l'étape 19, sans regarder le SQL. Le coût reste
négligeable, mais pour une autre raison, et c'est une mesure, pas une garantie écrite :
PostgreSQL ne paie pas la sous-requête quand le premier membre du OR est vrai. Avant de raisonner sur le coût d'un filtre, lire `ToQueryString()`, puis mesurer
avec `plan_cache_mode = force_generic_plan` : Npgsql prépare les requêtes, et un plan
générique ne connaît pas la valeur du paramètre.

**Deux `dotnet test` en parallèle détruisent `klean_test`, et l'erreur n'accuse pas sa
cause.** Les deux séries partagent la base, et leurs fixtures s'y amorcent : l'une crée
les deux entreprises de test pendant que l'autre nettoie les comptes. Il reste alors des
entreprises sans leurs administrateurs, et **toutes** les séries suivantes échouent sur
`23505: duplicate key value violates unique constraint "IX_Entreprises_Siret"` — 296
échecs en moins d'une seconde, sur un code qui n'a pas changé. Le message parle de SIRET
et la cause est la concurrence.

Une seule exécution à la fois, donc — y compris en intégration continue, où deux travaux
parallèles visant la même base produiraient exactement ce symptôme. Pour s'en sortir,
vider `klean_test` entièrement, `AspNetUsers` et `Entreprises` comprises, et laisser la
fixture ré-amorcer.

**`dotnet test` en français met une espace insécable avant ses deux-points.** La ligne de
bilan s'écrit « échec : 0 » et « total : 476 » avec une espace **insécable** avant « : ».
Un `grep "total :"` ou `grep "échec :"` tapé au clavier ne la trouve donc jamais, sans
erreur. Le 29 octobre, une série saine ainsi filtrée n'a rien affiché ; quatre lignes
« Échoué » venues des sabotages lancés dans la même commande ont été prises pour
la sienne, et un « échec intermittent » a été cherché pendant dix-sept passages. Il
n'existait pas : trouvé le 4 novembre en relisant la commande. Filtrer sur `chec` ou
`total`, sans le deux-points, et lancer une série saine **seule** dans sa commande.

**Sur un parcours authentifié, un 401 n'est pas un refus : c'est une déconnexion.**
`api/client.ts` traite tout 401 comme une session morte — il rafraîchit, rejoue, puis
purge la session et prévient ses abonnés. Un endpoint authentifié qui répond 401 pour
dire « ta saisie est fausse » déconnecte donc l'utilisateur au lieu de le corriger.

Le cas concret : `POST /api/auth/changer-mot-de-passe` sur un mot de passe actuel
faux. Un 401 y aurait été le choix naturel — c'est bien un échec d'authentification —
et il renvoyait l'utilisateur à l'écran de connexion sur une faute de frappe. Il rend
400, et un test le nomme pour que personne ne le « corrige ».

La règle générale, à appliquer à tout endpoint futur : **401 est réservé à l'état de
la session, 400 à celui du corps de la requête.** Trois endpoints dérogent
délibérément et sont inscrits dans `SansRafraichissement` côté client — `connexion`,
`rafraichir`, `deconnexion` — parce que leur 401 porte justement sur la session.

**Un bouton destructeur dont l'effet inclut l'appelant demande un `finally` inversé.**
`deconnecter` purge la session locale même si l'appel échoue : rien n'est pire qu'un
bouton « Déconnexion » qui laisse l'écran connecté. `deconnecterPartout` fait
l'inverse et ne purge qu'au succès — afficher « déconnecté partout » alors que les
autres appareils sont restés ouverts est un mensonge sur un geste de sécurité, et
l'utilisateur n'a alors aucune raison de réessayer. Le même geste, deux raisonnements
opposés : ne pas recopier l'un sur l'autre.

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

**Trois pièges de l'exploitation, mesurés sur l'instance d'essai de l'étape 18.**
Détail et procédures dans `DEPLOIEMENT.md`.

- **`POSTGRES_PASSWORD` n'est lu qu'à la création du volume.** Le changer dans `.env`
  seul rend l'instance inaccessible — 502 partout. `ALTER USER` d'abord, `.env` ensuite.
- **Tourner `Jwt:Cle` ne déconnecte personne.** Le jeton de rafraîchissement est opaque
  et en base : l'interface en obtient un nouveau jeton d'accès, signé par la nouvelle
  clé, sans que l'utilisateur voie rien. Après une compromission, il faut **aussi**
  révoquer les jetons de rafraîchissement — même leçon que le témoin de déconnexion
  totale plus haut.
- **Le retour arrière d'une migration se joue avec la nouvelle image**, puis on revient
  à l'ancienne : le code `Down` n'existe que dans la version qui a introduit la
  migration.

Et un quatrième, trouvé à l'étape 26 sur un conteneur jetable : **`pg_dump` ne
sauvegarde pas les rôles**, qui vivent dans la grappe. Une sauvegarde restaurée sur une
machine neuve s'arrête sur `role "klean_app" does not exist`, après les tables et avant
les droits. Le rôle se crée d'abord ; la commande est dans `DEPLOIEMENT.md` §4.

Et un faux signal : sans `Gss Encryption Mode=Disable` dans la chaîne de connexion,
Npgsql cherche `libgssapi_krb5`, absente de l'image .NET, et écrit « Error: … cannot
open shared object file » au premier démarrage. Sans conséquence, mais c'est la première
ligne qu'un exploitant lirait.

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

  **La journalisation est une affaire de configuration, pas de code.** Depuis
  l'étape 18, `appsettings.json` écrit une ligne JSON par événement, avec le
  `TraceId` de la requête dans les portées — le même que l'en-tête
  `X-Correlation-Id` et que le champ `correlation` d'un corps d'erreur. Une ligne
  par requête, et chaque exception non rattrapée avec sa pile : c'est ce qu'il faut
  pour diagnostiquer à distance, et rien de cela ne demande un `ILogger` dans un
  service. Un appel de journal ajouté dans le métier ne doit **jamais** écrire une
  donnée personnelle — un nom, un courriel, un motif —, sinon le journal devient la
  base parallèle que le §7 bis interdit au journal d'accès.

  Deux exceptions, et deux seulement : `ITenantContext` et `IServiceCourriel`.
  Même justification dans les deux cas — la substitution est **nécessaire**, pas
  décorative. Pour le courriel, l'alternative serait une classe concrète qui
  « n'envoie pas vraiment » selon la configuration, ce qui ferait dépendre le
  comportement de production d'un réglage, sur un chemin d'authentification.
  Toute troisième interface de service doit être justifiée de la même façon.

  **`TimeProvider`**, la classe abstraite du framework, tient cette place depuis
  l'étape 20, et pour la même raison. « Douze semaines à partir d'aujourd'hui » ne
  s'éprouve pas sur un jour qui change : sans substitution, la série de génération
  dépendrait du jour où elle tourne. C'est le défaut que le paramètre obligatoire
  `lundi` du planning existe pour éviter. `ServiceGeneration` le lit, et
  `ServicePrevisionStock` depuis l'étape 22 : « les quatre semaines à venir » pose la même
  question.

  Les autres lectures de l'horloge gardent `DateTime.UtcNow`. Plusieurs décident
  pourtant : expiration des sessions et des jetons, fenêtre de la limitation de
  débit. Leurs tests raisonnent en durées relatives à l'instant présent, pas sur
  un jour du calendrier. Ils n'ont donc pas eu besoin de fixer l'horloge, et les
  convertir serait un changement sans motif.
- Ne jamais contourner les filtres multi-tenant avec `IgnoreQueryFilters()`.

**Frontend**
- Un `.tsx` et un `.css` par composant, côte à côte.
- `import type` pour les types (projet en `verbatimModuleSyntax`).
- Aucune couleur en dur : uniquement les variables de `tokens.css`.
- Trois états explicites sur tout appel réseau : chargement, erreur, données.
- Le wrapper `src/api/client.ts` ajoute le token et purge la session sur 401. Depuis
  l'étape 25, `api.postBrut` envoie un corps tel quel sous son type — une photo en
  `image/jpeg`, avec le CSRF de toute écriture —, et `api.getBlob` lit une réponse
  binaire en rafraîchissant la session sur 401, ce qu'un `<img src>` ne sait pas faire
  (§6 bis).
- Les composants de présentation reçoivent leurs données en props ; c'est la
  page qui orchestre les appels API.
- **Un écran de données de référence ne s'écrit pas à la main.** Six listes et six
  formulaires copiés-collés auraient six fois l'occasion de diverger de la matrice
  des droits, et la divergence a une forme précise : un bouton d'enregistrement qui
  répond 403. Une description dans `src/reference/descriptions.ts`, et
  `src/pages/Reference.tsx` la rend. Ajouter une ressource, c'est ajouter une
  description et une clé dans `sections.ts` — jamais un fichier de page.

  **Ce qui n'est pas une liste à formulaire a sa page**, déclarée dans `ecrans` de sa
  section : le stock et la prévision de l'étape 22, les comptes de l'étape 24. Une
  quantité ne se saisit pas, elle résulte d'un mouvement ; une prévision ne s'enregistre
  pas ; un compte ne se modifie pas, il se ferme, se rouvre ou se pseudonymise, et sa
  création rend un mot de passe à afficher une fois. Un écran propre porte
  **ses** rôles — la prévision est fermée au chef d'équipe, à qui la section reste
  ouverte —, et la sous-navigation le range après les ressources (`ongletsDe`).
  `sections.test.ts` exige que les rôles d'une section couvrent ceux de ses écrans, et
  qu'aucun chemin ne soit monté deux fois.
- **L'étiquette d'une description n'est jamais une clé étrangère.** Elle sert deux
  fois : nommer une ligne, et **libeller les options de la liste déroulante d'une
  ressource enfant**. L'écran d'une prestation charge les contrats, pas les sites : une
  étiquette `siteId` sur les contrats y afficherait une liste d'identifiants, sans
  qu'aucune erreur ne soit levée. D'où `frequence` pour un contrat et `type` pour une
  absence — imparfaits, lisibles, et gardés par une invariante.
- **Toute donnée décrite n'est pas une donnée de référence.** Les droits d'écriture
  d'une description recopient ceux du contrôleur, et les absences sont écrites par le
  `Responsable` parce que `AbsencesController` porte `RolesKlean.Exploitation`. Leur
  appliquer `EcritureReference` présenterait un formulaire en lecture seule sur la
  seule chose que l'étape 15 demande de saisir — la faute d'interface symétrique de
  celle que tout ce motif évite.
- **Un écran de référence décrit chaque champ de son DTO de création, sans exception.**
  Les `PUT` remplacent la ligne entière : un champ que l'écran n'envoie pas est remis à
  son défaut à chaque enregistrement, sans erreur. C'est arrivé entre les lots A et C de
  l'étape 20. `descriptions.test.ts` lit désormais `backend/DTOs/*.cs` et compare les
  deux listes ; ajouter une propriété à un `XxxCreateDto` demande donc d'ajouter son
  champ à la description.
- **Des propriétés déclaratives étendent le motif sans écrire de page** :
  - `action` sur une description, pour un geste de liste au-delà du CRUD ;
  - `routeChoix` sur une description, pour remplir les listes déroulantes des
    ressources enfants sans charger la ressource entière (§7 bis, `ChoixAgents`) ;
  - `preremplit` et `seulementActifs` sur un champ de référence ;
  - `libelles` sur un champ de choix : « Congé » s'affiche, `Conge` part ;
  - `roles` et `comptes` sur une colonne — la colonne « Chef d'équipe » des agents,
    réservée à l'administrateur. ⚠️ Une colonne de comptes n'est ouverte qu'à des rôles
    d'écriture : la liste des comptes n'est chargée que pour eux, et un lecteur lirait
    « Hors de votre périmètre » sur chaque ligne ;
  - `modificationSeule` sur une description : ni création ni suppression, l'API
    n'exposant que le `PUT` — l'écran Entreprise.

  Leur logique est dans `valeurs.ts`, leurs invariantes dans `descriptions.test.ts`, qui
  refuse tout nom de champ qui ne désigne rien. ⚠️ Le préremplissage ne réécrit qu'un
  champ vide ou encore à la valeur du choix précédent : l'étendre à « toujours » écraserait
  le libellé propre au site d'une prestation à chaque changement de type.
- **La logique de conversion sort du composant.** `src/reference/valeurs.ts` porte
  le passage d'une ligne du serveur au formulaire et au corps JSON, parce que c'est
  la seule partie qui écrit des données fausses au lieu d'afficher quelque chose de
  faux : une chaîne vide au lieu d'un `null`, un nombre en chaîne, un jour décalé de
  vingt-quatre heures. Enfermée dans un `.tsx`, elle n'est pas éprouvable.

  Même raison pour `src/auth/motDePasse.ts`, qui porte les deux refus que l'écran de
  compte prononce avant d'appeler : ce sont les seuls cas du parcours où le serveur
  répondrait **en anglais**, la validation de modèle d'ASP.NET s'exécutant avant
  Identity. Les retirer ne casse rien de visible — l'appel partirait, le serveur
  refuserait — et un francophone lirait « The MotDePasseActuel field is required. »
  sous un formulaire entièrement en français. Un écran qui refuse n'est éprouvable
  qu'extrait ; un écran qui affiche se relit.

  Et pour `src/auth/secondFacteur.ts` (étape 23), avec une raison de plus : côté
  serveur, un code mal formé **compte dans les cinq échecs du verrouillage**. Le refus
  local épargne un essai qui n'en était pas un.

  Et pour `src/passage/passage.ts` (étape 25) : la preuve et son bilan — « le début sans
  position, la fin hors du rayon » —, les points non cochés qui font confirmer *Terminer*,
  les refus d'une description ou d'une photo, et la demande de position elle-même, avec
  `maximumAge: 0` et un refus qui rend `null` au lieu d'échouer. La réduction d'une photo
  est à part, dans `passage/photo.ts`, parce qu'elle exige un canevas que `node --test`
  n'a pas ; ce qui s'y décide sans canevas — les côtés et les qualités essayés — reste
  dans `passage.ts`, éprouvé.

  Et pour `src/comptes/comptes.ts` (étape 24) : l'état d'une ligne, les gestes qu'elle
  propose — aucun sur sa propre ligne, que le serveur refuserait tous —, la question de
  confirmation, et le corps de la création. ⚠️ Le masquage y a ses deux moitiés, comme le
  motif d'une absence (§10) : une fiche agent choisie avant de passer au rôle « Client
  syndic » part à `null`, sinon elle suivrait un compte qui ne doit pas en porter.
- **Un QR code se dessine noir sur blanc dans les deux thèmes.** `--qr-fond` et
  `--qr-module` ne sont pas redéfinis par le bloc sombre, et c'est délibéré : un QR
  inversé n'est pas lu par toutes les applications. C'est le seul dessin du produit
  qu'une machine lit plutôt qu'un humain.
- **Un écran de compte n'est pas une section.** `layout/sections.ts` déclare
  `CheminCompte` sans le mettre dans `Sections`, et `sections.test.ts` garde
  l'absence. Une section est une fonction du produit, ouverte par rôle, et
  `premiereSectionOuverte` en fait une **destination d'arrivée** : le compte, ouvert à
  tout rôle, deviendrait l'arrivée de tous. Il vit au pied de la barre latérale,
  auprès de la déconnexion, et reste atteignable pour tout rôle.

  ⚠️ **Le rôle `Agent` a une section depuis l'étape 25**, *Ma journée* (`MJ`), qui est sa
  destination d'arrivée ; avant, il arrivait sur un refus, et le compte était le seul
  écran qu'il atteignait. *Ma journée* est **dernière** dans `Sections` : un compte qui
  porterait aussi un rôle interne arrive sur le planning. *Anomalies* (`AN`), juste après
  le planning, est aux trois rôles internes. Les deux sont des écrans propres, montés par
  `Ecrans` dans `App.tsx`, sans ressource de référence.

  ⚠️ **Après connexion, `depuis` n'est honoré que si le rôle ouvre ce chemin**
  (`destinationApresConnexion`). `RouteProtegee` le pose sur l'écran où la session a
  manqué, ce qu'une session expirée **et** une déconnexion font pareillement : honoré tel
  quel, il ramenait le compte suivant sur l'écran du précédent, et un agent arrivait sur
  « Accès refusé ». La règle qui dit qui ouvre un chemin vit dans `routesDesSections`,
  que monte `App.tsx` et que lit la connexion : elle n'est écrite qu'une fois.

  Ne pas confondre avec l'écran **Comptes**, au pluriel : celui-là gère les comptes de
  l'entreprise, est réservé à l'administrateur, et vit donc dans une section, *Entreprise*.

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
| hachure | `#D8D4CC` | — |
| sur-brand | `#FFFFFF` | `#FFFFFF` |

`sur-brand` est le texte posé **sur** un fond de marque. `surface` ne peut pas
jouer ce rôle : il vaut `#191A18` en thème sombre, et le libellé d'une pastille
active tombait alors à 3,32:1, sous le seuil AA. Le blanc tient 9,06:1 au-dessus
de `#3F4A54` et 5,27:1 au-dessus de `#5E6E7A`.

⚠️ **Il n'y a que deux niveaux de gris de texte, et `texte-3` a été retiré.**
Sa valeur mesurait `3,63:1` sur `surface` et `3,33:1` sur `canvas` en thème clair,
`4,07:1` et `4,41:1` en sombre : jamais AA, sur aucun fond du système. Elle
contredisait donc la règle « contraste AA minimum sur tout texte » quatre lignes
plus bas.

Le corriger était impossible : **toute** valeur qui tient AA sur `surface` tombe à
moins d'un demi-point de `texte-2`, et deux gris séparés par si peu ne forment pas
une hiérarchie. Un troisième niveau de gris lisible n'existe pas dans cette
palette. Un troisième niveau d'information passe par la **graisse** ou la
**taille**, comme le fait déjà `BlocVacation` sur `brand-tint`.

Pour les usages non textuels — filet, séparateur, icône éteinte — `border` et
`border-fort` sont là. Le jeton n'avait plus de rôle, et il en avait l'apparence :
deux fois pendant les étapes 13 et 15 il a paru le choix naturel pour « encore
plus secondaire », et deux fois seule une mesure l'a arrêté. Retiré, sa
réapparition devient une **erreur dure** — `npm run verifier` échoue sur tout
`var(--…)` absent de `tokens.css`.

**Typographie.** Figtree pour l'interface, IBM Plex Mono pour **tout chiffre
comparé en colonne** — heures, km, scores, quantités. La chasse fixe aligne
verticalement sans effort.

⚠️ **Servies par KLEAN, depuis `frontend/public/polices/`, et jamais par un tiers.**
Quatre fichiers, 79 Ko au total : la fonte **variable** de Figtree couvre à elle seule
les graisses 400 à 700, et IBM Plex Mono vient en trois graisses dans son
sous-ensemble **Latin1** — trois fois plus léger que la fonte complète, et suffisant
pour une interface française, `œ` et guillemets compris. Les `@font-face` sont en tête
de `tokens.css` ; la plage `unicode-range` y est recopiée des déclarations amont d'IBM
et doit y rester, sinon un caractère hors sous-ensemble s'afficherait en tofu au lieu
de retomber sur la fonte système.

Elles étaient chargées depuis Google Fonts jusqu'au 3 octobre 2026 — voir §6 bis et
`RGPD.md` §9.1. `npm run verifier` vérifie désormais que chaque `url()` du CSS pointe
sur un fichier présent, que chaque `.woff2` porte sa signature, que les deux licences
OFL sont versées, et qu'aucune origine externe ne réapparaît.

Échelle (taille / interligne / graisse) : 32/36/700 titre d'écran ·
20/26/600 titre de section · 16/24/500 corps mobile · 14/20/400 corps
desktop · 13/18/500 ligne de tableau · 12/16/500 bloc de planning ·
11/14/600 étiquette.

Plancher mobile : 16 px pour le corps, 22 px pour l'information principale.
Jamais 11–12 px sur mobile. L'information principale a son jeton depuis l'étape 25,
`--info-mobile-taille` et `--info-mobile-interligne` (22/28) : le nom du site et l'heure
d'une vacation, sur *Ma journée*, la première page du produit écrite pour un téléphone.

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
# Certificat de développement — une fois, avant le premier npm run dev
dotnet dev-certs https --trust
dotnet dev-certs https --export-path frontend/certs/localhost.pem \
    --format PEM --no-password

# Backend — laisser tourner dans son terminal
cd backend && dotnet watch run        # http://localhost:5137

# Frontend — second terminal
cd frontend && npm run dev            # https://localhost:5173

# Vérifications
cd backend && dotnet build
cd frontend && npm run lint
cd frontend && npm run verifier       # système d'interface, §12
cd frontend && npm test               # dates et session, cinq fuseaux
cd frontend && npm run build          # tsc -b && vite build
cd frontend && npm run preview        # https://localhost:4173, CSP de production

# Migrations
cd backend
dotnet ef migrations add NomDeLaMigration
dotnet ef database update

# Production, depuis la racine — marche complète dans DEPLOIEMENT.md
docker compose build
docker compose run --rm migrer        # jamais au démarrage de l'API
docker compose up -d
```

⚠️ **Les migrations construisent leur contexte par `Data/FabriqueDeConception.cs`,
sans `Program.cs`** (étape 26). `dotnet ef` et le bundle préfèrent une fabrique de
conception à l'hôte de l'application : le contexte est construit hors injection, donc
sans l'intercepteur qui met l'API sous `klean_app` — un rôle sous lequel aucune
migration ne passe, et qui n'existe pas avant la migration qui le crée. La fabrique lit
la chaîne dans l'environnement, ou dans les secrets utilisateur en développement.

Le bundle ne demande donc rien à la construction, et seulement la chaîne de connexion à
l'exécution — ni clé JWT, ni le reste de la configuration. `docker-compose.yml` la lui
passe par l'environnement et non par `--connection`, qui exposerait le mot de passe dans
la liste des processus. ⚠️ Jusqu'à l'étape 26, il exécutait `Program.cs` jusqu'à
`Build()` et échouait sur « Chaîne de connexion absente » : le `Dockerfile` lui passait
des valeurs factices, retirées depuis. Et `dotnet ef … -c` veut dire `--context`, pas
`--configuration`.

Le proxy Vite redirige `/api` vers `http://localhost:5137`. Le frontend
n'appelle donc jamais l'URL absolue du backend.

`npm test` et `npm run verifier` n'ajoutent **aucune dépendance** : Node 22.18
dépouille les types TypeScript sans drapeau et porte son propre lanceur de tests.
`npm test` rejoue `src/utils/dates.ts` dans **cinq fuseaux**, ce qui n'est pas du
zèle — voir le piège des dates en §10, dont aucune des deux moitiés ne se voit
sous `TZ=UTC`. `npm run verifier` mesure les contrastes de §12 là où l'œil ne
distingue pas 4,44:1 de 4,52:1.

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
