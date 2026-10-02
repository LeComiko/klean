# RGPD — registre, cartographie et conservation

Lot D de l'étape 17. Ce fichier décrit **le code tel qu'il est**, pas une intention :
chaque affirmation sur une table, une colonne ou un endpoint est vérifiable dans le
dépôt, et les manques sont listés au §9 plutôt que tus.

⚠️ **Ce fichier ne remplace pas une relecture juridique.** Les bases légales et les
durées du §5 sont des **propositions motivées**, pas des avis. Elles sont écrites pour
qu'un juriste ait quelque chose à corriger, ce qui est plus utile qu'une page blanche —
et moins dangereux qu'une page blanche doublée d'un logiciel qui tourne.

---

## 1. Qui est responsable de quoi

KLEAN est un **logiciel**, pas un service. Il n'existe aucune instance centrale,
aucun serveur de l'auteur, aucune télémétrie. Cela décide des rôles :

| Rôle RGPD | Qui |
|---|---|
| **Responsable de traitement** | l'entreprise de propreté qui exploite l'instance |
| **Sous-traitants** | son hébergeur, son fournisseur SMTP s'il en configure un, et — aujourd'hui — Google (voir §9.1) |
| **L'auteur du logiciel** | ni responsable, ni sous-traitant : il ne traite aucune donnée, n'y a aucun accès et n'en reçoit aucune |

⚠️ **Une entreprise qui héberge KLEAN pour d'autres entreprises devient leur
sous-traitant** et doit conclure l'accord de l'article 28. Le multi-tenant du §7 de
`CLAUDE.md` rend ce montage techniquement possible — une instance, plusieurs
`EntrepriseId` — et c'est précisément pour cela qu'il faut le dire : la séparation
logique des données n'est pas une dispense de contrat.

La licence AGPL n'a aucun effet sur ces rôles. Elle oblige à publier les
modifications ; elle ne transfère aucune responsabilité de traitement.

**Personnes concernées**, trois catégories :

1. **Les agents** — salariés de l'entreprise. La catégorie la plus exposée : nom,
   adresse, **coordonnées GPS du domicile**, absences.
2. **Les utilisateurs** — titulaires d'un compte de connexion. Souvent les mêmes
   personnes, dans un autre rôle : un agent peut avoir un compte, un responsable n'a
   pas de fiche agent.
3. **Les contacts clients** — nom, courriel et téléphone d'un interlocuteur chez le
   donneur d'ordre. Données **professionnelles**, et pas toujours celles d'une
   personne morale : voir §8.3.

---

## 2. Registre des traitements

Six traitements. Chacun existe réellement dans le code, et l'endpoint ou le service
qui le porte est nommé.

### T1 — Planification des interventions

| | |
|---|---|
| **Finalité** | affecter des agents à des vacations datées sur les sites sous contrat |
| **Base légale proposée** | art. 6(1)(b), exécution du contrat de travail et du contrat de prestation |
| **Personnes** | agents |
| **Données** | identité, affectation, dates et heures de passage, statut du passage |
| **Tables** | `Interventions`, `Prestations`, `Contrats`, `Sites` |
| **Accès** | rôles internes (`Administrateur`, `Responsable`, `ChefEquipe`) ; l'agent voit ses propres interventions, cadré par l'axe `AgentId` |
| **Destinataires** | aucun hors de l'entreprise |
| **Durée** | voir §5 |
| **Journalisé** | `GET /api/planning` — oui, parce qu'il nomme **tous** les agents de l'entreprise |

### T2 — Recherche d'un remplaçant

| | |
|---|---|
| **Finalité** | proposer des candidats classés quand une vacation n'est pas pourvue |
| **Base légale proposée** | art. 6(1)(f), intérêt légitime — assurer la continuité du service |
| **Personnes** | agents |
| **Données** | identité, **coordonnées GPS du domicile** (via la distance au site), nombre de passages antérieurs sur le site, heures de la semaine |
| **Services** | `AffectationService`, `RemplacementService` |
| **Accès** | rôles internes ; les propositions et acceptations sont réservées à `Exploitation` |
| **Durée** | voir §5 |
| **Journalisé** | oui, par **deux** routes — `/api/interventions/{id}/candidats` et `/api/remplacements/{id}/candidats` |

