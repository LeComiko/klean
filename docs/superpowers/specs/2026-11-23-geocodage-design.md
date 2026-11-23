# Version 0.3.0 — Le géocodage des sites

Première moitié du lot A3 de l'étape 34 (`SPEC.md`, « Lot A3 — L'import et le géocodage »,
points 9 et 10). L'import des classeurs `.xlsx` en est la seconde moitié, en 0.3.1, et
réutilisera ce service une fois éprouvé.

Relue six fois : une relecture de spécification en deux passes, puis quatre relectures
spécialisées — serveur .NET, sécurité, RGPD, écran React. Leurs constats sont intégrés ; ce
qui reste une lecture juridique, et non un fait, est marqué comme tel.

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
  IP** ; au-delà, 429 avec un en-tête `retry-after`, blocage de 5 s (cartes.gouv.fr, « Limites
  d'usage »).
- **La réponse**, mesurée sur une adresse publique : GeoJSON, `features[]` ; chaque
  `geometry.coordinates` est **`[longitude, latitude]`** ; `properties` porte `label`
  (l'adresse trouvée), `score` (0 à 1), `type` (`housenumber`, `street`, `municipality`…),
  `city`, `postcode`. **Une adresse introuvable rend `features: []`**, en 200.
- **Les bornes**, mesurées : `q` doit compter **de 3 à 200 caractères et commencer par une
  lettre ou un chiffre**, sinon 400 ; `postcode` doit compter **exactement cinq chiffres**,
  sinon 400.
- **Les conditions** : CGU de la Géoplateforme (cartes.gouv.fr/cgu, IGN, version du
  15 octobre 2024) ; données sous Licence Ouverte Etalab ; aucune mention de source exigée pour
  l'usage d'une API ; l'IGN peut « limiter ou bloquer l'accès » en cas d'usage abusif. L'IGN est
  un établissement public de l'État, hébergé par OVH à Roubaix (mentions légales).
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

- Trois colonnes sur `Entreprises`, une migration additive, à présenter avant d'être générée
  (§14) :
  - `GeocodageActif`, `bool`, `NOT NULL DEFAULT false` ;
  - `GeocodageActiveLe` et `GeocodageDesactiveLe`, `timestamp with time zone` nullables,
    **posées par le serveur** au changement d'état, jamais reçues. Elles répondent à la
    question d'une demande d'accès — « mon adresse est-elle partie, et quand ? » — que le
    clic, qui n'enregistre rien, laisse sans trace (`RGPD.md` §6). Le même raisonnement que
    `DateAnonymisation`, une date et non un drapeau.
- `EntrepriseDto` gagne les trois ; `EntrepriseCreateDto` gagne `GeocodageActif` seul.
- ⚠️ **`EntreprisesController` projette `EntrepriseDto` à la main**, dans `Lister` et dans
  `Obtenir`, et le `PUT` recopie champ par champ. Le champ doit être ajouté aux trois
  endroits : oublié dans une projection, la case s'affiche toujours décochée ; oublié dans le
  `PUT`, chaque enregistrement de l'entreprise désactive la localisation, sans erreur. Un test
  fait `PUT` à `true`, relit par `GET` **et** en base.
- Le `PUT` pose `GeocodageActiveLe` quand le réglage passe de `false` à `true`, et
  `GeocodageDesactiveLe` dans l'autre sens ; un `PUT` qui ne le change pas ne touche aucune
  date. Les droits du `PUT` sont ceux d'aujourd'hui : administrateur seul. Ce changement n'est
  pas consigné au journal d'accès (§7 bis, règle 5 : rien n'est divulgué).
- `KLEAN_GEOCODAGE` disparaît de `deploiement/env.modele` et de l'écriture de `.env` par
  `klean installer` (`deploiement/klean`, ligne 890), avec le commentaire des lignes 866-868
  (« la question viendra avec elle »), réécrit. Une ligne déjà présente dans un `.env` n'est
  lue par rien et ne gêne rien. `deploiement/tests/tests.sh` l'emploie comme variable d'exemple
  de `completer_env` (lignes 99-100) : elle y est remplacée par une variable que `env.modele`
  porte encore. Le contrat N → N+1 n'est pas touché : retirer une ligne du modèle ne casse aucun
  `klean` installé.

### La route

`POST /api/sites/localiser`, dans `SitesController`, `[Authorize(Roles =
RolesKlean.Administrateur)]` sur l'action, comme l'écriture des sites. L'adresse voyage dans
le **corps** : un `GET ?adresse=` la ferait entrer dans le journal des requêtes d'ASP.NET,
réglé à `Information`.

- **Corps** : `LocaliserSiteDto { string? Adresse; string? CodePostal; string? Ville }`,
  sans annotation, vérifié par le contrôleur en français (§10, la validation de modèle répond
  en anglais).
- **Réponse 200** : `LocalisationDto { double Latitude; double Longitude; string
  AdresseTrouvee; bool AVerifier }`. Les deux DTOs vont dans `DTOs/SiteDto.cs`, un fichier par
  entité (§11).
- **Ordre des contrôles** :
  1. le plafond par compte, puis le plafond global ;
  2. le réglage de **l'entreprise du jeton**, lu par `Entreprises.AsNoTracking()` sous la RLS
     et cadré sur `e.Id == tenant.EntrepriseId` ; désactivé, ou entreprise introuvable :
     **409** « La localisation des sites est désactivée : cochez-la dans Entreprise. » ;
  3. les champs, **400** en français :
     - adresse obligatoire, de 200 caractères au plus, code postal 10, ville 100 — les bornes
       de `Site` ; un caractère de contrôle refusé, comme dans l'assistant du lot A2 ;
     - **la recherche envoyée** — l'adresse et la ville, jointes par une espace, blancs rognés
       — doit compter de 3 à 200 caractères et commencer par une lettre ou un chiffre : les
       bornes mesurées du service. Au-delà : « L'adresse et la ville dépassent ensemble
       200 caractères : abrégez-les. » Ces refus ne consomment aucun appel sortant ;
  4. le disjoncteur, plus bas ;
  5. l'appel au service.
- **Les issues de l'appel** :
  - trouvé : 200 ;
  - `features` vide : **400** « Adresse introuvable : vérifiez-la, ou saisissez les
    coordonnées à la main. » Aucun site ne reçoit jamais (0, 0) ;
  - tout le reste — injoignable, délai dépassé, **tout statut hors 2xx**, réponse illisible ou
    trop grosse, coordonnées hors bornes — : **503** « La Géoplateforme de l'IGN ne répond
    pas : réessayez plus tard, ou saisissez les coordonnées à la main. »
- **`AVerifier`** s'écrit `!(score >= 0,7) || type != "housenumber"` : un score absent ou non
  numérique, une adresse placée au centre de sa rue ou de sa commune, se voient. Le seuil est
  une constante, à éprouver sur le terrain avant d'en faire un réglage — la règle du rayon du
  passage (§9 ter).
- **`AdresseTrouvee`** est un texte d'un tiers renvoyé à l'écran : borné à 200 caractères,
  caractères de contrôle et de bidirectionnalité retirés — sinon il pourrait faire afficher une
  autre adresse que celle trouvée. React l'échappe ; ce n'est pas une question de XSS.
- **Non journalisée, et c'est une décision.** `LocalisationDto` porte `Latitude` et
  `Longitude`, que le filet de `JournalAccesTests` surveille ; il n'y tombe pas parce que la
  route est un `POST`, et le filet ne vise que les `GET` (§7 bis). Ce qui la justifie : **rien
  n'est lu en base, la réponse est une donnée publique de référence sur l'adresse que
  l'appelant vient d'envoyer, et aucune donnée d'agent n'y figure.** Le journal d'accès existe
  pour la surveillance des salariés (`RGPD.md` §8.3), qui n'est pas en jeu. La décision tombe
  le jour où une route géocode une adresse **lue en base** — celle d'un agent, ou l'import de la
  0.3.1 s'il géocode des adresses déjà enregistrées.

### Les plafonds et le disjoncteur

Le service compte **par adresse IP**, donc par instance : toutes les entreprises et tous les
comptes d'un serveur partagent ses 50 requêtes par seconde, et un dépassement bloque tout le
monde, cinq secondes ou plus si l'IGN juge l'usage abusif.

- **Par compte** : `PlafondsDebit.LocalisationsParCompte = 30` sur
  `PlafondsDebit.FenetreLocalisations`, quinze minutes, par `LimiteurDebit`. Il borne un
  compte, et ne protège pas le quota seul : trente requêtes peuvent partir dans la même seconde,
  et un administrateur peut créer d'autres administrateurs.
- **Global** : `PlafondsDebit.LocalisationsGlobales = 10` par seconde, clé `geocodage:global`,
  sur le modèle de `installation:codes-faux:global`. Une seconde d'attente ne prive personne ;
  dix par seconde restent loin des cinquante du service.
- **Le disjoncteur** : un 429 du service pose une échéance — `retry-after` lu en secondes,
  cinq à défaut, trente au plus — dans un singleton. Jusqu'à elle, la route répond **503** sans
  appeler le service : relancer pendant le blocage le prolongerait.
- Tous les 429 de KLEAN portent `Retry-After`, en secondes, comme ceux de
  `InstallationController`. La fonction qui le pose y est privée : elle est extraite plutôt que
  recopiée.

### `ServiceGeocodage`

Classe concrète, dans `backend/Services/`. Aucune interface nouvelle : les tests substituent
le gestionnaire du client HTTP nommé (§11).

- **La configuration** : `ServiceGeocodage.Lire(IConfiguration)` lit et valide en une fois,
  sur le motif de `CodeInstallation.Lire`, éprouvé directement par ses tests :
  - `Geocodage:Adresse`, par défaut `https://data.geopf.fr/geocodage` : une URI absolue en
    `https`, **sans identifiants, requête ni fragment** ; sinon refus ;
  - `Geocodage:DelaiSecondes`, 5 par défaut : un entier de 1 à **20**, lu par `int.TryParse`
    pour refuser en français — strictement sous les 30 s du délai de requête
    (`Http:DelaiRequeteSecondes`), sinon le jeton de la requête gagnerait la course et
    l'appelant recevrait un 504 au lieu du 503.

  ⚠️ **Elle est appelée deux fois**, et les deux sont nécessaires. Par
  `ServiceGeocodage.Enregistrer(IServiceCollection, IConfiguration)`, appelée par `Program.cs`
  avec `builder.Configuration` : c'est ce qui **fait refuser le démarrage**, comme
  `CodeInstallation.Lire` à `Program.cs:236`. Et dans le délégué du client nommé, qui lit
  `IConfiguration` depuis le fournisseur de services, pour la valeur finale : c'est ce qui laisse
  le test du délai poser `Geocodage:DelaiSecondes` par `ConfigureAppConfiguration` sur une
  fabrique dérivée, sans toucher au reste de la série (§10, `WebApplicationFactory`).
- **Le client nommé `geocodage`**, enregistré par `Enregistrer` :
  - gestionnaire primaire par `UseSocketsHttpHandler`, réglé : `AllowAutoRedirect = false` ;
    `UseCookies = false` — sinon un cookie posé par le service reviendrait sur toutes les
    requêtes de toutes les entreprises, et les relierait ; `ActivityHeadersPropagator =
    DistributedContextPropagator.CreateNoOutputPropagator()` — sinon chaque requête porterait
    `traceparent`, dont le `TraceId` est **l'identifiant de corrélation de KLEAN**, celui de ses
    journaux, de `X-Correlation-Id` et de ses corps d'erreur ; `AutomaticDecompression = None`,
    explicitement ;
  - `Timeout` à `DelaiSecondes`, et `MaxResponseContentBufferSize = 65536` ;
  - `DefaultRequestHeaders.UserAgent.ParseAdd("KLEAN")`, sans version ni domaine ;
  - **`RemoveAllLoggers()`**, la garde principale du journal technique, plus bas.
- **La requête** : la base débarrassée de ses `/` finaux, puis `/search?`, puis les paramètres
  encodés **un par un par `Uri.EscapeDataString`** — jamais une combinaison d'`Uri`, qui
  perdrait le dernier segment d'une base sans `/` final, et ferait changer d'hôte à une partie
  relative commençant par `//`. Paramètres : `q` (la recherche contrôlée plus haut),
  `postcode` **seulement s'il compte exactement cinq chiffres**, `limit=1`, `index=address`.
  Ce qui part : **la voie, le code postal et la commune** d'un site — ni son nom, ni ses
  consignes, ni le client — et rien d'autre que `User-Agent` et `Host`.
- **La lecture** : `GetAsync` en mode par défaut, qui met le corps en tampon — c'est ce qui
  fait valoir le plafond de 64 Kio **et** le délai sur tout le corps, y compris une réponse
  servie au compte-gouttes ; jamais `ResponseHeadersRead`, après quoi `Timeout` ne couvre plus
  le corps. Puis `System.Text.Json` sur le premier `feature` : `coordinates` d'au moins deux
  éléments, lus par `TryGetDouble`, longitude puis latitude ; bornes écrites
  `double.IsFinite(x) && -90 <= lat <= 90` et `-180 <= lon <= 180`, qui rejettent NaN et
  l'infini ; `label` absent ou vide : illisible.
- **Les exceptions** : l'appel **et** l'analyse sont dans le même `try`.
  `catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }` vient
  d'abord : l'annulation de la requête remonte. Puis `catch (Exception)` rend « injoignable » —
  dont le délai du client, qui lève lui aussi une `TaskCanceledException`, mais avec un `ct` non
  annulé. Une exception non rattrapée finirait dans le journal du gestionnaire d'erreurs.

### Le journal technique

L'URL de la requête porte l'adresse d'un site. Le journal JSON de l'API (§11) ne doit pas la
recopier.

- **Deux gardes.** `RemoveAllLoggers()` sur le client nommé, la principale : un exploitant qui
  passe `Default` en `Debug` ne la défait pas. Et `appsettings.json`, qui règle
  **`System.Net.Http.HttpClient` à `Warning`**, la redondance que demande le cadrage. Les lignes
  d'information du client portent l'URL complète, et sa **portée** `HTTP GET {Uri}` aussi : or
  `IncludeScopes` est vrai, et un niveau filtre les messages, pas la portée d'une ligne écrite
  par une autre catégorie.
- **Deux tests, un par garde**, car deux gardes qui couvrent le même cas ne s'éprouvent qu'une
  fois séparées (§7 bis) :
  - **`RemoveAllLoggers()`**, dans un hôte dérivé (`WithWebHostBuilder`, le précédent de
    `ExploitationTests`), jamais l'hôte partagé, qui retiendrait chaque ligne de la série.
    Le gestionnaire **primaire** y est substitué (`ConfigurePrimaryHttpMessageHandler`), pour que
    toute la chaîne de journalisation du client s'exécute comme en production. Un fournisseur
    de capture, avec sa règle propre `AddFilter<FournisseurCapture>(null, LogLevel.Trace)` — elle
    l'emporte sur le `Warning` de `appsettings.json` —, enregistre les messages et
    `state.ToString()` de chaque `BeginScope`, **sans** `ISupportExternalScope`. Après une
    localisation d'une adresse portant un jeton unique, aucune ligne ne contient ce jeton, ni sa
    forme encodée, ni `geocodage/search?`. Sabotage : `RemoveAllLoggers()` retiré, le test
    rougit. Il éprouve le client lui-même, pas tout à fait la console.
  - **Le réglage de `appsettings.json`**, lu dans le fichier : `System.Net.Http.HttpClient` y
    vaut `Warning`, et `appsettings.Development.json` ne le redéfinit pas plus bas.
- .NET masque déjà la requête des URL dans les traces d'`IHttpClientFactory` : un troisième
  filet, que le commutateur `System.Net.Http.DisableUriRedaction` ferait tomber. Il ne dispense
  pas des deux autres, et c'est pourquoi le test cherche aussi `geocodage/search?`.

## L'écran

### Entreprise

- Un champ `geocodageActif`, type `booleen`, libellé « Localiser les sites par la
  Géoplateforme de l'IGN ».
- Aide, ce qu'il faut savoir pour décider (art. 5(2) et 24) : « La voie, le code postal et la
  commune d'un site partent vers la Géoplateforme de l'IGN, établissement public, à chaque clic
  sur Localiser dans la fiche d'un site. Rien d'un agent, aucun nom. L'IGN en est responsable
  sous ses propres conditions ; la durée de ses journaux n'est pas publiée. Pour un client
  particulier, son adresse est une donnée personnelle : voir RGPD.md. »
- `descriptions.test.ts` l'exige déjà, puisqu'il compare les champs aux DTOs de création ; une
  invariante de plus vérifie que la description `entreprises` porte `geocodageActif` en
  `booleen`, sinon le nom que lit la page n'est lié à rien.

### La fiche d'un site

- **Une propriété déclarative nouvelle, `localiser`**, ajoutée à `DescriptionRessource` dans
  `types/reference.ts` et posée sur la description des sites :
  `{ adresse: 'adresse', codePostal: 'codePostal', ville: 'ville', latitude: 'latitude', longitude: 'longitude' }`.
  La route se déduit de celle de la description (`${route}/localiser`), et un test la fige.
  Aucune page écrite à la main (§11).
- **Le champ Adresse des sites** gagne une aide : « Voie et numéro, sans nom de personne. » Le
  champ est un texte libre et part tel quel ; filtrer un nom côté serveur n'est pas réaliste.
- **Qui fait quoi** (§11 : la page orchestre, le composant présente) :
  - **`Reference.tsx` lit le réglage**, dans un effet sur le motif de `comptes` : seulement si
    `ecriture` **et** `description.localiser !== undefined` — sans la seconde condition, chaque
    écran en écriture lirait l'entreprise ; et sans la première, le `ClientSyndic`, à qui la
    fiche d'un site est ouverte et `GET /api/entreprises` fermé, recevrait un 403 à chaque
    ouverture. Route par `descriptionDe('entreprises').route`, `AbortController`, état initial
    `false` ; une lecture en échec cache le bouton, sans message. L'écran est remonté par
    `key={description.cle}` : le réglage est relu à chaque entrée dans Sites.
  - Réglage coché, la page passe à `FormulaireRessource` la prop
    `onLocaliser: (corps: CorpsLocalisation, signal: AbortSignal) => Promise<ResultatLocalisation>`,
    qui appelle `api.post` et laisse l'`ErreurApi` remonter ; sans réglage, aucune prop.
  - **`FormulaireRessource` tient les valeurs** (`useState(depart)`) et l'état de la
    localisation — en cours, trouvée, refusée. Il rend, juste après le champ de longitude, un
    sous-composant `Localisation` qui porte le bouton, son aide et le résultat. Le signal est
    abandonné au démontage : un clic sur une autre ligne remonte le formulaire pendant l'appel.
- **Le bouton** : `type="button"` ; libellé « Localiser » ; aide dessous : « Envoie la voie, le
  code postal et la commune à l'IGN. Pour le domicile d'un particulier, c'est une donnée
  personnelle : mentionnez-le dans l'information que vous lui donnez. » Cible tactile ajoutée à
  la liste de `FormulaireRessource.css`.
  - Adresse vide : bouton **actif** ; le clic donne un refus local en `role="alert"` et rend le
    focus au champ adresse. Un bouton inactif sans explication serait muet au clavier.
  - Pendant l'appel : `aria-disabled="true"`, clics ignorés, libellé « Localisation… » ; jamais
    `disabled`, qui ferait perdre le focus.
- **Le résultat** :
  - latitude et longitude posées **en chaînes**, arrondies à six décimales — le `pas` des
    champs —, sans notation exponentielle, `-0` rendu `"0"` : les champs nombre et `corpsDe` ne
    lisent que des chaînes, et un double non arrondi ferait refuser l'enregistrement par le
    navigateur ;
  - une zone `<output>` **toujours rendue**, dont le texte devient « Latitude et longitude
    renseignées. Trouvé : 20 Avenue de Ségur 75007 Paris », suivi de « — à vérifier : le point
    est approximatif » quand `aVerifier`, en mots, en `--texte` et graisse 600, jamais en
    `--alerte` (§10, §12). Le focus reste sur le bouton ;
  - un refus du serveur en `role="alert"`, précédé de « Refusé — » comme `formulaire__erreur`,
    avec le message du serveur tel quel ;
  - ⚠️ **la ligne « Trouvé » ne ment jamais** : elle s'efface dès que l'un des cinq champs de
    `localiser` est modifié à la main, et une réponse arrivée après une telle modification est
    ignorée. L'écriture faite par la localisation passe par `setValeurs` directement, et ne
    s'efface donc pas elle-même.
- **`reference/localiser.ts`**, pur, sans import, porte ce qui se décide hors du composant :
  - le refus local, avec les bornes lues dans `longueurMax` des champs de la description ;
  - le corps envoyé : valeurs rognées, code postal et ville vides envoyés à `null` ;
  - la chaîne d'une coordonnée ;
  - la ligne « Trouvé : … », avec et sans `aVerifier` ;
  - le message d'un refus : `ErreurApi`, erreur ordinaire, et annulation, qui n'affiche rien ;
  - la lecture du réglage, `geocodageActif === true`, page vide et valeur absente comprises.

  Sa série est **ajoutée à la liste de `frontend/scripts/tests.mjs`** : hors de la liste, elle
  ne tournerait pas, sans aucune erreur.

### L'assistant d'installation

`pages/Installation.tsx`, dernier écran : les lignes 300 à 302 deviennent « La
**localisation automatique des sites** est désactivée : à activer dans Entreprise, qui dit ce
qui part vers l'IGN. »

## Les documents

- **`RGPD.md`** — ce qui suit est une **lecture**, à faire valider par le juriste que le
  fichier prévoit déjà ; 1.3 et 1.4 d'abord, car l'un ou l'autre, s'il tombe, remet en cause la
  fonction et pas seulement sa documentation :
  - **§1, la qualification de l'IGN** : **destinataire tiers, responsable distinct de ses
    propres traitements, et non sous-traitant** ni responsable conjoint. Fondée sur ce que fait
    l'IGN, pas sur l'absence de contrat — qui découle de la qualification et ne la fonde pas
    (EDPB, lignes directrices 07/2020) : il rend un service public de diffusion d'une donnée de
    référence, la Base Adresse Nationale, à l'identique à tout demandeur ; il décide seul de ce
    qu'il garde des requêtes, pourquoi et combien de temps ; il ne garde rien pour le compte de
    l'exploitant. Pas de responsabilité conjointe : l'exploitant décide seul d'envoyer, l'IGN
    seul de ce qui suit. CGU et page « données personnelles » lues le 23 novembre 2026, la
    qualification à relire si elles changent.
  - **§1, le risque résiduel**, écrit plutôt que tu : une autorité pourrait voir le calcul des
    coordonnées comme un traitement « pour le compte » de l'exploitant ; l'art. 28 exigerait alors
    un contrat que les CGU ne fournissent pas, et la fonction serait à désactiver. Ce qui
    l'atténue : aucun nom ne part ; pour un bureau ou un immeuble, aucune donnée personnelle ; la
    fonction est désactivée par défaut.
  - **§1, les personnes concernées** : une catégorie de plus, les occupants d'un site qui est un
    domicile, souvent le client particulier lui-même.
  - **T6** : le géocodage comme moyen de localiser les sites ; une ligne « Destinataires » —
    l'IGN, si activé, à chaque clic ; **la base de l'envoi, art. 6(1)(f)**, avec sa mise en
    balance en trois lignes — l'art. 6(1)(b) est fragile, la saisie à la main restant possible ;
    et les trois cas : bureau ou immeuble, pas de donnée personnelle selon cette lecture ; maison
    individuelle, donnée personnelle ; IP du serveur, donnée personnelle pour un entrepreneur
    individuel seulement. L'appel part du serveur : l'IP des utilisateurs ne part jamais.
  - **§3** : la phrase « aucun appel sortant hors SMTP » réécrite ; le journal technique, l'URL
    sortante qui porte une adresse, et ses deux gardes.
  - **§4** : « trois sorties » devient quatre ; la sortie vers l'IGN — ce qui part, sur quel
    geste, désactivée par défaut, depuis le serveur seul.
  - **§5** : une ligne « requête de localisation ». Côté KLEAN, rien n'est conservé, ni la
    requête ni l'adresse trouvée, et les coordonnées suivent la durée du site ; côté IGN, « non
    publiée, lu le 23 novembre 2026 ». Une question écrite à `dpo@ign.fr` est proposée à
    l'auteur, sa réponse datée à verser.
  - **§6** : pour un client particulier, l'IGN fait partie des destinataires à citer en réponse
    à une demande d'accès (art. 15(1)(c)), encadré par `GeocodageActiveLe` et
    `GeocodageDesactiveLe`. La notification de l'art. 19 est sans doute impossible, l'IGN ne
    recevant aucun nom (lecture).
  - **§6 ou §11, l'information des clients particuliers** (art. 13(1)(e), 14(1)(e)) : une phrase
    modèle pour les conditions ou la notice que l'exploitant remet à ses clients — « L'adresse du
    lieu d'intervention peut être transmise, sans votre nom, à l'IGN (Géoplateforme) pour en
    calculer les coordonnées. »
  - **§7, la note aux agents** : « L'adresse de votre domicile n'est jamais envoyée à un service
    extérieur. » Elle tombera si les domiciles sont un jour géocodés.
  - **§9.6, l'AIPD** : aucun critère nouveau (lecture), mais ce flux entre dans sa description
    du traitement.
  - **§10** : HTTPS exigé au démarrage, redirections et cookies refusés, aucun `traceparent`,
    `User-Agent` générique, réponse plafonnée, aucun journal du client HTTP.
  - **§11, côté exploitant** : décider l'activation ; mettre à jour l'information des clients
    particuliers ; garder les CGU datées ; reporter l'IGN dans son registre (art. 30(1)(d) et
    (e)) ; ouvrir la sortie 443 vers `data.geopf.fr` sur un pare-feu sortant.
  - **Transferts** : « aucun transfert hors UE connu ; non vérifié au-delà des mentions légales
    — réseau de diffusion, sous-traitants de l'IGN ».
