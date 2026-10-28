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
├── CLAUDE.md, SPEC.md, README.md, RGPD.md, LICENSE
├── backend/
│   ├── Models/          11 entités métier + AccesJournalise + Utilisateur
│   │                    + RolesKlean
│   ├── Data/            KleanDbContext (filtres multi-tenant)
│   ├── DTOs/            un fichier par entité, XxxDto + XxxCreateDto
│   ├── Controllers/     un par entité + Auth + Planning
│   ├── Services/        AffectationService, RemplacementService,
│   │                    PlanningService, JwtService, ServiceSession,
│   │                    AmorcageService, TenantContext, LimiteurDebit,
│   │                    ServiceCourrielSmtp, DescripteurErreursFrancais
│   ├── Middleware/      VerificationCsrf, EnTetesSecurite, LimiteCorpsRequete
│   ├── Filtres/         JournaliserAcces — filtre d'action MVC, pas un middleware
│   ├── Migrations/
│   └── Scripts/         jeu de démonstration, contrat-ecrans-reference.py,
│                        et les quatre SQL de conservation — purge-journal-acces,
│                        purge-jetons-rafraichissement, conservation-donnees-metier,
│                        degradation-absences-maladie (RGPD.md §5)
└── frontend/
    ├── csp.ts           les deux politiques de sécurité du contenu
    ├── public/polices/  Figtree et IBM Plex Mono, servies en propre (RGPD.md §9.1)
    ├── vite.config.ts   serveur https, proxy /api, en-têtes
    ├── scripts/         verifier-interface.mjs, tests.mjs
    └── src/
        ├── api/         wrappers fetch typés, client.ts, ressources.ts, + leurs séries
        ├── auth/        AuthContext, RouteProtegee, motDePasse.ts
        ├── composants/  un .tsx + un .css par composant
        ├── layout/      Navigation, Disposition, sections.ts
        ├── pages/       un fichier par écran, plus Reference.tsx et Compte.tsx
        ├── reference/   descriptions.ts, valeurs.ts — le motif liste-et-formulaire
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

Onze entités métier, toutes avec un `Id` de type `Guid` généré côté
application et une colonne `EntrepriseId` (dénormalisée, voir §7).

Une douzième entité existe et n'est **pas** métier : `AccesJournalise`, le journal
d'accès aux données personnelles de §7 bis. Elle suit les mêmes règles — `Guid`
applicatif, `EntrepriseId` dénormalisé, filtre global — mais elle ne décrit rien du
nettoyage de bureaux : elle rend compte de l'usage des onze autres.

```
Entreprise ─┬─ Client ─── Site ─── Contrat ─── Prestation ─── Intervention
            ├─ Agent ─── Absence                                    │
            └─ Produit ─── Stock                    Remplacement ───┘
```