⚠️ **C'est le traitement le plus sensible du produit, et le moins évident.** Le
`CandidatDto` expose `DistanceKm`, dérivée des coordonnées du domicile de l'agent.
Stocker le domicile d'un salarié pour calculer une distance relève de la doctrine CNIL
sur la géolocalisation des employés. La position de ce projet, à assumer par écrit
auprès des agents : **la donnée sert l'affectation, jamais le suivi.** Deux faits la
soutiennent dans le code, et il faut les tenir :

- la position n'est **jamais historisée** : `Agent.Latitude` et `Agent.Longitude` sont
  deux colonnes écrasables, pas une trace. Aucune table ne garde où un agent était.
- la distance n'est calculée **qu'entre le domicile et un site**, jamais entre deux
  positions dans le temps.

⚠️ **Aucune décision individuelle automatisée au sens de l'article 22.** Le score
sur 100 est un **classement**, pas une décision : `ProposerAsync` et `AccepterAsync`
sont deux gestes humains distincts, et il n'existe **aucun mode de remplacement
automatique** (`CLAUDE.md` §9 — « sans notifications ni application agent, il n'y a
personne à contacter »). Si un tel mode était ajouté un jour, cette ligne du registre
devient fausse et l'article 22 s'applique.

### T3 — Gestion des absences

| | |
|---|---|
| **Finalité** | savoir qu'un agent est indisponible, pour déclencher la recherche de remplaçant |
| **Base légale proposée** | art. 6(1)(b) et, pour le type `Maladie`, **art. 9(2)(b)** — obligations en matière de droit du travail |
| **Personnes** | agents |
| **Données** | type, dates, et un motif en texte libre **sauf sur un arrêt maladie** |
| **Tables** | `Absences` |
| **Accès** | lecture par les rôles internes, écriture par `Exploitation` |
| **Durée** | voir §5 — et §8.2 pour le résidu |
| **Journalisé** | oui, en liste et en lecture unitaire |

⚠️ **Le motif libre est refusé par le serveur sur un arrêt maladie**, et c'est le
seul arbitrage de donnée de santé que ce produit ait tranché en code :
`AbsencesController` répond 400 en disant pourquoi. Ce n'est donc pas une convention
d'interface qu'un appel direct contournerait.

⚠️ **Et le planning divulgue une absence sans qu'on lise les absences.**
`PlanningNonPourvueDto.Cause` vaut littéralement « Agent absent ». C'est pour cela que
`GET /api/planning` est journalisé.

### T4 — Comptes et authentification

| | |
|---|---|
| **Finalité** | authentifier, autoriser, et pouvoir révoquer |
| **Base légale proposée** | art. 6(1)(b) et art. 6(1)(c) combiné à l'art. 32 — obligation de sécurité |
| **Personnes** | utilisateurs |
| **Données** | adresse de courriel, nom, prénom, téléphone, **empreinte du mot de passe**, rôle, compteur d'échecs et fin de verrouillage, **empreinte** du jeton de rafraîchissement, **adresse IP** de l'ouverture de session |
| **Tables** | `AspNetUsers`, `AspNetUserRoles`, `JetonsRafraichissement` |
| **Accès** | aucune route ne **liste** les comptes. `GET /api/auth/moi` rend le sien ; `POST /api/auth/comptes` en crée un, administrateur seul |
| **Durée** | voir §5 — et §9.2 pour l'adresse IP |
| **Journalisé** | non, et délibérément : `ProfilDto` est l'identité de l'appelant lui-même, un journal de « qui a lu son propre nom » est du bruit |

⚠️ **Ni le mot de passe ni le jeton de rafraîchissement ne sont stockés** : seules
leurs empreintes le sont, SHA-256 pour le second. Une fuite de `JetonsRafraichissement`
ne permet pas de rejouer les sessions.