- **`CLAUDE.md`** : la route, le service et son client nommé, les plafonds et le disjoncteur, le
  réglage et ses dates, le journal technique et ses deux gardes ; `ServiceGeocodage` dans
  l'arborescence ; `KLEAN_GEOCODAGE` retiré ; et dans §7 bis, la décision de ne pas journaliser
  la route, avec sa condition de bascule.
- **`DEPLOIEMENT.md`** : la sortie vers `data.geopf.fr` en 443, ligne 53 (« aucun accès
  sortant ») ; un proxy de l'environnement (`HTTPS_PROXY`) voit l'hôte, pas le contenu ; les
  lignes 175-176, qui annoncent le géocodage et parlent de `KLEAN_GEOCODAGE`, réécrites.
- **`README.md`** : une ligne dans la liste des sorties, si elle existe.
- **`SPEC.md`** : le compte rendu « 0.3.0 — fait le … », à la publication.

## Les tests

**Serveur** (`backend.Tests`). La série substitue le gestionnaire primaire du client nommé
(`ConfigurePrimaryHttpMessageHandler`, dans `FabriqueApi`) : toute la chaîne du client
s'exécute.

- **Les issues** : trouvé, avec `AVerifier` faux ; score bas ; score absent ; `type` `street` ;
  introuvable (`features: []`) ; injoignable (`HttpRequestException`) ; délai dépassé, sur un
  hôte dérivé où `DelaiSecondes` vaut 1 ; **en-têtes immédiats et corps qui ne finit pas**, 503
  dans le délai ; 429 ; 302 rendu en 503 ; 400 et 404 du service ; 500 ; 200 sans JSON valide ;
  réponse de plus de 64 Kio ; latitude hors bornes ; latitude NaN ; `coordinates` d'un seul
  élément ; `label` qui porte un caractère de bidirectionnalité, retiré.