| Entité | Champs notables |
|---|---|
| Entreprise | RaisonSociale, Siret, DateCreation |
| Client | Type (enum `TypeClient`), coordonnées, DateCreation |
| Site | Latitude, Longitude, Consignes, DateCreation |
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
  `Amorcage:RaisonSocialeEntreprise`.
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
UseHsts + UseHttpsRedirection   hors développement seulement
UseRequestTimeouts
LimiteCorpsRequete              avant toute lecture de corps
UseCors(« klean »)              avant la session : une origine interdite n'atteint rien
UseSwagger + UseSwaggerUI       développement seulement
EnTetesSecurite                 APRÈS Swagger, délibérément
VerificationCsrf
UseAuthentication / UseAuthorization
```

**`EnTetesSecurite` est enregistré après `UseSwaggerUI`.** L'interface de Swagger
a besoin de script et de style en ligne, que la politique interdit ; elle
court-circuite le pipeline, donc ses réponses n'atteignent jamais le middleware.
L'ordre suffit, aucune exception de chemin à écrire.

**Il y a deux politiques de sécurité du contenu, et il ne faut jamais recopier
l'une sur l'autre.**

| | Où | Contenu |
|---|---|---|
| API | `Middleware/EnTetesSecurite.cs` | `default-src 'none'` et rien d'autre |
| Document | `frontend/csp.ts` | `'self'` sur `script-src`, `style-src`, `font-src`, `connect-src` |

L'API ne rend que du JSON : rien ne doit jamais être chargé depuis une de ses
réponses. Le document, lui, charge une feuille de style, un script et quatre
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

Mécanisme en trois parties, toutes nécessaires :

1. **`ITenantContext` / `TenantContext`** lit la revendication `entrepriseId`
   du token via `IHttpContextAccessor`. Expose aussi `DesactiverFiltrage()`,
   utilisé par l'amorçage qui doit créer la première entreprise avant
   qu'aucun tenant n'existe.

2. **Filtres globaux EF Core**, **douze** au total :
   ```csharp
   modelBuilder.Entity<X>().HasQueryFilter(e =>
       !TenantFiltrageActif || TenantEntrepriseId == null
       || e.EntrepriseId == TenantEntrepriseId);
   ```
   où `TenantFiltrageActif` et `TenantEntrepriseId` sont des **propriétés
   privées d'instance du DbContext**. Voir le piège §10.

   Dix filtres sur les entités métier portant `EntrepriseId`, un onzième sur
   `AccesJournalise` (§7 bis), **plus un sur `Entreprise` elle-même**, sur son
   propre `Id` :
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

**Neuf lectures sont journalisées, et deux ne s'en doutaient pas.** C'est le fait
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
   cadré noierait les divulgations réelles.
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
dans une liste d'exemptions motivées. Quatre y figurent aujourd'hui, et leurs raisons
diffèrent :

| Exempté | Raison |
|---|---|
| `ClientDto`, `SiteDto` | coordonnées d'une **personne morale** — entreprise, syndic, copropriété |
| `ProfilDto` | l'identité de **l'appelant lui-même** ; un journal de « qui a lu son propre nom » est du bruit |
| `ProduitDto` | le `Nom` d'un produit est « Détergent sols » |

⚠️ `ClientDto` reste discutable : le contact d'une petite copropriété est souvent une
personne physique. La question appartient au lot de la cartographie des données ; elle
est consignée dans le test, pas tranchée. Et `ProduitDto` montre le prix de la règle —
`Nom` est un signal grossier. Le raffiner ne marche pas : exiger `Prenom` manquerait
`AbsenceDto`, qui ne porte que `Motif` ; exiger `Nom` **et** `Adresse` rattraperait
`ClientDto`. Une règle grossière dont chaque exception est écrite vaut mieux qu'une
règle fine qui laisse passer en silence.

Un second test garde l'excès symétrique — les dix actions journalisées sont
exactement celles attendues, parce qu'un journal bruyant ne se lit plus.

**`Divulgation` et `Compter` sont `public`, pour être éprouvées directement.** Même
raison que l'extraction de `reference/valeurs.ts` côté écran (§11) : c'est la seule
partie du filtre qui écrit une donnée **fausse** au lieu de ne rien écrire, et un
compte erroné ne lève aucune erreur. Plusieurs de leurs branches gardent des formes
qu'aucune action ne produit aujourd'hui — notamment qu'une `string`, étant un
`IEnumerable<char>`, vaut **une** ligne et non cent trente-quatre. Trois sabotages y
sont restés verts avant que ces tests existent.

**Coût assumé** : une insertion synchrone par lecture sur ces neuf endpoints.

⚠️ **Dix attributs pour neuf lectures** : la dixième action est `Anonymiser`, qui n'est
pas une lecture mais une **écriture irréversible** — nature `Anonymisation`. Et
`Dossier` porte la nature `Export` plutôt que `Lecture`, parce que sortir le dossier
complet d'un salarié n'est pas le même geste que consulter sa fiche : les confondre
ferait disparaître l'export dans le bruit des consultations.

### Le dossier d'un agent

`GET /api/agents/{id}/dossier`, administrateur seul, journalisé en `NatureAcces.Export`.
Il sert l'accès de l'article 15 et la portabilité de l'article 20 ; le détail de son
contenu et de ses exclusions est en `RGPD.md` §6. Trois choses à savoir avant d'y
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
- ⚠️ **Le compte lié est cadré à la main sur `EntrepriseId`**, `AspNetUsers` n'étant pas
  filtrée (§7). Le test construit le compte intrus **directement en base**, sous les
  validations qui le rendraient incréable par l'API — même raison qu'au lot B de
  l'étape 17.

### Les quatre scripts de conservation

`RGPD.md` §5 porte les durées ; `backend/Scripts/` porte les gestes. **Deux seulement
suppriment**, et la distinction est dans le script lui-même :

| Script | Durée | Opération |
|---|---|---|
| `purge-journal-acces.sql` | 12 mois | `DELETE` décommentable |
| `purge-jetons-rafraichissement.sql` | 30 jours après mort du jeton | `DELETE` décommentable |
| `conservation-donnees-metier.sql` | 1 à 5 ans selon la table | **rapport seul**, `DELETE` commenté |
| `degradation-absences-maladie.sql` | 3 mois | **rapport seul**, `UPDATE` commenté |

⚠️ Les deux derniers ne suppriment rien tant qu'un humain ne décommente pas, et ce n'est
pas de la timidité : les durées de `RGPD.md` §5 sont des propositions non validées
juridiquement, et une purge de données métier est irréversible.

⚠️ **L'ordre des `DELETE` est une contrainte, pas une préférence de présentation.** Six
clés de la chaîne d'exploitation sont en `Restrict`, et deux de plus sur l'agent —
`Intervention.AgentId` et `Remplacement.AgentRemplacantId`. Un `DELETE` dans le mauvais
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
route ne permettait d'effacer ses données — ni même de le désactiver.

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

  Deux exceptions, et deux seulement : `ITenantContext` et `IServiceCourriel`.
  Même justification dans les deux cas — la substitution est **nécessaire**, pas
  décorative. Pour le courriel, l'alternative serait une classe concrète qui
  « n'envoie pas vraiment » selon la configuration, ce qui ferait dépendre le
  comportement de production d'un réglage, sur un chemin d'authentification.
  Toute troisième interface de service doit être justifiée de la même façon.
- Ne jamais contourner les filtres multi-tenant avec `IgnoreQueryFilters()`.

**Frontend**
- Un `.tsx` et un `.css` par composant, côte à côte.
- `import type` pour les types (projet en `verbatimModuleSyntax`).
- Aucune couleur en dur : uniquement les variables de `tokens.css`.
- Trois états explicites sur tout appel réseau : chargement, erreur, données.
- Le wrapper `src/api/client.ts` ajoute le token et purge la session sur 401.
- Les composants de présentation reçoivent leurs données en props ; c'est la
  page qui orchestre les appels API.
- **Un écran de données de référence ne s'écrit pas à la main.** Six listes et six
  formulaires copiés-collés auraient six fois l'occasion de diverger de la matrice
  des droits, et la divergence a une forme précise : un bouton d'enregistrement qui
  répond 403. Une description dans `src/reference/descriptions.ts`, et
  `src/pages/Reference.tsx` la rend. Ajouter une ressource, c'est ajouter une
  description et une clé dans `sections.ts` — jamais un fichier de page.
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
- **Un écran de compte n'est pas une section.** `layout/sections.ts` déclare
  `CheminCompte` sans le mettre dans `Sections`, et `sections.test.ts` garde
  l'absence. Une section est une fonction du produit, ouverte par rôle, et
  `premiereSectionOuverte` en fait une **destination d'arrivée** : le compte, ouvert à
  tout rôle, deviendrait l'arrivée de tous. Il vit au pied de la barre latérale,
  auprès de la déconnexion. Conséquence heureuse : il est atteignable pour le rôle
  `Agent`, qui n'a aucune section et tombe sur un refus en arrivant — la barre étant
  rendue, son mot de passe lui reste accessible.

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
```

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