### T5 — Journal d'accès aux données personnelles

| | |
|---|---|
| **Finalité** | rendre compte de qui a lu les données d'agents et d'absences |
| **Base légale proposée** | art. 6(1)(c) combiné aux art. 5(2) et 32 — responsabilité et sécurité |
| **Personnes** | utilisateurs (ceux qui lisent), et indirectement les agents (par `CibleId`) |
| **Données** | identifiant de l'utilisateur, ressource, nature de l'accès, identifiant de la cible pour une lecture unitaire, **nombre** de lignes pour une liste, horodatage |
| **Table** | `AccesJournalises` |
| **Accès** | `GET /api/journal-acces`, **administrateur seul**, lecture seule |
| **Durée** | **12 mois**, par `backend/Scripts/purge-journal-acces.sql` |
| **Journalisé** | oui — lire le journal laisse une ligne dans le journal |

⚠️ **Le journal ne contient jamais ce qui a été lu** : ni nom, ni adresse, ni motif,
ni liste d'identifiants. Un journal qui recopie ce qu'il surveille devient une seconde
base de données personnelles, moins protégée que la première. **Et aucune adresse IP** :
une donnée de plus à justifier, qui n'ajoute rien à « qui a lu quoi » dans un produit
où chaque compte est nominatif.

⚠️ **Il est fermé au `Responsable` et au `ChefEquipe`.** Ouvert à eux, il deviendrait
un outil de surveillance des collègues — ce qu'un journal RGPD n'est pas censé être.

### T6 — Relation client

| | |
|---|---|
| **Finalité** | joindre l'interlocuteur du donneur d'ordre, et localiser les sites |
| **Base légale proposée** | art. 6(1)(b) et art. 6(1)(f) |
| **Personnes** | contacts clients |
| **Données** | nom, adresse, courriel, téléphone du client ; nom, adresse et coordonnées GPS du site ; **consignes en texte libre** |
| **Tables** | `Clients`, `Sites` |
| **Accès** | rôles internes et `ClientSyndic`, ce dernier cadré sur son propre `ClientId` |
| **Durée** | voir §5 |
| **Journalisé** | **non** — exemption motivée, voir §8.3 |

---

## 3. Cartographie des données

Les douze tables du modèle, plus les tables d'Identity. « DP » = donnée personnelle.

| Table | Colonnes porteuses de DP | Catégorie | Sort par |
|---|---|---|---|
| `Entreprises` | `RaisonSociale`, `Siret` | personne morale — pas une DP | `GET /api/entreprises` |
| `Agents` | `Nom`, `Prenom`, `Email`, `Telephone`, `Adresse`, **`Latitude`**, **`Longitude`**, `DateEmbauche` | identité, **localisation du domicile** | `/api/agents`, et **`/api/planning`** et les deux routes de candidats |
| `Absences` | `AgentId`, `Type`, dates, `Motif` | **art. 9 quand `Type = Maladie`** | `/api/absences`, et la cause « Agent absent » du planning |
| `Interventions` | `AgentId`, `DatePrevue`, `HeurePrevue`, `Statut` | activité professionnelle | `/api/interventions`, `/api/planning` |
| `Remplacements` | `AgentRemplacantId`, `Score` | **évaluation** d'une personne | `/api/remplacements` |
| `Clients` | `Nom`, `Email`, `Telephone`, `Adresse` | contact professionnel — voir §8.3 | `/api/clients` |
| `Sites` | `Adresse`, coordonnées, **`Consignes`** | lieu, et DP par accident dans les consignes | `/api/sites` |
| `Contrats`, `Prestations` | aucune — `Prestation.Libelle` est un intitulé de prestation | — | `/api/contrats`, `/api/prestations` |
| `Produits`, `Stocks` | aucune | — | — |
| `AccesJournalises` | `UtilisateurId`, `CibleId`, `Horodatage` | trace d'activité d'un utilisateur | `/api/journal-acces` |
| `AspNetUsers` | `Email`, `UserName`, `PhoneNumber`, `Nom`, `Prenom`, `PasswordHash`, verrouillage | identité et **secret d'authentification** | `/api/auth/moi` seulement |
| `JetonsRafraichissement` | `UtilisateurId`, **`AdresseIp`**, dates | trace de connexion | **aucune route** |

