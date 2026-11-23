# Version 0.3.0 — Le géocodage des sites

Première moitié du lot A3 de l'étape 34 (`SPEC.md`, « Lot A3 — L'import et le géocodage »,
points 9 et 10). L'import des classeurs `.xlsx` en est la seconde moitié, en 0.3.1, et
réutilisera ce service une fois éprouvé.

## Pourquoi

Un site exige une latitude et une longitude (`Site.Latitude`, `Site.Longitude`, non
nullables) : le moteur d'affectation classe les remplaçants par distance (§8), et le passage
de l'agent tranche sa preuve sur la distance au site (§9 ter). Aujourd'hui, le patron les tape
à la main, ce qu'il ne sait pas faire. Le géocodage les remplit depuis l'adresse du site.

## Ce qui est vérifié du service, le 23 novembre 2026

- **L'adresse** : `https://data.geopf.fr/geocodage/search`, la Géoplateforme de l'IGN.
  L'ancienne `api-adresse.data.gouv.fr` est annoncée « décommissionnée fin janvier 2026 »
  (adresse.data.gouv.fr, page de l'API Adresse).
- **Sans clé ni inscription** (data.gouv.fr : « Ouvert »). **50 requêtes par seconde et par
  IP** ; au-delà, 429 avec un en-tête `retry-after` (cartes.gouv.fr, « Limites d'usage »).
- **La réponse**, mesurée sur une adresse publique : GeoJSON, `features[]` ; chaque
  `geometry.coordinates` est **`[longitude, latitude]`** ; `properties` porte `label`
  (l'adresse trouvée), `score` (0 à 1), `type` (`housenumber`, `street`, `municipality`…),
  `city`, `postcode`. **Une adresse introuvable rend `features: []`**, en 200.
- **Les conditions** : CGU de la Géoplateforme (cartes.gouv.fr/cgu, IGN) ; données sous
  Licence Ouverte Etalab ; aucune mention de source exigée pour l'usage d'une API ; l'IGN peut
  « limiter ou bloquer l'accès » en cas d'usage abusif.
- **Les journaux du service** : aucune page lue ne dit si les requêtes ou les adresses IP
  sont conservées, ni combien de temps. La limite par IP suppose que l'IP est lue. Contact :
  `dpo@ign.fr`. Ce silence est écrit tel quel dans `RGPD.md`, sans être comblé par une
  supposition.

## Décisions prises avec l'utilisateur

1. **Découpage** : le géocodage d'abord (0.3.0), l'import ensuite (0.3.1).
2. **Le réglage vit dans l'application**, sur l'entreprise, et non dans `.env` : aucun SSH,
   et l'assistant du lot A2 dit déjà « comment le changer ».
3. **Un bouton « Localiser »** sur la fiche d'un site. Rien ne part sans ce geste ; aucun
   géocodage à l'enregistrement.

## Le serveur

### Le réglage

- `Entreprise.GeocodageActif`, `bool`, **`false` par défaut**, colonne `NOT NULL DEFAULT
  false`. Migration additive, à présenter avant d'être générée (§14) : aucune opération
  destructrice.
- `EntrepriseDto` et `EntrepriseCreateDto` gagnent `GeocodageActif`. Il se modifie par le
  `PUT /api/entreprises/{id}` existant, avec ses droits actuels.
- `KLEAN_GEOCODAGE` disparaît de `deploiement/env.modele` et de l'écriture de `.env` par
  `klean installer` (`deploiement/klean`). Une ligne déjà présente dans un `.env` n'est lue par
  rien et ne gêne rien. `deploiement/tests/tests.sh` l'emploie comme variable d'exemple de
  `completer_env` : elle y est remplacée par une variable que `env.modele` porte encore. Le
  contrat N → N+1 n'est pas touché : retirer une ligne du modèle ne casse aucun `klean`
  installé.

### La route

`POST /api/sites/localiser`, dans `SitesController`.

- **Administrateur seul**, comme l'écriture des sites (`RolesKlean.Administrateur`).
- **Corps** : `LocaliserSiteDto { string? Adresse; string? CodePostal; string? Ville }`,
  sans annotation, vérifié par le contrôleur en français (§10, la validation de modèle répond
  en anglais). Mêmes bornes que `Site` : adresse obligatoire, 200 ; code postal 10 ; ville
  100. Un caractère de contrôle est refusé, comme dans l'assistant du lot A2.
- **Réponse 200** : `LocalisationDto { double Latitude; double Longitude; string
  AdresseTrouvee; bool AVerifier }`. `LocaliserSiteDto` et `LocalisationDto` vont dans
  `DTOs/SiteDto.cs`, un fichier par entité (§11).
- **Non journalisée, et c'est une décision.** `LocalisationDto` porte `Latitude` et
  `Longitude`, que le filet de `JournalAccesTests` surveille ; il n'y tombe que parce que la
  route est un `POST`, et le filet ne vise que les `GET` (§7 bis). Ce qu'elle rend est dérivé
  de ce que l'appelant vient d'envoyer, pas une donnée d'autrui lue en base : le cas que §7 bis
  laisse hors du journal (« a lu les données qu'il a lui-même écrites »). ⚠️ L'adresse
  envoyée peut être un domicile, ce qu'aucun contrôle ne distingue : c'est écrit dans
  `RGPD.md` T6 et rappelé dans `CLAUDE.md` §7 bis, avec la condition qui ferait changer la
  décision — une route qui géocoderait une adresse **lue en base**, celle d'un agent par
  exemple, serait une lecture de données d'autrui, à journaliser.
- **Ordre des contrôles** :
  1. le plafond par compte ;
  2. le réglage de l'entreprise de l'appelant : désactivé, **409** « La localisation des sites
     est désactivée : cochez-la dans Entreprise. » ;
  3. les champs : **400** en français ;
  4. l'appel au service.
- **Les issues de l'appel** :
  - trouvé : 200 ;
  - `features` vide : **400** « Adresse introuvable : vérifiez-la, ou saisissez les
    coordonnées à la main. » Aucun site ne reçoit jamais (0, 0) ;
  - injoignable, délai dépassé, 429, 3xx, 5xx, réponse illisible ou trop grosse, coordonnées
    hors bornes : **503** « Le service de localisation de l'État ne répond pas : réessayez
    plus tard, ou saisissez les coordonnées à la main. »
- **`AVerifier`** est vrai si `score < 0,7` **ou** `type ≠ housenumber` : une adresse placée
  au centre de sa rue ou de sa commune doit se voir. Le seuil est une constante, à éprouver
  sur le terrain avant d'en faire un réglage — même règle que le rayon du passage (§9 ter).
- **Le plafond** : `PlafondsDebit.LocalisationsParCompte = 30` sur
  `PlafondsDebit.FenetreLocalisations`, quinze minutes, par
  `LimiteurDebit`, clé du compte. Un 429 porte `Retry-After`. Il protège le quota par IP de
  l'instance auprès de l'IGN, que tous les comptes partagent.

### `ServiceGeocodage`

Classe concrète, dans `backend/Services/`, enregistrée avec un **client HTTP nommé**
`geocodage`. Aucune interface nouvelle : les tests substituent le `HttpMessageHandler` du
client nommé (§11, et le cadrage A3, point 9).

- **Réglage `Geocodage:Adresse`**, par défaut `https://data.geopf.fr/geocodage`. Le
  démarrage **refuse** une adresse qui n'est pas une URI absolue en `https` — relative,
  illisible ou en `http` : configuré à moitié, comme un SMTP incomplet (§2). La lecture suit
  le motif de `CodeInstallation.Lire(IConfiguration)`, une fonction éprouvée directement par
  ses tests, sans second hôte ni variable d'environnement globale à la série.
- **La requête** : `GET {Adresse}/search?q={adresse} {ville}&postcode={codePostal}&limit=1&index=address`,
  `postcode` omis s'il est vide. Ce qui part : **la voie, le code postal et la commune** d'un
  site — ni son nom, ni ses consignes, ni le client.
- **`User-Agent: KLEAN`**, sans version ni domaine : aucun en-tête n'identifie l'instance.
- **L'enregistrement est une méthode**, `ServiceGeocodage.Enregistrer(IServiceCollection,
  IConfiguration)`, appelée par `Program.cs` : le test de la redirection l'appelle sur une
  `ServiceCollection` neuve pour obtenir la configuration de production du client, sans
  substitution. La série, elle, substitue en ajoutant au client nommé un `DelegatingHandler`
  **en dernier**, qui répond sans appeler la suite : le gestionnaire primaire reste en place.
- **Le gestionnaire** : le gestionnaire primaire du client nommé est un `SocketsHttpHandler`
  avec `AllowAutoRedirect = false` ; c'est lui qui suivrait une redirection, pas `HttpClient`.
  Délai : `Geocodage:DelaiSecondes`, 5 par défaut ; un entier de 1 à 30, sinon le démarrage
  refuse, comme pour l'adresse. Il est lu **à la construction du service**, depuis
  `IConfiguration`, et non au niveau supérieur de `Program.cs` : le test du délai le pose par
  `ConfigureAppConfiguration` sur une fabrique dérivée, qui n'atteint pas le reste de la série
  (§10, `WebApplicationFactory`). Réponse lue avec un plafond de 64 Kio, au-delà de quoi elle est rejetée.
  Le `CancellationToken` de l'action est passé à l'appel.
- **Aucun journal du client** : `RemoveAllLoggers()` sur le client `geocodage`. C'est la
  garde principale du journal technique, plus bas : un exploitant qui passe `Default` en
  `Debug` ne la défait pas.
- **La lecture** : `System.Text.Json`, le premier `feature` ; longitude puis latitude ;
  latitude dans [-90, 90] et longitude dans [-180, 180], sinon « illisible » ; `label` vide ou
  absent, illisible aussi.
- **Toute exception de l'appel est rattrapée dans le service** et rendue comme « injoignable »,
  sauf l'annulation par le jeton de la requête, qui remonte. Le délai dépassé du client lève
  lui aussi une `TaskCanceledException` : la distinction se fait sur
  `ct.IsCancellationRequested`, jamais sur le type de l'exception. Une exception non rattrapée
  finirait dans le journal du gestionnaire d'erreurs.

### Le journal technique

L'URL de la requête porte l'adresse d'un site. Le journal JSON de l'API (§11) ne doit pas la
recopier.

- **Deux gardes.** `RemoveAllLoggers()` sur le client nommé, la principale ; et
  `appsettings.json`, qui règle **`System.Net.Http.HttpClient` à `Warning`**, la redondance
  demandée par le cadrage. Les lignes d'information du client (« Sending HTTP request GET … »)
  portent l'URL complète, et sa **portée** `HTTP GET {Uri}` aussi — or `IncludeScopes` est
  vrai, et un niveau filtre les messages, pas les portées d'une ligne écrite par une autre
  catégorie.
- **Deux tests, un par garde**, car deux gardes qui couvrent le même cas ne s'éprouvent
  qu'une fois séparées (§7 bis) :
  - **`RemoveAllLoggers()`** : un fournisseur de capture, ajouté à l'hôte de la série avec sa
    règle propre `AddFilter<FournisseurCapture>(null, LogLevel.Trace)` — une règle propre à un
    fournisseur l'emporte sur toute règle générale, donc sur le `Warning` de
    `appsettings.json` —, enregistre **les messages et les portées**. Après une localisation,
    aucune ligne ne contient l'adresse envoyée, ni `geocodage/search?`. Sabotage :
    `RemoveAllLoggers()` retiré, le test rougit. Ce n'est pas tout à fait le chemin de la
    console : c'est le client lui-même qu'il éprouve.
  - **Le réglage de `appsettings.json`** : lu dans le fichier, comme la série du frontend lit
    les sources du serveur, `System.Net.Http.HttpClient` y vaut `Warning`, et
    `appsettings.Development.json` ne le redéfinit pas plus bas.

## L'écran

### Entreprise

- Un champ `geocodageActif`, type `booleen`, libellé « Localiser les sites par le service de
  l'État ».
- Aide : « La voie, le code postal et la commune d'un site partent vers la Géoplateforme de
  l'IGN à chaque clic sur Localiser. Rien d'un agent. Désactivé, les coordonnées se saisissent
  à la main. »
- `descriptions.test.ts` l'exige déjà, puisqu'il compare les champs aux DTOs de création.

### La fiche d'un site

- **Une propriété déclarative nouvelle, `localiser`**, ajoutée à `DescriptionRessource`
  dans `types/reference.ts` et posée sur la description des sites. Elle nomme les champs
  d'adresse et de coordonnées :
  `{ adresse: 'adresse', codePostal: 'codePostal', ville: 'ville', latitude: 'latitude', longitude: 'longitude' }`.
  Aucune page écrite à la main (§11). `descriptions.test.ts` vérifie que chaque nom désigne un
  champ de la description.
- **Qui fait quoi** (§11 : la page orchestre, le composant présente) :
  - `Reference.tsx` lit le réglage et l'appel. **Seulement quand le formulaire est éditable**,
    donc pour un rôle d'écriture : `GET /api/entreprises` est fermé au `ClientSyndic`, à qui la
    fiche d'un site est ouverte, et y répondrait 403 à chaque ouverture — le motif de
    `comptes` dans `types/reference.ts`. Réglage coché, la page passe à `FormulaireRessource`
    une prop `onLocaliser(adresse, codePostal, ville)`, qui rend le résultat ou le refus ;
    sans réglage, aucune prop.
  - `FormulaireRessource` tient les valeurs (`useState(depart)`) : il montre le bouton quand
    la prop est là, l'appelle avec ses valeurs courantes, et pose lui-même latitude et
    longitude.
- **Au clic** : les deux champs sont remplis, rien n'est enregistré, et une ligne sous eux dit
  « Trouvé : 20 Avenue de Ségur 75007 Paris », suivie de « — à vérifier » quand
  `aVerifier`, en mots et non par la seule couleur (§12). Les champs restent modifiables.
- Adresse vide : bouton inactif, refus local en français, sans appel.
- Pendant l'appel : bouton inactif, libellé « Localisation… ». Un refus du serveur s'affiche
  sous les champs ; un 409 dit d'aller dans Entreprise.
- **`reference/localiser.ts`** porte ce qui se décide hors du composant : le refus local, le
  corps envoyé, le message d'un refus du serveur et la ligne « Trouvé : … ». Éprouvé par sa
  série, avec l'extension `.ts` explicite sur la chaîne d'imports (§10).

### L'assistant d'installation

`pages/Installation.tsx`, dernier écran : « Elle se proposera avec une prochaine version, sur
votre choix » devient « Désactivée : à activer, si vous le voulez, dans Entreprise ».

## Les documents

- **`RGPD.md`**
  - §1 : **l'IGN est un destinataire, responsable de ses propres traitements, et non un
    sous-traitant.** Il opère un service public sous ses propres CGU, sans contrat ni
    instruction de l'exploitant, et ne traite pas « pour le compte » de l'entreprise. Ce qu'il
    garde de ses journaux n'est pas publié : écrit tel quel, avec le contact `dpo@ign.fr`.
  - T6 : le géocodage comme moyen de localiser les sites. ⚠️ Le serveur ne distingue pas une
    adresse de site d'un domicile : un client particulier a une adresse personnelle.
  - §3 et §4 : la sortie vers `data.geopf.fr`, ce qui part, à chaque clic, désactivée par
    défaut.
- **`CLAUDE.md`** : la route, le service et son client nommé, le réglage, le journal
  technique réglé ; `ServiceGeocodage` dans l'arborescence ; `KLEAN_GEOCODAGE` retiré.
- **`DEPLOIEMENT.md`** : la sortie vers `data.geopf.fr` en 443, à la ligne 53 (« aucun accès
  sortant »), à ouvrir sur un pare-feu sortant ; les lignes 175-176, qui annoncent le
  géocodage et parlent de `KLEAN_GEOCODAGE`, réécrites.
- **`deploiement/klean`** : le commentaire des lignes 866-868 (« la question viendra avec
  elle »), réécrit avec la ligne retirée.
- **`README.md`** : une ligne dans la liste des sorties, si elle existe.
- **`SPEC.md`** : le compte rendu « 0.3.0 — fait le … », à la publication.

## Les tests

**Serveur** (`backend.Tests`), le gestionnaire du client nommé substitué :

- trouvé, avec `AVerifier` faux ; score bas ; `type` `street` ; introuvable (`features: []`) ;
- injoignable ; délai dépassé ; 429 ; 302 rendu en 503 ; 500 ; JSON illisible ; réponse de
  plus de 64 Kio ; latitude hors bornes ;
- **la redirection n'est pas suivie** : ce n'est pas le faux gestionnaire qui le prouve — il
  ne suit rien de lui-même. Le test résout `IHttpMessageHandlerFactory.CreateHandler("geocodage")`
  sur la configuration de production du client, descend la chaîne des `InnerHandler` jusqu'au
  gestionnaire primaire, et vérifie qu'il est un `SocketsHttpHandler` avec
  `AllowAutoRedirect = false`. Le sabotage — la ligne retirée — le fait rougir ;
- réglage désactivé : 409, **aucun appel sortant** ;
- champs refusés en français, dont l'adresse vide et un caractère de contrôle ;
- un test par rôle refusé (Responsable, ChefEquipe, Agent, ClientSyndic) ;
- le plafond par compte, avec `Retry-After` ;
- la requête sortante : `q`, `postcode`, `limit`, `index`, et rien d'autre ; `User-Agent`
  exactement `KLEAN` ;
- le journal technique : aucune ligne ne contient l'adresse envoyée ;
- le démarrage refuse `Geocodage:Adresse` en `http` ;
- la migration : une entreprise insérée **par SQL, sans la colonne**, depuis une
  `PorteeAuProprietaire()` (sous `klean_app`, `Entreprises` refuserait en `42501`, §10) et sous
  un SIRET libre (`IX_Entreprises_Siret`) — comme une ligne écrite
  avant la migration —, relue en base, porte `false`. Une entreprise créée par EF ne prouve
  rien : EF écrit explicitement le `false` du CLR, et le défaut de la colonne n'est jamais
  exercé.

**Écran** : la série de `localiser.ts` ; l'invariante de `localiser` dans
`descriptions.test.ts`.

**Pour de vrai** : sur le VPS, réglage coché, une adresse publique localisée depuis la fiche
d'un site ; puis le réglage décoché, le bouton disparu.

## La version

**0.3.0**, avec une migration. La mise à jour depuis 0.2.1 est conduite par le `klean` de
0.2.1 et ne demande rien de nouveau : la migration passe par `migrer`, et `env.modele` perd
seulement une ligne.

## Hors de cette version

- L'import des classeurs `.xlsx` : 0.3.1.
- Le géocodage des domiciles des agents (cadrage A3, « Hors de cette étape »).
- Une carte : elle demanderait des tuiles d'un tiers, donc une origine externe dans la
  politique de sécurité du contenu (§6 bis).
- Un géocodage à l'enregistrement, ou par lot depuis l'écran.