- **La configuration de production du client** : `Enregistrer` sur une `ServiceCollection`
  neuve, avec `IConfiguration` ; `CreateHandler("geocodage")`, puis la chaîne des
  `InnerHandler` jusqu'au gestionnaire primaire, qui doit être un `SocketsHttpHandler` avec
  `AllowAutoRedirect = false`, `UseCookies = false`, le propagateur sans sortie et
  `AutomaticDecompression = None`. **Un sabotage par ligne**, chacun le fait rougir — sauf
  `AutomaticDecompression`, déjà `None` par défaut, gardée comme déclaration explicite — c'est le
  seul test qui le prouve : le faux gestionnaire ne suit rien, ne garde aucun cookie et n'ajoute
  aucun en-tête de lui-même.
- **La requête sortante**, avec une saisie hostile — `&`, `#`, `?`, `%`, `+`, `//hote`,
  `1&limit=50` en code postal, de l'Unicode : l'hôte et le chemin sont exactement ceux
  configurés ; la requête, analysée, porte `q`, `limit`, `index` et `postcode` seulement s'il a
  cinq chiffres, et rien d'autre ; **l'ensemble exact des en-têtes** est `User-Agent: KLEAN` et
  `Host`.
- **Le démarrage** : `Lire` refuse une adresse en `http`, relative, illisible, avec
  identifiants, requête ou fragment, et un délai hors de 1 à 20 ou illisible ; et
  `Enregistrer` sur une configuration invalide lève — sans ce second test, retirer l'appel à
  `Lire` dans `Enregistrer` laisserait la série verte.