⚠️ **Deux colonnes sont des données personnelles que personne ne cherche là.**

- **`Sites.Consignes`** est un texte libre lu par l'agent avant son passage. Rien
  n'empêche d'y écrire « sonner chez Mme Durand, deuxième étage ». La table est
  exemptée de journalisation parce qu'un site est un lieu ; le champ peut faire
  mentir cette exemption. Parade proposée au §9.5.
- **`JetonsRafraichissement.AdresseIp`** est la seule adresse IP du produit, et elle
  n'est jamais purgée. Voir §9.2.

**Ce qui n'existe pas, et qu'il est utile de pouvoir affirmer :** aucune table de
pointage, aucune position historisée, aucun accusé de lecture de consigne, aucun
cookie de mesure d'audience, aucune télémétrie, aucun appel sortant hors SMTP — à une
exception près, §9.1.

---

## 4. Où les données sortent de l'instance

Quatre sorties, et deux seulement sont voulues.

1. **Vers le navigateur de l'utilisateur** — c'est l'usage. Protégé par HTTPS, par
   les trois cookies `HttpOnly Secure SameSite=Strict`, et par la politique de
   sécurité du contenu.
2. **Vers le serveur SMTP**, si l'exploitant en configure un : l'adresse du
   destinataire et un jeton de réinitialisation. **Sous-traitant à déclarer.** Sans
   SMTP, KLEAN démarre et seul ce parcours est désactivé — il répond 503 en disant
   quoi configurer.
3. **Vers l'hébergeur** — la base entière. Sous-traitant à déclarer.
4. ⚠️ **Vers Google**, à chaque chargement de page, sans que l'exploitant l'ait
   choisi. Voir §9.1 : c'est l'écart le plus sérieux de cette revue.

---

## 5. Durées de conservation

⚠️ **Propositions motivées, à valider juridiquement.** La colonne la plus importante
est la dernière : une durée sans mécanisme est une promesse que personne ne tient.

| Nature de la donnée | Durée proposée | Raison | Mécanisme |
|---|---|---|---|
| Fiche agent (`Agents`) | 5 ans après la fin du contrat de travail | aligné sur la conservation des pièces du registre du personnel | **aucun** — la pseudonymisation est un geste manuel |
| Coordonnées GPS du domicile | tant que le contrat de travail court, puis effacement immédiat | elles ne servent qu'à l'affectation : un agent parti n'est plus candidat | **aucun** — mais `/anonymiser` les met à `null` |
| Absences, hors `Maladie` | 5 ans, avec la fiche agent | pièce justificative d'une indisponibilité | **aucun** |
| Absences de type `Maladie` | **3 mois après la date de fin** | voir §8.2 : KLEAN n'est pas un outil de paie, il n'a aucune raison de garder une donnée de santé au-delà du remplacement | **aucun** — mécanisme proposé au §8.2 |
| Interventions et leur statut | 5 ans | preuve des passages facturés, prescription de l'action contractuelle | **aucun** |
| Remplacements et scores | 1 an | état dérivé, sans valeur probante au-delà de l'exercice | **aucun** |
| Clients et sites | durée du contrat, puis 5 ans | relation commerciale | **aucun** |
| Comptes (`AspNetUsers`) | désactivation à la sortie, effacement à 5 ans | un compte désactivé reste nécessaire pour attribuer les lignes du journal | **aucun** |
| Jetons de rafraîchissement | **30 jours après révocation ou expiration** | la fenêtre de diagnostic d'un rejeu ; au-delà, seule l'adresse IP survit, sans usage | **aucun** — voir §9.2 |
| Journal d'accès | **12 mois** | un journal RGPD est lui-même une donnée personnelle | ✅ `purge-journal-acces.sql` |

**Un seul mécanisme sur dix lignes.** C'est l'état réel, et c'est le principal chantier
que ce lot ouvre pour l'étape 18 : les durées ci-dessus demandent un script de purge
construit comme celui du journal — lecture d'abord, suppression commentée, exécuté par
l'exploitation et non par l'application. La raison de ne pas le faire dans le serveur
est la même que pour le journal : une tâche planifiée embarquée existerait en un
exemplaire logique alors que le produit peut tourner en plusieurs instances.

⚠️ **Une purge de données métier n'est pas une purge de journal.** La chaîne
d'exploitation est en `Restrict` depuis l'étape 16 bis : supprimer un contrat dont les
interventions existent échoue. Un script de conservation doit donc remonter la chaîne
dans le bon ordre, et ce n'est pas un détail d'écriture — c'est la différence entre un
script qui purge et un script qui échoue à mi-parcours.

---

## 6. Droits des personnes, et comment ils sont servis

| Droit | Servi aujourd'hui par | État |
|---|---|---|
| Information (art. 13) | rien dans le produit | **manquant** — modèle de note au §7 |
| Accès (art. 15) | `GET /api/agents/{id}` et `GET /api/absences`, par un administrateur | partiel : réponse manuelle, aucun dossier consolidé |
| Rectification (art. 16) | `PUT /api/agents/{id}` | ✅ |
| Effacement (art. 17) | `POST /api/agents/{id}/anonymiser` | partiel — voir §8.1 et §9.3 |
| Limitation (art. 18) | `Actif = false` sur l'agent et sur le compte | approximatif : c'est une désactivation, pas un gel |
| Portabilité (art. 20) | rien | **manquant** — voir §9.4 |
| Opposition (art. 21) | sans objet sur T1, T3, T4 (exécution du contrat) ; recevable sur T2 | à traiter au cas par cas, hors code |

⚠️ **Le délai d'un mois de l'article 12 court même quand l'outil n'aide pas.** Les
deux manques ci-dessus — pas de dossier consolidé, pas d'export — ne sont pas des
manquements en soi : un administrateur peut composer la réponse à la main. Ils
deviennent un manquement le jour où le volume fait dépasser le délai.

---

## 7. Modèle de note d'information aux agents

À adapter, à remettre à chaque agent et à afficher. C'est la contrepartie de T2 :
conserver les coordonnées du domicile d'un salarié est défendable **à condition de le
dire**, et un outil silencieux sur ce point est indéfendable quelle que soit la qualité
de son code.

> **Traitement de vos données dans l'outil de planification**
>
> *[Raison sociale]* utilise le logiciel KLEAN pour planifier les interventions et
> organiser les remplacements.
>
> **Ce qui est enregistré sur vous** : vos nom et prénom, vos coordonnées de contact,
> votre adresse et **les coordonnées géographiques de votre domicile**, votre date
> d'embauche, vos interventions planifiées et réalisées, et vos absences — type et
> dates. **Un arrêt maladie ne porte jamais de motif** : l'outil le refuse.
>
> **À quoi servent les coordonnées de votre domicile** : à calculer la distance entre
> chez vous et un site, lorsqu'il faut trouver un remplaçant. C'est un des trois
> critères de classement des candidats, avec votre connaissance du site et vos heures
> déjà planifiées dans la semaine. **Votre position n'est jamais suivie ni
> enregistrée dans le temps** : l'outil ne connaît que l'adresse que vous avez
> communiquée.
>
> **Qui décide** : une personne, toujours. Le classement est une aide ; la proposition
> et l'affectation sont des décisions humaines.
>
> **Qui y a accès** : la direction et les responsables de planning. Les accès à vos
> données sont enregistrés — qui a consulté, quand — et cet enregistrement n'est
> consultable que par la direction.
>
> **Combien de temps** : *[reporter les durées du §5 validées]*.
>
> **Vos droits** : accès, rectification, effacement, limitation, opposition et
> réclamation auprès de la CNIL. Contact : *[adresse]*.