- **La route** : réglage désactivé, 409, **aucun appel sortant** ; **l'entreprise A a coché,
  l'administrateur de B non : 409 pour B, aucun appel** — le test seul du réglage désactivé ne
  distingue pas une lecture mal cadrée ; champs refusés en français, dont l'adresse vide, un
  caractère de contrôle, et une recherche de plus de 200 caractères, sans appel sortant ; un test
  par rôle refusé (Responsable, ChefEquipe, Agent, ClientSyndic) ; le plafond par compte et le
  plafond global, avec `Retry-After` ; le disjoncteur — un 429 du service, puis une seconde
  localisation répond 503 sans appel.
- **Le réglage** : `PUT` à `true`, puis `GET` et lecture en base ; `GeocodageActiveLe` posée au
  passage à `true`, `GeocodageDesactiveLe` au retour, aucune date touchée par un `PUT` qui ne
  change pas le réglage.
- **La migration** : une entreprise insérée **par SQL, sans les trois colonnes**, depuis une
  `PorteeAuProprietaire()` — sous `klean_app`, `Entreprises` refuserait en `42501` (§10) —, avec
  `Id`, `RaisonSociale`, un `Siret` libre (`IX_Entreprises_Siret`) et `DateCreation`, relue en
  base, porte `false` et deux dates nulles. Une entreprise créée par EF ne prouve rien : EF écrit
  explicitement le `false` du CLR.