---

## 8. Les trois questions que les lots A et B avaient laissées ouvertes

### 8.1 — « Anonymisation » est le mauvais mot, et le registre doit être exact

`POST /api/agents/{id}/anonymiser` est une **pseudonymisation au sens de l'article
4(5)**, pas une anonymisation au sens de l'article 4(1). L'`Id` de l'agent survit,
parce que toutes ses interventions passées en dépendent : la ligne reste rattachable à
une personne par quiconque détiendrait une correspondance externe.

**Conséquence pour le registre, et elle n'est pas cosmétique :** une donnée
pseudonymisée **reste une donnée personnelle**, donc elle reste dans le périmètre du
RGPD, donc elle garde une durée de conservation. Déclarer une anonymisation
laisserait croire l'inverse et sortirait à tort ces lignes du registre.

La route garde le mot « anonymiser » parce que c'est l'intention de l'exploitant, et
`ServiceAnonymisation` inscrit la qualification dans la liste `Subsiste` du compte
rendu, article cité. **Tranchée : le registre dit pseudonymisation, partout.**

### 8.2 — Le résidu de santé des absences `Maladie`

Après `/anonymiser`, le `Motif` d'une absence est effacé — mais une absence de type
`Maladie` **garde son type et ses dates**, et cela reste une donnée de santé. Supprimer
la ligne casserait l'historique des remplacements qui en découlent.

C'est donc une question de **durée de conservation**, et elle se tranche sans rien
casser : ce que l'historique des remplacements a besoin de savoir, c'est **qu'il y
avait un trou**, pas pourquoi.

**Proposition, à confirmer avant écriture :** trois mois après la date de fin d'une
absence, basculer `Type` de `Maladie` vers `Indisponibilite` — valeur qui existe déjà
dans l'énumération — en conservant l'agent et les dates. La donnée de santé disparaît,
la chaîne de remplacement reste entière, et aucune clé étrangère n'est touchée.

Trois mois parce que **KLEAN n'est pas un outil de paie** (`CLAUDE.md` §1 le met hors
périmètre) : il n'a donc aucune justification pour conserver un motif d'arrêt au-delà
du remplacement et de la contestation du mois concerné. C'est le logiciel de paie, pas
celui-ci, qui porte les obligations longues sur les arrêts de travail.

⚠️ **Deux réserves à ne pas cacher.** Le basculement est **irréversible** et porte sur
des lignes métier : il demande donc le même soin qu'une migration. Et il ne rend pas la
donnée indevinable — une indisponibilité de trois semaines reste une indisponibilité de
trois semaines. Il supprime la **qualification** de santé, qui est ce que l'article 9
protège, pas la possibilité d'une inférence.

### 8.3 — `ClientDto` et `SiteDto` sont-ils des données personnelles ?

La question était consignée dans `JournalAccesTests` sans être tranchée : le contact
d'une petite copropriété est souvent une personne physique.

**Tranchée : l'exemption de journalisation est maintenue, et sa raison est réécrite.**
Elle ne repose plus sur « c'est une personne morale » — c'est faux pour une copropriété
— mais sur ceci : les colonnes de `Clients` sont des **coordonnées professionnelles
d'un interlocuteur dans l'exécution d'un contrat**. Le journal d'accès de T5 existe
pour un risque précis et nommé : la surveillance des salariés par leur employeur.
Y verser chaque lecture de fiche client noierait les divulgations qui comptent — et un
journal bruyant ne se lit plus, ce qu'un second test garde explicitement.

**La condition qui ferait tomber l'exemption**, écrite pour qu'un relecteur futur
puisse l'appliquer sans rouvrir le débat : **l'ajout à `Client` d'une colonne qui ne
soit pas une coordonnée professionnelle** — date de naissance, adresse personnelle
distincte du site, champ de commentaire libre. Une telle colonne sortirait du
raisonnement ci-dessus, et le filet par réflexion de `JournalAccesTests` la
rattraperait de lui-même : il faudrait alors inscrire une exemption, et on ne pourrait
plus.

**`SiteDto` est le cas plus faible, et c'est `Consignes` qui l'affaiblit** — texte
libre pouvant nommer une personne. La parade proportionnée n'est pas de journaliser
chaque lecture de site, mais d'empêcher que la donnée y entre. Voir §9.5.

---

## 9. Écarts constatés

Classés par gravité. Aucun n'est corrigé par ce lot, qui est de la documentation ;
chacun est écrit pour être pris ou refusé en connaissance de cause.

### 9.1 — Les polices sont chargées chez Google, à chaque page

`frontend/index.html` charge Figtree et IBM Plex Mono depuis `fonts.googleapis.com` et
`fonts.gstatic.com`. Chaque chargement de page transmet donc à Google LLC l'**adresse
IP** du navigateur, son agent et le référent — sans que l'exploitant l'ait choisi, et
pour toute instance de KLEAN.

⚠️ **Cela contredit directement le §2 de `CLAUDE.md`** : « pas de dépendance
propriétaire, pas de service tiers obligatoire ». Google Fonts **est** un service tiers
obligatoire à l'exécution, et il a échappé à cette règle parce qu'il n'apparaît pas
dans `package.json` — la règle avait été lue comme portant sur les dépendances de
construction.

C'est l'écart le plus sérieux de cette revue, parce qu'il est le seul qui concerne
**tous les utilisateurs de toutes les instances** et le seul qui soit un transfert à un
tiers. **Correction proposée** : verser les deux familles dans `frontend/public/`, les
déclarer en `@font-face`, et refermer `style-src` et `font-src` sur `'self'` dans
`frontend/csp.ts` — ce qui supprime les deux seules origines externes des deux
politiques. Aucun compromis d'interface : §12 impose ces deux familles, pas leur
provenance.

### 9.2 — L'adresse IP des sessions n'est jamais purgée

`JetonsRafraichissement.AdresseIp` est renseignée à chaque ouverture de session et à
chaque rotation — donc plusieurs fois par jour et par utilisateur. **Rien ne supprime
ces lignes**, ni l'expiration, ni la révocation, ni la déconnexion : elles s'accumulent
indéfiniment avec leur adresse.

L'ironie est à nommer : le journal d'accès du lot A refuse délibérément de stocker une
adresse IP (« une donnée personnelle de plus, dont la conservation demanderait sa propre
justification »), et la table de sessions en garde l'historique complet. La colonne a
son usage — diagnostiquer un rejeu — mais cet usage dure des jours, pas des années.

**Correction proposée** : un script de purge des jetons révoqués ou expirés depuis plus
de trente jours, sur le modèle de `purge-journal-acces.sql`.

### 9.3 — Aucun chemin d'effacement pour un compte sans fiche agent

`ServiceAnonymisation.ExecuterAsync` part d'un `agentId` et ne touche `AspNetUsers` que
si un compte porte cet `AgentId`. Un compte de rôle `Responsable`, `Administrateur` ou
`ClientSyndic` a `AgentId = null` : **aucun endpoint ne permet d'effacer ses données.**
Son nom, son prénom et son courriel restent en base indéfiniment, et il n'existe même
pas de route pour le désactiver — la fixture de tests le fait directement en base,
faute d'endpoint.

Ce n'est pas un oubli du lot B, qui traitait la demande d'effacement d'un **agent** ;
c'est un trou du modèle de droits, et il concerne les personnes les mieux placées pour
s'en apercevoir.

### 9.4 — Aucun export, donc aucune portabilité

Rien ne compose un dossier consolidé pour une personne. Un administrateur peut
reconstituer la réponse à une demande d'accès à partir de quatre ou cinq appels ; il n'y
a pas d'export au sens de l'article 20. Acceptable à l'échelle visée — 5 à 150 agents —
tant que le volume de demandes reste faible.