- **Le journal technique** : les deux tests de la section du même nom.

**Écran** :
- la série de `localiser.ts`, ajoutée à `scripts/tests.mjs` ;
- dans `descriptions.test.ts`, l'invariante de `localiser` : chaque nom désigne un champ ;
  `adresse`, `codePostal` et `ville` en `texte` ; `latitude` et `longitude` en `nombre`, avec
  `pas` ; cinq noms distincts ; **la propriété n'est posée que sur la description `sites`** —
  les agents ont les mêmes noms de champs, et la poser sur eux enverrait des domiciles de
  salariés à l'IGN, en silence (art. 25(1)). Un sabotage la fait rougir ;
- la description `entreprises` porte `geocodageActif` en `booleen` ;
- les trois aides nouvelles — Entreprise, bouton, champ Adresse — gardées par un test.

**Pour de vrai** : sur le VPS, réglage coché, une adresse publique localisée depuis la fiche
d'un site, puis enregistrée ; le réglage décoché, le bouton disparu ; `GeocodageActiveLe` et
`GeocodageDesactiveLe` relues en base.

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
- L'atomicité de `LimiteurDebit` : `MemoryCache.GetOrCreate` n'est pas atomique, et une
  première rafale sur une clé peut laisser passer un peu plus que le plafond. Le défaut est
  antérieur et touche tous les plafonds ; le plafond global le rend sans gravité ici. Consigné
  dans `SPEC.md`, à reprendre à part.