### 9.5 — `Sites.Consignes` peut nommer une personne, et rien ne le décourage

Le champ est aujourd'hui décrit par « Accès, codes, matériel sur place. Lues par
l'agent avant son passage. » Rien n'y dissuade d'écrire le nom d'un gardien ou d'un
occupant.

**Correction proposée**, d'une ligne : ajouter à l'aide du champ, dans
`frontend/src/reference/descriptions.ts`, qu'il ne doit pas nommer de personne. C'est
une mesure de minimisation à la source, infiniment moins coûteuse que de journaliser
toutes les lectures de sites — et la seule qui agisse sur la cause.

### 9.6 — Une AIPD est probablement requise

Au titre de l'article 35(3) et de la liste de la CNIL, trois critères au moins sont
réunis :

1. **Données de l'article 9** — le type `Maladie` des absences.
2. **Données concernant des personnes vulnérables** — des salariés, en situation de
   dépendance économique vis-à-vis du responsable de traitement.
3. **Évaluation ou notation** — le score sur 100 attribué aux candidats au
   remplacement, calculé en partie sur la distance à leur domicile.

Deux critères suffisent à la présomption. L'AIPD est un document de l'exploitant, pas
du logiciel ; ce fichier lui fournit la matière des sections « description du
traitement », « nécessité et proportionnalité » et « mesures de sécurité ».

---

## 10. Mesures de sécurité, pour l'article 32

Résumé de ce qui existe, à reporter dans le registre et l'AIPD. Le détail est dans
`CLAUDE.md` §6, §6 bis et §7.

- **Cloisonnement** : douze filtres de requête globaux sur `EntrepriseId`, forçage
  inconditionnel à l'écriture, et trois axes d'isolation — entreprise, client, agent.
  `EntrepriseId` n'apparaît dans aucun DTO de création : sa seule source est le jeton.
- **Authentification** : mots de passe de dix caractères minimum avec empreinte
  Identity, verrouillage après cinq échecs, limitation de débit par adresse **et** par
  compte, jeton d'accès de quinze minutes en cookie `HttpOnly Secure SameSite=Strict`,
  jeton de rafraîchissement opaque de 256 bits stocké par empreinte, rotation avec
  détection de rejeu, session absolue de quatorze jours.
- **Révocation immédiate** : `Actif` et `SecurityStamp` vérifiés à chaque requête ;
  une désactivation ou un changement de mot de passe périme les jetons en circulation
  sans attendre leur échéance.
- **Transport et navigateur** : HTTPS, HSTS hors développement, protection CSRF par
  double soumission, politique de sécurité du contenu en `default-src 'none'` sur
  l'API, limite de taille de corps appliquée avant toute lecture.
- **Journalisation des accès** : neuf actions, dont les trois que personne n'aurait
  cherchées — les deux routes de candidats et le planning.
- **Traçabilité du code** : 329 tests côté serveur, 499 côté interface, et la
  discipline de validation par sabotage décrite dans `CLAUDE.md`.
- ⚠️ **Les valeurs de paramètres SQL ne sont pas journalisées** :
  `EnableSensitiveDataLogging` n'est activé nulle part, et ne doit jamais l'être en
  production — les requêtes d'EF Core sont tracées au niveau `Information`, et leurs
  paramètres y apparaîtraient en clair.

---

## 11. Ce qui reste à faire, et par qui

**Par l'exploitant**, hors logiciel : valider les durées du §5, faire relire ce
fichier, remettre la note du §7, conclure les contrats de l'article 28 avec l'hébergeur
et le fournisseur SMTP, tenir l'AIPD du §9.6, désigner un point de contact.

**Par le logiciel**, dans l'ordre de gravité : §9.1 les polices, §9.2 la purge des
jetons, le script de conservation du §5, §8.2 le basculement des absences `Maladie`,
§9.5 l'aide du champ consignes, §9.3 l'effacement d'un compte sans fiche agent, et
§9.4 l'export.

Aucun de ces points n'est engagé par ce lot.
