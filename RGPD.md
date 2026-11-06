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
| **Sous-traitants** | son hébergeur, son fournisseur SMTP s'il en configure un, et le stockage de ses sauvegardes s'il appartient à un tiers (§4). Google l'a été jusqu'au 3 octobre 2026, par les polices (§9.1) |
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
   adresse, **coordonnées GPS du domicile**, absences, et depuis l'étape 25 la **distance
   au site** relevée au début et à la fin de chaque passage (T7).
2. **Les utilisateurs** — titulaires d'un compte de connexion. Souvent les mêmes
   personnes, dans un autre rôle : un agent peut avoir un compte, un responsable n'a
   pas de fiche agent.
3. **Les contacts clients** — nom, courriel et téléphone d'un interlocuteur chez le
   donneur d'ordre. Données **professionnelles**, et pas toujours celles d'une
   personne morale : voir §8.3.

---

## 2. Registre des traitements

Sept traitements. Chacun existe réellement dans le code, et l'endpoint ou le service
qui le porte est nommé.

### T1 — Planification des interventions

| | |
|---|---|
| **Finalité** | affecter des agents à des vacations datées sur les sites sous contrat |
| **Base légale proposée** | art. 6(1)(b), exécution du contrat de travail et du contrat de prestation |
| **Personnes** | agents |
| **Données** | identité, affectation, dates et heures de passage, statut du passage |
| **Tables** | `Interventions`, `Prestations`, `Contrats`, `Sites` |
| **Accès** | `Administrateur` et `Responsable` sur toute l'entreprise ; `ChefEquipe` sur **son équipe** seulement — les agents rattachés à son compte, et les vacations sans agent dont ils sont titulaires (étape 19) ; l'agent voit ses propres interventions, cadré par l'axe `AgentId` |
| **Destinataires** | aucun hors de l'entreprise |
| **Durée** | voir §5 |
| **Journalisé** | `GET /api/planning` — oui, parce qu'il nomme **tous** les agents de l'entreprise. Lu par un chef d'équipe, il ne nomme que son équipe, et le journal consigne d'autant moins de lignes : la frontière réduit ce qu'il lit, pas ce qui est tracé |

### T2 — Recherche d'un remplaçant

| | |
|---|---|
| **Finalité** | proposer des candidats classés quand une vacation n'est pas pourvue |
| **Base légale proposée** | art. 6(1)(f), intérêt légitime — assurer la continuité du service |
| **Personnes** | agents |
| **Données** | identité, **coordonnées GPS du domicile** (via la distance au site), nombre de passages antérieurs sur le site, heures de la semaine |
| **Services** | `AffectationService`, `RemplacementService` |
| **Accès** | rôles internes ; un chef d'équipe ne voit et ne propose que les agents de son équipe, sur les vacations de son équipe — ouvrir, proposer, accepter et refuser lui sont ouverts depuis l'étape 19 |
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
  ⚠️ Depuis l'étape 25, `Pointages` garde **à quelle distance d'un site** il était au
  début et à la fin d'un passage — une donnée dérivée, sans coordonnées, d'un autre
  traitement et d'une autre durée : voir T7.
- la distance n'est calculée **qu'entre le domicile et un site**, jamais entre deux
  positions dans le temps.

⚠️ **Aucune décision individuelle automatisée au sens de l'article 22.** Le score
sur 100 est un **classement**, pas une décision : `ProposerAsync` et `AccepterAsync`
sont deux gestes humains distincts, et il n'existe **aucun mode de remplacement
automatique** (`CLAUDE.md` §9 — « sans notifications, il n'y a personne à
contacter » ; l'écran de l'agent de l'étape 25 ne lui propose rien). Si un tel mode était ajouté un jour, cette ligne du registre
devient fausse et l'article 22 s'applique.

### T3 — Gestion des absences

| | |
|---|---|
| **Finalité** | savoir qu'un agent est indisponible, pour déclencher la recherche de remplaçant |
| **Base légale proposée** | art. 6(1)(b) et, pour le type `Maladie`, **art. 9(2)(b)** — obligations en matière de droit du travail |
| **Personnes** | agents |
| **Données** | type, dates, et un motif en texte libre **sauf sur un arrêt maladie** |
| **Tables** | `Absences` |
| **Accès** | lecture par les rôles internes — un chef d'équipe, celles de son équipe seulement —, écriture par `Exploitation` |
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
| **Données** | adresse de courriel, nom, prénom, téléphone, **empreinte du mot de passe**, rôle, compteur d'échecs et fin de verrouillage, **empreinte** du jeton de rafraîchissement, **adresse IP** de l'ouverture de session ; depuis l'étape 23, la **clé de double authentification chiffrée**, les **empreintes** des codes de récupération et le dernier pas de code accepté |
| **Tables** | `AspNetUsers`, `AspNetUserRoles`, `AspNetUserTokens`, `JetonsRafraichissement` |
| **Accès** | `GET /api/auth/moi` rend le sien. Depuis l'étape 24, `GET /api/auth/comptes` **liste** ceux de l'entreprise, administrateur seul ; `POST /api/auth/comptes` en crée un, et `/desactiver` et `/reactiver` le ferment et le rouvrent, administrateur seul aussi |
| **Durée** | voir §5 — et §9.2 pour l'adresse IP |
| **Journalisé** | pas son propre profil, et délibérément : `ProfilDto` est l'identité de l'appelant lui-même, un journal de « qui a lu son propre nom » est du bruit. **Oui** pour la liste des comptes de l'entreprise, qui nomme des collègues (`Compte` / `Liste`), et pour trois gestes d'administrateur sur le compte d'autrui : la réinitialisation du second facteur, la fermeture et la réouverture (T5) |

⚠️ **Aucun mot de passe n'est choisi par un autre que son titulaire.** Depuis l'étape 24,
la création ne reçoit plus de mot de passe : le serveur en tire un, l'affiche **une seule
fois** à l'administrateur, et ne le garde que par empreinte, comme tout autre. La colonne
`MotDePasseProvisoire` marque le compte jusqu'à la première connexion, qui impose de le
remplacer. L'administrateur ne connaît donc le mot de passe d'un collègue que jusqu'à ce que
celui-ci se connecte. Le premier administrateur, dont le mot de passe a été écrit dans
`.env`, suit la même règle.

⚠️ **Ni le mot de passe ni le jeton de rafraîchissement ne sont stockés** : seules
leurs empreintes le sont, SHA-256 pour le second. Une fuite de `JetonsRafraichissement`
ne permet pas de rejouer les sessions.

⚠️ **Il en va de même du second facteur, et la clé est le cas délicat.** Un code de
récupération n'est gardé que par empreinte SHA-256. La clé de l'application, elle, doit
pouvoir être relue pour vérifier un code : elle est donc **chiffrée**, par la Data
Protection d'ASP.NET, dont le trousseau vit dans le volume `cles-api` et non dans la
base. Une copie de la base seule ne livre aucune clé ; une copie de la base **et** du
trousseau les livre toutes, d'où la consigne de `DEPLOIEMENT.md` §4 de ne jamais les
sauvegarder ensemble. Aucun code de six chiffres n'est conservé, seulement le numéro du
dernier pas de trente secondes accepté, qui empêche de rejouer un code.

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

**Quatre natures n'y consignent pas une lecture**, mais un geste d'administrateur sur le
compte d'autrui : `Anonymisation`, irréversible ; depuis l'étape 23
`ReinitialisationSecondFacteur`, qui abaisse la protection d'un collègue jusqu'à son
prochain équipement ; et depuis l'étape 24 `Desactivation` et `Reactivation`, qui lui
retirent puis lui rendent l'accès. Rien n'y est lu ; c'est l'exception écrite à la règle
« seules les divulgations sont consignées » de `CLAUDE.md` §7 bis. La ligne ne dit que
l'auteur, le compte visé et l'instant — jamais le nom de la personne. ⚠️ Le même geste joué par
l'exploitant, en base, par `backend/Scripts/reinitialiser-second-facteur.sql`, **ne laisse
aucune ligne** : le journal attribue chaque ligne à un compte, et l'exploitant n'en est
pas un. `DEPLOIEMENT.md` §10 lui demande de consigner le geste de son côté.

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

### T7 — Certification des passages

Depuis l'étape 25 de `SPEC.md`.

| | |
|---|---|
| **Finalité** | prouver au client qu'un passage a eu lieu sur son site, et rendre compte de ce qui y a été fait et constaté |
| **Base légale proposée** | art. 6(1)(f), intérêt légitime — justifier la prestation auprès du donneur d'ordre, finalité que la CNIL admet pour la localisation d'un salarié ; art. 6(1)(b) pour la checklist et les anomalies, qui sont le compte rendu du travail |
| **Personnes** | agents ; et, par accident, toute personne qu'une photo d'anomalie montrerait |
| **Données** | pour chaque vacation, deux **pointages** au plus — début et fin : l'heure du serveur, la **distance au site** et la **précision annoncée** par le téléphone, en mètres, la preuve qui en découle (`SurSite`, `HorsRayon`, `Imprecise`, `NonLocalisee`), l'auteur. La **checklist** : chaque point, coché ou non, avec son libellé du moment. Les **anomalies** : une description libre, l'état, les dates, l'auteur et qui l'a traitée, et jusqu'à trois **photos** JPEG |
| **Tables** | `Pointages`, `ControlesEffectues`, `Anomalies`, `PhotosAnomalie` ; `PointsControle` n'est qu'un catalogue |
| **Services** | `ServicePassage`, `MetadonneesJpeg` |
| **Accès** | **Pointages** : l'agent seul, ceux de sa journée, par `GET /api/interventions/ma-journee`. **Aucune route ne les montre à un rôle interne** : direction et chefs ne lisent que le statut de la vacation, `Terminee` ou `NonCertifiee`. **Anomalies et photos** : les trois rôles internes, le chef d'équipe sur son équipe seulement ; l'agent, les siennes. Le syndic : rien |
| **Destinataires** | aucun aujourd'hui. Le client concerné est la raison d'être du traitement, mais aucun portail ne lui montre encore ses passages : la preuve, si elle est demandée, se lit en base par l'exploitant. ⚠️ Une donnée gardée pour une finalité qu'aucun écran ne sert encore est un point que l'AIPD doit regarder en face |
| **Durée** | un an pour les pointages ; voir §5 pour le reste |
| **Journalisé** | **non** — `VacationAgentDto` ne nomme que l'appelant, comme `ProfilDto` ; les anomalies et leurs photos, voir §8.4 |

⚠️ **La position n'est pas conservée, et il faut dire exactement ce qui l'est.** Le
téléphone envoie latitude, longitude et précision au moment du geste. Le serveur calcule
la distance au site, tranche la preuve, puis **jette les coordonnées** : aucune colonne de
`Pointages` ne peut porter une position, et un test fige la liste de ses colonnes. Elles
n'atteignent pas davantage le journal d'accès ni le journal technique, qui ne consigne
aucun corps de requête (§3). Ce qui reste est pourtant une donnée **dérivée** d'une
localisation : « à 140 m du site à 8 h 02 » situe une personne à un instant. D'où une
durée d'un an, et non les cinq ans de la vacation, dont seul le statut subsiste.

⚠️ **Ponctuelle, jamais continue.** La position n'est demandée qu'à *Démarrer* et à
*Terminer*, au geste de l'agent : jamais à l'ouverture de l'écran, jamais en arrière-plan
— il n'y a ni service worker ni file d'attente. Le téléphone ne doit pas servir de
position gardée en cache (`maximumAge: 0`). L'écran dit avant chaque geste ce qui est
relevé et ce qui ne l'est pas. Un refus, un téléphone sans capteur ou un délai dépassé
donnent un passage **sans position**, qui compte quand même : l'agent termine toujours,
la vacation est seulement `NonCertifiee`, et la cause est dite.

⚠️ **Seul l'agent affecté pointe, et seulement une vacation du jour.** Un chef qui
pointerait pour lui viderait la preuve de son sens. Le recours d'une équipe sans
téléphone est la saisie manuelle de `NonCertifiee` — « fait, sans preuve » — ; aucune
saisie ne pose `Terminee`.

⚠️ **Une photo de téléphone porte souvent ses coordonnées GPS**, dans ses métadonnées
EXIF. Elles sont retirées deux fois : par l'écran, qui réencode la photo sans en recopier
aucune, et par le serveur, `MetadonneesJpeg`, qui ne se fie pas à l'écran. Le second est la
garde ; un test part d'un JPEG qui porte une position et vérifie **en base** qu'elle n'y
est plus.

⚠️ **Un statut de vacation n'est pas une note de l'agent, et il ne doit pas le devenir.**
`NonCertifiee` dit qu'une preuve manque, pas qu'un salarié a failli : un téléphone sans
réseau dans un sous-sol en produit autant qu'une absence. Aucun écran ne calcule de taux
de certification par agent. En ajouter un ferait de T7 une **évaluation** au sens de l'AIPD
(§9.6), et un outil de contrôle de l'activité qu'il faudrait de nouveau présenter au CSE
(`DEPLOIEMENT.md` §11) : cette ligne du registre deviendrait fausse.

⚠️ **C'est un dispositif de contrôle de l'activité des salariés**, au sens du Code du
travail. Il doit être porté à leur connaissance **avant** sa mise en œuvre (L1222-4), et
soumis au CSE quand il en existe un. Ce n'est pas un réglage du logiciel :
`DEPLOIEMENT.md` §11 le met avant l'ouverture de *Ma journée* aux agents.

---

## 3. Cartographie des données

Les tables du modèle, plus celles d'Identity. « DP » = donnée personnelle.

| Table | Colonnes porteuses de DP | Catégorie | Sort par |
|---|---|---|---|
| `Entreprises` | `RaisonSociale`, `Siret` | personne morale — pas une DP | `GET /api/entreprises` |
| `Agents` | `Nom`, `Prenom`, `Email`, `Telephone`, `Adresse`, **`Latitude`**, **`Longitude`**, `DateEmbauche`, `ChefEquipeId` | identité, **localisation du domicile**, lien hiérarchique | `/api/agents`, et **`/api/planning`** et les deux routes de candidats ; nom et prénom seuls par `/api/agents/choix`, pour les listes déroulantes (journalisée en `ChoixAgents`) |
| `Absences` | `AgentId`, `Type`, dates, `Motif` | **art. 9 quand `Type = Maladie`** | `/api/absences`, et la cause « Agent absent » du planning |
| `Interventions` | `AgentId`, `DatePrevue`, `HeurePrevue`, `Statut` | activité professionnelle | `/api/interventions`, `/api/planning` |
| `Remplacements` | `AgentRemplacantId`, `Score` | **évaluation** d'une personne | `/api/remplacements` |
| `Clients` | `Nom`, `Email`, `Telephone`, `Adresse` | contact professionnel — voir §8.3 | `/api/clients` |
| `Sites` | `Adresse`, coordonnées, **`Consignes`** | lieu, et DP par accident dans les consignes | `/api/sites` |
| `Contrats` | aucune | — | `/api/contrats` |
| `Prestations` | `AgentTitulaireId` — `Libelle` est un intitulé de prestation | affectation habituelle d'un salarié à un site, à une heure | `/api/prestations`, et le dossier de l'agent |
| `Produits`, `Stocks`, `Emplacements` | aucune — un véhicule est nommé par sa plaque ou son modèle, jamais par son conducteur | — | — |
| `MouvementsStock` | **`AuteurId`**, `DateMouvement` | trace d'activité d'un utilisateur : qui a sorti, transféré, compté quoi, et quand | `/api/mouvements-stock` — l'identifiant seulement, jamais le nom, donc hors du journal d'accès (§7 bis de `CLAUDE.md`) |
| `TypesPrestation` | aucune — un libellé de catalogue et une durée | — | `/api/types-prestation` |
| `ConsommationsType` | aucune — une quantité de produit par passage d'un type | — | `/api/consommations-type` ; la prévision `/api/stocks/prevision` en dérive des besoins **par site**, sans agent ni vacation nommés |
| `Pointages` | `InterventionId`, `Horodatage`, **`DistanceMetres`**, **`PrecisionMetres`**, `Preuve`, `AuteurId` — **aucune coordonnée** | **donnée dérivée d'une localisation** : à quelle distance d'un site un agent était, à quel instant (T7) | `/api/interventions/ma-journee`, à l'agent lui-même et pour sa journée seulement ; aux autres rôles, rien que le statut de la vacation qui en découle |
| `PointsControle` | aucune — un libellé de checklist et son ordre, par type de prestation | — | `/api/points-controle` |
| `ControlesEffectues` | `InterventionId`, `Libelle`, `Fait` | compte rendu du travail d'un agent, point par point | `/api/interventions/ma-journee`, à l'agent seul, qui relit ce qu'il a déclaré une fois la vacation terminée ; **aucune route ne le montre à un rôle interne** |
| `Anomalies` | `InterventionId`, **`Description`**, `AuteurId`, `TraiteeParId`, dates | constat sur un site, et **DP par accident dans la description**, texte libre comme les consignes | `/api/anomalies`, rôles internes ; l'agent relit les siennes dans sa journée |
| `PhotosAnomalie` | **`Contenu`**, une image JPEG en `bytea`, `DateAjout` | une image d'un lieu, qui peut **montrer une personne** ; ses métadonnées, position GPS comprise, sont retirées par le serveur | `/api/anomalies/{id}/photos/{photoId}`, rôles internes et l'agent auteur, en `Cache-Control: private, no-store` |
| `AccesJournalises` | `UtilisateurId`, `CibleId`, `Horodatage` | trace d'activité d'un utilisateur | `/api/journal-acces` |
| `AspNetUsers` | `Email`, `UserName`, `PhoneNumber`, `Nom`, `Prenom`, `PasswordHash`, verrouillage, `MotDePasseProvisoire`, `DateAnonymisation` | identité et **secret d'authentification** ; `MotDePasseProvisoire` dit seulement si la personne s'est déjà connectée | `/api/auth/moi` ; adresse, nom, prénom, rôle et état de chaque compte par `/api/auth/comptes` (administrateur, journalisée) ; nom et prénom des chefs d'équipe par `/api/agents/chefs-equipe` (administrateur, journalisée) |
| `AspNetUserTokens` | sous le fournisseur `[KleanSecondFacteur]` : `Cle` (la clé de double authentification, **chiffrée**), `CodesRecuperation` (des **empreintes**), `DernierPas` (un numéro de pas de trente secondes) | **secret d'authentification** | **aucune route** — la clé ne sort qu'une fois, au QR code de l'équipement, et les codes qu'une fois, à leur création |
| `JetonsRafraichissement` | `UtilisateurId`, **`AdresseIp`**, dates | trace de connexion | **aucune route** |

⚠️ **Quatre colonnes sont des données personnelles que personne ne cherche là.**

- **`Sites.Consignes`** est un texte libre lu par l'agent avant son passage. Rien
  n'empêche d'y écrire « sonner chez Mme Durand, deuxième étage ». La table est
  exemptée de journalisation parce qu'un site est un lieu ; le champ peut faire
  mentir cette exemption. Parade proposée au §9.5.
- **`JetonsRafraichissement.AdresseIp`** est la seule adresse IP du produit. Elle
  n'était jamais purgée ; elle l'est à trente jours depuis §9.2.
- **`Anomalies.Description`** est un texte libre, comme les consignes, et l'écran demande
  de l'écrire « sans nommer de personne ».
- **`PhotosAnomalie.Contenu`** est une image, et une image de couloir peut montrer
  l'occupant qui y passe. Aucune consigne ne le dit encore à l'agent : §9.9.

**Ce qui n'existe pas, et qu'il est utile de pouvoir affirmer :** aucune position
historisée — `Pointages` garde une distance, jamais des coordonnées (T7) —, aucun suivi
en arrière-plan, aucun accusé de lecture de consigne, aucun cookie de mesure d'audience,
aucune télémétrie, aucun appel sortant hors SMTP depuis la correction du §9.1.

**Le journal technique de l'API**, depuis l'étape 18 : une ligne par requête, sur la
sortie standard. Elle porte la méthode, le chemin, le statut, la durée et un
identifiant de corrélation — **ni adresse IP, ni identité de l'appelant, ni corps de
requête**. Le chemin contient parfois l'identifiant technique d'un agent
(`/api/agents/{id}`) : une donnée pseudonyme, donc personnelle au sens strict, mais
qui ne dit pas qui a lu — c'est le rôle du journal d'accès de T5. Sa conservation est
celle que l'exploitant donne à ses journaux de conteneur ; voir `docker-compose.yml`.

---

## 4. Où les données sortent de l'instance

Trois sorties depuis la correction du §9.1, et toutes les trois sont voulues.

1. **Vers le navigateur de l'utilisateur** — c'est l'usage. Protégé par HTTPS, par
   les trois cookies `HttpOnly Secure SameSite=Strict`, et par la politique de
   sécurité du contenu.
2. **Vers le serveur SMTP**, si l'exploitant en configure un : l'adresse du
   destinataire et un jeton de réinitialisation. **Sous-traitant à déclarer.** Sans
   SMTP, KLEAN démarre et seul ce parcours est désactivé — il répond 503 en disant
   quoi configurer.
3. **Vers l'hébergeur** — la base entière. Sous-traitant à déclarer.

   **Et vers le stockage des sauvegardes**, qui est une copie de la base entière, arrêts
   maladie compris. Si ce stockage appartient à un tiers, c'est un sous-traitant de plus.
   Une sauvegarde a sa propre durée de conservation : gardée cinq ans, elle conserve
   cinq ans ce que les scripts du §5 ont supprimé de la base. Une restauration ressuscite
   donc des données purgées, et les scripts sont à rejouer après (`DEPLOIEMENT.md` §4).
4. ~~**Vers Google**, à chaque chargement de page.~~ **Supprimée** le 3 octobre 2026 :
   les polices sont servies par KLEAN, et les deux politiques de sécurité du contenu ne
   déclarent plus aucune origine externe. Voir §9.1 — c'était l'écart le plus sérieux de
   cette revue, et il n'en reste **trois** sorties.

---

## 5. Durées de conservation

⚠️ **Propositions motivées, à valider juridiquement.** La colonne la plus importante
est la dernière : une durée sans mécanisme est une promesse que personne ne tient.

| Nature de la donnée | Durée proposée | Raison | Mécanisme |
|---|---|---|---|
| Fiche agent (`Agents`) | 5 ans après la fin du contrat de travail | aligné sur la conservation des pièces du registre du personnel | 📋 `conservation-donnees-metier.sql` §6 — **rapport seulement**, voir ci-dessous |
| Coordonnées GPS du domicile | tant que le contrat de travail court, puis effacement immédiat | elles ne servent qu'à l'affectation : un agent parti n'est plus candidat | 📋 rapport ; l'effacement passe par `/anonymiser`, qui les met à `null` |
| Absences, hors `Maladie` | 5 ans, avec la fiche agent | pièce justificative d'une indisponibilité | 📋 `conservation-donnees-metier.sql` §2 |
| Absences de type `Maladie` | **3 mois après la date de fin** | voir §8.2 : KLEAN n'est pas un outil de paie, il n'a aucune raison de garder une donnée de santé au-delà du remplacement | 📋 `degradation-absences-maladie.sql` |
| Interventions et leur statut | 5 ans | preuve des passages facturés, prescription de l'action contractuelle | 📋 `conservation-donnees-metier.sql` §1 |
| Remplacements et scores | 1 an | état dérivé, sans valeur probante au-delà de l'exercice | 📋 `conservation-donnees-metier.sql` §3 |
| Clients et sites | durée du contrat, puis 5 ans ; sans contrat, 5 ans après la création | relation commerciale ; pour un prospect jamais signé, la CNIL retient plutôt 3 ans (§9.7) | 📋 `conservation-donnees-metier.sql` §4 et §5 |
| Comptes (`AspNetUsers`) | désactivation à la sortie, effacement à 5 ans | un compte désactivé reste nécessaire pour attribuer les lignes du journal | 📋 deux gestes manuels, à l'écran *Comptes* : **Fermer** à la sortie (`/desactiver`, depuis l'étape 24), puis **Pseudonymiser** (`/anonymiser`, §9.3) |
| Second facteur (`AspNetUserTokens`) | tant qu'il est actif ; effacé au retrait, à la réinitialisation et à la pseudonymisation du compte | un secret qui n'authentifie plus rien n'a aucun usage | ✅ par construction : chaque retrait efface les trois jetons, et `ServiceAnonymisation` les efface sur ses deux chemins |
| Jetons de rafraîchissement | **30 jours après révocation ou expiration** | la fenêtre de diagnostic d'un rejeu ; au-delà, seule l'adresse IP survit, sans usage | ✅ `purge-jetons-rafraichissement.sql` |
| Journal d'accès | **12 mois** | un journal RGPD est lui-même une donnée personnelle | ✅ `purge-journal-acces.sql` |
| Mouvements de stock et leur auteur | 5 ans, comme les interventions | l'historique qui explique une quantité ; l'auteur suit son compte, pseudonymisé à la sortie | 📋 `conservation-donnees-metier.sql` §8 — purger un mouvement ne change aucune quantité, et son `DELETE` passe **avant** celui des sites qu'il cite |
| Pointages des passages (`Pointages`) | **1 an après le pointage** | la CNIL retient un an quand une localisation sert à prouver une prestation au client, ce qui est la finalité de T7 — par analogie avec la norme NS-051, écrite pour les véhicules. Le statut de la vacation, lui, reste cinq ans avec elle | ✅ `purge-pointages.sql`, qui supprime avec `-v confirmer=1` |
| Checklist d'un passage (`ControlesEffectues`) | 1 an après la date de la vacation | le détail du compte rendu, comme les pointages ; au-delà, le statut suffit | 📋 `conservation-donnees-metier.sql` §9 |
| Anomalies | 1 an après le signalement, **une fois traitées** | le temps d'une contestation du client sur l'exercice. Une anomalie encore ouverte n'est pas purgée : elle décrit un travail à faire, pas une trace | 📋 `conservation-donnees-metier.sql` §9 |
| Photos d'anomalie | **3 mois après le traitement**, et au plus tard avec leur anomalie | elle sert à juger et à traiter ; elle peut montrer une personne, et c'est la donnée la plus lourde du passage. La description et les dates disent ensuite ce qui a été constaté | 📋 `conservation-donnees-metier.sql` §9 |

**Lire la dernière colonne avec attention : 📋 n'est pas ✅.** Un script marqué 📋
**rapporte** ce qui est hors délai et porte son `DELETE` **en commentaire**. Il ne
supprime rien tant qu'un humain ne décommente pas, et c'est délibéré pour deux raisons :
les durées de ce tableau sont des propositions non validées juridiquement, et une purge
de données métier n'est pas réversible. Deux scripts étaient ✅ — ceux dont la
durée est tranchée et dont les lignes n'ont aucune valeur probante. Un troisième l'est
depuis l'étape 25, `purge-pointages.sql`, pour une autre raison : sa durée d'un an est
retenue, et une donnée dérivée d'une localisation ne doit pas attendre qu'un humain
décommente une ligne. Il ne supprime qu'avec `-v confirmer=1`, ce qui permet de
l'ordonnancer sans le modifier.

⚠️ **Une purge de données métier n'est pas une purge de journal, et l'ordre est la
moitié du travail.** La chaîne d'exploitation est en `Restrict` depuis l'étape 16 bis ;
deux clés de plus le sont sur l'agent — `Intervention.AgentId` et
`Remplacement.AgentRemplacantId` —, et trois sur la vacation depuis l'étape 25 : ses
pointages, sa checklist et ses anomalies. Un `DELETE` dans le mauvais ordre échoue sur une
violation de clé **à mi-parcours**, après avoir déjà supprimé ce qui précédait.
`conservation-donnees-metier.sql` porte donc l'ordre en commentaire, et une colonne
`supprimable_maintenant` qui distingue une ligne hors délai d'une ligne hors délai
**mais bloquée par un enfant encore dans le sien**. Les lignes bloquées le restent
jusqu'à ce que leurs enfants tombent : le script est à **rejouer**, pas à forcer.

⚠️ **Aucun `DELETE` sur `Agents`, à aucun moment.** C'est le compromis de l'étape 17 :
`Restrict` sur `Intervention.AgentId` rend un agent insupprimable dès qu'il a travaillé,
et la réponse à une demande d'effacement est `POST /api/agents/{id}/anonymiser`, qui
conserve la ligne. Le script ne fait que lister les agents inactifs non pseudonymisés —
une liste à examiner, pas une échéance, le modèle ne portant pas de date de fin de
contrat de travail.

---

## 6. Droits des personnes, et comment ils sont servis

| Droit | Servi aujourd'hui par | État |
|---|---|---|
| Information (art. 13) | pour T7 seulement, la notice que *Ma journée* affiche avant chaque geste qui relève la position ; rien d'autre dans le produit | **à remettre par l'exploitant** — modèle de note au §7. La notice dit l'essentiel au moment où il compte ; elle ne remplace pas la note |
| Accès (art. 15) | **`GET /api/agents/{id}/dossier`**, administrateur seul | ✅ dossier consolidé en une réponse — ⚠️ **sans les passages** de l'étape 25, voir §9.8 |
| Rectification (art. 16) | `PUT /api/agents/{id}` | ✅ |
| Effacement (art. 17) | `POST /api/agents/{id}/anonymiser` et `POST /api/auth/comptes/{id}/anonymiser` | ✅ sur les deux chemins — mais c'est une **pseudonymisation**, voir §8.1 |
| Limitation (art. 18) | `Actif = false` sur l'agent et sur le compte | approximatif : c'est une désactivation, pas un gel |
| Portabilité (art. 20) | **`GET /api/agents/{id}/dossier`** | ✅ JSON structuré et lisible par machine |
| Opposition (art. 21) | sans objet sur T1, T3, T4 (exécution du contrat) ; recevable sur T2 et T7, fondés sur l'intérêt légitime | à traiter au cas par cas, hors code. Sur T7, l'agent peut déjà refuser la localisation à chaque geste : son passage compte, sans être certifié |

⚠️ **Le délai d'un mois de l'article 12 court même quand l'outil n'aide pas.** C'est ce
qui rendait les deux manques précédents — pas de dossier consolidé, pas d'export —
dangereux plutôt que seulement incommodes : un administrateur pouvait composer la
réponse à partir de cinq appels, et rien ne garantissait qu'il n'en oubliait pas un.

**Le dossier répond aux deux, et il est borné autant que complet.** Trois choses n'y
figurent pas, et les trois sont des choix, inscrits dans la réponse elle-même sous
`nonInclus` :

- **L'empreinte du mot de passe.** Elle n'est pas communicable au titre de l'article
  15 : elle n'apporte rien à la personne et sa divulgation affaiblirait le secret
  qu'elle protège. Un test l'éprouve sur le **corps brut** de la réponse, et non sur le
  DTO — une propriété ajoutée demain n'apparaîtrait dans aucune assertion typée.
- **L'identité de ceux qui ont consulté la fiche.** Le dossier donne leur **nombre** et
  la nature de leurs accès, jamais leurs noms : les lignes du journal nomment des
  **collègues**, et l'article 15(4) écarte l'atteinte aux droits d'autrui. Le nombre dit
  que la traçabilité existe ; les identités appartiennent à la direction, par
  `GET /api/journal-acces`.
- **L'historique des connexions et les adresses IP.** Leur finalité est la sécurité des
  sessions, pas la description de la personne, et leur conservation est de trente jours.

⚠️ Et trois choses y figurent **exprès**, parce que les retenir serait le vrai
manquement : le **motif** d'une absence quand il existe, les **prestations dont l'agent
est titulaire** — jours, heure, site et ville : ce qu'on prévoit de lui confier, et que
la génération des vacations lui affecte (étape 20 de `SPEC.md`) —, le **nom de son
chef d'équipe**, en clair et non en identifiant (étape 19), et le **score**
attribué à l'agent comme candidat au remplacement. Ce score est une évaluation de la personne — un
des trois critères qui rendent l'AIPD probable — et la lui cacher dans sa propre réponse
d'accès serait précisément ce que l'article 15 interdit.

⚠️ **Le dossier est réservé à l'administrateur, pas ouvert aux rôles internes.** Un
responsable lit la fiche d'un agent, c'est son métier ; sortir le dossier complet d'un
salarié est une réponse à une demande légale, et c'est un autre geste. Il est journalisé
en nature **`Export`**, distincte de `Lecture` pour la même raison : les confondre
ferait disparaître l'export dans le bruit des consultations.

---

## 7. Modèle de note d'information aux agents

À adapter, à remettre à chaque agent et à afficher. C'est la contrepartie de T2 et de T7 :
conserver les coordonnées du domicile d'un salarié, et relever sa position à chaque
passage, est défendable **à condition de le dire**, et un outil silencieux sur ce point
est indéfendable quelle que soit la qualité de son code.

⚠️ **Pour T7, la remettre ne suffit pas : elle doit l'être avant.** Un dispositif qui
collecte une information sur un salarié doit avoir été porté à sa connaissance **avant**
(Code du travail, L1222-4) ; et le CSE consulté s'il existe. Voir `DEPLOIEMENT.md` §11.

⚠️ **Le paragraphe sur les photos promet une consigne que l'écran ne donne pas encore** :
§9.9. Le garder, c'est s'engager à la corriger.

> **Traitement de vos données dans l'outil de planification**
>
> *[Raison sociale]* utilise le logiciel KLEAN pour planifier les interventions et
> organiser les remplacements.
>
> **Ce qui est enregistré sur vous** : vos nom et prénom, vos coordonnées de contact,
> votre adresse et **les coordonnées géographiques de votre domicile**, votre date
> d'embauche, vos interventions planifiées et réalisées, et vos absences — type et
> dates. **Un arrêt maladie ne porte jamais de motif** : l'outil le refuse. Pour chaque
> passage, l'heure et la distance au site au début et à la fin, ce que vous avez coché
> dans la liste des contrôles, et les anomalies que vous signalez, avec leurs photos.
>
> **À quoi servent les coordonnées de votre domicile** : à calculer la distance entre
> chez vous et un site, lorsqu'il faut trouver un remplaçant. C'est un des trois
> critères de classement des candidats, avec votre connaissance du site et vos heures
> déjà planifiées dans la semaine.
>
> **Ce que l'écran « Ma journée » relève de votre position** : quand vous appuyez sur
> *Démarrer* et sur *Terminer*, et seulement à ce moment-là, votre téléphone donne votre
> position une fois. Elle sert à calculer **à quelle distance du site** vous étiez, pour
> prouver au client que le passage a eu lieu. **La position elle-même n'est jamais
> enregistrée** : seules la distance, sa précision et l'heure le sont. Rien n'est relevé
> en dehors de ces deux gestes, ni pendant le passage, ni en dehors du travail.
>
> **Si vous refusez la localisation**, ou si votre téléphone ne la trouve pas, le passage
> compte quand même : il est seulement marqué « non certifié ». Ce n'est pas une faute,
> et cela ne vous est pas reproché.
>
> **Les photos d'anomalie** montrent ce qui a été constaté, pas les personnes : ne
> photographiez personne. Leur position GPS est effacée avant l'enregistrement.
>
> **Qui décide** : une personne, toujours. Le classement est une aide ; la proposition
> et l'affectation sont des décisions humaines.
>
> **Qui y a accès** : la direction et les responsables de planning. Les accès à vos
> données sont enregistrés — qui a consulté, quand — et cet enregistrement n'est
> consultable que par la direction. **Les distances relevées à vos passages ne sont
> visibles que de vous** : la direction voit seulement si un passage est certifié ou non.
>
> **Combien de temps** : *[reporter les durées du §5 validées]*.
>
> **Vos droits** : accès, rectification, effacement, limitation, opposition et
> réclamation auprès de la CNIL. Contact : *[adresse]*.

---

## 8. Les questions que les lots précédents avaient laissées ouvertes

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

### 8.4 — Les anomalies et leurs photos sont-elles à journaliser ?

Posée au lot A de l'étape 25 et laissée au lot D : une photo d'anomalie peut montrer une
personne.

**Tranchée : pas de journal, et la raison est écrite pour pouvoir tomber.**

- **Le journal de T5 existe pour un risque nommé** : la surveillance des salariés par
  leur employeur, à travers leurs données. Une anomalie ne dit rien de l'agent qui la
  signale au-delà d'un identifiant d'auteur — le raisonnement de `MouvementsStock.AuteurId`
  — et ce qu'elle décrit est un lieu.
- **Une personne photographiée par accident n'est pas un salarié dont on lit le
  dossier.** La journaliser à chaque vignette affichée — trois photos par anomalie, à
  chaque ouverture de l'écran *Anomalies* — ferait du journal un relevé de l'écran, et un
  journal bruyant ne se lit plus.
- **La parade proportionnée est à la source et dans le temps** : que personne n'entre
  dans la photo (§9.9), et qu'elle parte trois mois après le traitement (§5).

⚠️ **Le filet par réflexion de `JournalAccesTests` ne voit pas ces routes, et c'est à
savoir.** `LirePhoto` rend des octets, pas un DTO : aucune propriété ne la signalerait.
`AnomalieDto` porte une `Description` en texte libre, de même nature que le `Motif` d'une
absence, mais le filet ne connaît que `Motif`. L'absence de journal est donc ici une
**décision écrite**, et non un filet qui se tait — personne ne serait prévenu si elle
cessait d'être juste.

**Ce qui ferait tomber la décision** : une anomalie dont l'objet devient une personne —
un champ pour nommer un occupant ou un collègue, une catégorie « comportement » —, ou une
sortie des photos hors de l'entreprise, vers le syndic ou le client. Le premier cas
ferait des anomalies un canal de signalement sur autrui ; le second, un destinataire de
plus au registre.

---

## 9. Écarts constatés

Classés par gravité. Aucun n'est corrigé par ce lot, qui est de la documentation ;
chacun est écrit pour être pris ou refusé en connaissance de cause.

### 9.1 — ✅ CORRIGÉ — les polices étaient chargées chez Google, à chaque page

`frontend/index.html` chargeait Figtree et IBM Plex Mono depuis `fonts.googleapis.com`
et `fonts.gstatic.com`. Chaque chargement de page transmettait donc à Google LLC
l'**adresse IP** du navigateur, son agent et le référent — sans que l'exploitant l'ait
choisi, et pour toute instance de KLEAN.

⚠️ **Cela contredisait directement le §2 de `CLAUDE.md`** : « pas de dépendance
propriétaire, pas de service tiers obligatoire ». Google Fonts **était** un service
tiers obligatoire à l'exécution, et il avait échappé à cette règle parce qu'il
n'apparaît pas dans `package.json` — la règle avait été lue comme portant sur les
dépendances de construction.

C'était l'écart le plus sérieux de cette revue : le seul à concerner **tous les
utilisateurs de toutes les instances**, et le seul à être un transfert à un tiers.

**Corrigé.** Les quatre fichiers sont dans `frontend/public/polices/`, déclarés en
`@font-face` en tête de `tokens.css`, et les deux politiques de `frontend/csp.ts` sont
refermées sur `'self'` — elles ne déclarent plus **aucune** origine externe.

| Fichier | Taille | Provenance |
|---|---|---|
| `Figtree-variable.woff2` | 27 Ko | github.com/erikdkennedy/figtree, fonte variable 300–900 |
| `IBMPlexMono-Regular-Latin1.woff2` | 17 Ko | github.com/IBM/plex, sous-ensemble Latin1 |
| `IBMPlexMono-Medium-Latin1.woff2` | 17 Ko | idem |
| `IBMPlexMono-Bold-Latin1.woff2` | 17 Ko | idem |

Les deux licences **OFL** sont versées à côté des fichiers, ce qu'exige leur
redistribution dans un dépôt sous AGPL.

**79 Ko au total, et deux choix qui expliquent ce chiffre.** La fonte **variable** de
Figtree remplace à elle seule les quatre fichiers statiques qu'auraient demandés les
graisses 400 à 700. Et IBM Plex Mono vient en sous-ensemble **Latin1** — 17 Ko par
graisse contre 50 pour la fonte complète — dont la plage Unicode, vérifiée dans les
déclarations amont d'IBM, couvre tout ce qu'une interface française écrit : accents,
`Œ` et `œ`, guillemets, tiret cadratin, euro, points de suspension. La plage
`unicode-range` est recopiée telle quelle dans `tokens.css` et doit y rester : sans
elle, un caractère hors sous-ensemble s'afficherait en tofu au lieu de retomber
proprement sur la fonte à chasse fixe du système.

⚠️ **Vérifié de bout en bout, parce qu'une police absente ne casse rien de visible** —
la typographie serait seulement « un peu différente », et le cache du navigateur
masquerait même la requête manquante. `npm run preview` sert désormais
`default-src 'none'; script-src 'self'; style-src 'self'; font-src 'self'; …` sans une
seule origine externe, et les quatre `.woff2` y répondent 200 en `font/woff2`, aux
tailles exactes de leurs sources amont.

⚠️ **Et la garde est mécanique, pas verbale.** `npm run verifier` échoue si une `url()`
du CSS pointe sur un fichier absent, si un `.woff2` perd sa signature, si une licence
OFL disparaît, ou si une origine externe réapparaît dans `csp.ts` ou `index.html`.
Trois sabotages l'ont éprouvée : origines de Google réintroduites, fichier de police
retiré, signature de fichier cassée.

**Effet de bord utile pour le déploiement** : KLEAN n'a plus besoin d'aucun accès
sortant pour servir son interface. Une instance derrière un pare-feu fermé affiche la
même typographie qu'une autre.

### 9.2 — ✅ CORRIGÉ — l'adresse IP des sessions n'était jamais purgée

`JetonsRafraichissement.AdresseIp` est renseignée à chaque ouverture de session et à
chaque rotation — donc plusieurs fois par jour et par utilisateur. **Rien ne supprime
ces lignes**, ni l'expiration, ni la révocation, ni la déconnexion : elles s'accumulent
indéfiniment avec leur adresse.

L'ironie est à nommer : le journal d'accès du lot A refuse délibérément de stocker une
adresse IP (« une donnée personnelle de plus, dont la conservation demanderait sa propre
justification »), et la table de sessions en garde l'historique complet. La colonne a
son usage — diagnostiquer un rejeu — mais cet usage dure des jours, pas des années.

**Corrigé** par `backend/Scripts/purge-jetons-rafraichissement.sql`, sur le modèle de
celui du journal : rapport par état, `DELETE` décommentable.

⚠️ **La condition protège les sessions vivantes, et elle ne doit pas être « simplifiée ».**
Un jeton vivant se reconnaît à `DateRevocation IS NULL ET DateExpiration dans le futur`,
et la première clause du `WHERE` est là pour l'exclure. La retirer « parce que la seconde
suffit » détruirait les sessions en cours : un jeton vivant dont l'expiration est
lointaine passe la seconde condition dès que `DateRevocation` est nulle. Le prédicat a
été éprouvé sur sept cas limites — vivant, révoqué d'hier, révoqué à 29 et 31 jours,
expiré sans révocation à 10 et 40 jours — et la classification du rapport s'accorde
exactement avec ce que le `DELETE` emporterait.

### 9.3 — ✅ CORRIGÉ — il n'existait aucun chemin d'effacement pour un compte sans fiche agent

`ServiceAnonymisation.ExecuterAsync` part d'un `agentId` et ne touche `AspNetUsers` que
si un compte porte cet `AgentId`. Un compte de rôle `Responsable`, `Administrateur` ou
`ClientSyndic` a `AgentId = null` : **aucun endpoint ne permettait d'effacer ses
données.** Son nom, son prénom et son courriel restaient en base indéfiniment, et il
n'existait même pas de route pour le désactiver — la fixture de tests le fait directement
en base, faute d'endpoint. La fermeture d'un compte, `/desactiver`, n'existe que depuis
l'étape 24.

Ce n'était pas un oubli du lot B, qui traitait la demande d'effacement d'un **agent** ;
c'était un trou du modèle de droits, et il concernait les personnes les mieux placées
pour s'en apercevoir.

**Corrigé** par `POST /api/auth/comptes/{id}/anonymiser`, administrateur seul,
irréversible, journalisé en `Compte` / `Anonymisation`. Il remplace l'identité, substitue
l'adresse sur `anonymise.invalid`, désactive le compte, régénère l'empreinte de sécurité,
révoque les sessions et pose `DateAnonymisation` — **une date et non un drapeau**, pour
que le registre puisse répondre « quand cet effacement a-t-il été exécuté ». La colonne
est ajoutée par la migration `AnonymisationDesComptes`, un `AddColumn` nullable.

**Quatre refus en 409, et chacun nomme sa sortie** — déjà anonymisé, rattaché à un agent,
son propre compte, et le cas du dernier administrateur ci-dessous. Un compte rattaché à un
agent est renvoyé vers `POST /api/agents/{id}/anonymiser` avec l'identifiant exact :
cette route seule traite les trois endroits où la personne est nommée, et le traiter ici
laisserait la fiche agent intacte, c'est-à-dire donnerait un effacement qui n'efface pas.
Un test vérifie que la sortie nommée est **franchissable**.

⚠️ **Un sabotage a fait apparaître un trou que je n'avais pas vu, et déplacé une garde
que j'avais mal placée.** J'avais écrit un refus « jamais le dernier administrateur
actif » sur cette route. Le retirer n'a fait rougir aucun test — parce que la garde est
**inatteignable** : l'appelant est nécessairement un administrateur actif, donc si la
cible en est un autre, ils sont deux ; elle ne pourrait se déclencher que pour cible =
appelant, cas que le refus de s'effacer soi-même traite déjà. C'est donc **ce** refus qui
porte l'invariante « une entreprise conserve toujours au moins un administrateur actif ».

Le risque est réel, mais il est sur **l'autre route**. `POST /api/auth/comptes` exige un
`AgentId` pour le rôle `Agent` et ne l'interdit à aucun autre : un compte
`Administrateur` peut donc porter une fiche agent — le cas normal dans une entreprise de
cinq personnes où le responsable fait aussi le ménage. Anonymiser cette fiche neutralise
son compte **sans** passer par le refus de s'effacer soi-même, et l'entreprise se
retrouve sans aucun compte capable d'en créer un, `POST /api/auth/comptes` étant réservé
à l'administrateur — sans rattrapage par l'API. La garde est désormais là, et elle y est
éprouvée dans les deux sens : le refus, et la franchissabilité de la sortie qu'il nomme.

**Ce qui subsiste**, et c'est l'exact symétrique des interventions d'un agent :
l'identifiant technique du compte, parce que `AccesJournalise` attribue chaque lecture de
données personnelles à un `UtilisateurId`. Le supprimer rendrait illisible le journal qui
existe pour rendre des comptes — y compris les lignes documentant les accès d'autres
personnes. Le rôle subsiste aussi : il dit la fonction occupée, ne nomme personne, et
rend les lignes du journal interprétables.

⚠️ Et une troisième fois dans ce projet, `Actif = false` et le `SecurityStamp` se sont
couverts l'un l'autre : un sabotage retirant `UpdateSecurityStampAsync` est resté vert,
les deux assertions HTTP passant par le seul effet de `Actif`. Ce qui l'attrape est une
relecture de l'empreinte **en base**, avant et après.

### 9.4 — ✅ CORRIGÉ — il n'existait aucun export, donc aucune portabilité

Rien ne composait un dossier consolidé. Un administrateur devait reconstituer la réponse
à une demande d'accès à partir de cinq appels, sans garantie de n'en oublier aucun.

**Corrigé** par `GET /api/agents/{id}/dossier` — voir §6 pour ce qu'il contient, ce
qu'il exclut, et pourquoi. Deux remarques de conception qui n'appartiennent pas au
tableau des droits :

- **L'invariante par réflexion a imposé la journalisation avant que j'y pense.**
  `DossierAgentDto` porte `Nom`, `Prenom`, `Adresse`, `Latitude` : `JournalAccesTests` a
  rendu l'action rouge à la seconde où elle a existé, d'abord par le test d'excès — « les
  actions journalisées sont exactement celles attendues » — puis par celui d'omission dès
  que j'ai retiré l'attribut pour vérifier qu'il mordait. C'est le filet de §7 bis de
  `CLAUDE.md` faisant exactement ce pour quoi il a été écrit : un DTO créé plus tard y
  tombe sans que personne y pense.
- **Le cadrage du compte lié est écrit à la main, et c'est obligatoire.**
  `AspNetUsers` n'est pas couverte par les filtres globaux, donc le `EntrepriseId` figure
  explicitement dans la recherche du compte. Le test qui l'éprouve construit le compte
  intrus **directement en base**, sous les validations de `POST /api/auth/comptes` qui le
  rendraient incréable par l'API — une défense en profondeur qu'aucun test n'exerce est
  une ligne qu'on peut retirer sans rien casser.

### 9.5 — ✅ CORRIGÉ — `Sites.Consignes` ne dissuadait pas de nommer une personne

Le champ n'était décrit que par « Accès, codes, matériel sur place. Lues par l'agent
avant son passage. » Rien n'y dissuadait d'écrire le nom d'un gardien ou d'un occupant.

**Corrigé** dans `frontend/src/reference/descriptions.ts` : l'aide du champ demande
désormais de préférer une fonction — « le gardien », « l'accueil ». C'est une mesure de
minimisation **à la source**, infiniment moins coûteuse que de journaliser toutes les
lectures de sites, et la seule qui agisse sur la cause plutôt que sur la trace.

⚠️ Gardée par un test nommé, à côté de celui du motif d'arrêt maladie. Une phrase d'aide
supprimée ne casse rien de visible : c'est précisément pour cela qu'elle en a besoin.

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

**La certification des passages (T7) ajoute deux raisons, et l'AIPD doit la couvrir.**

4. **Des données de localisation**, que les lignes directrices européennes sur l'AIPD
   (WP248) rangent parmi les données à caractère hautement personnel. Ici une distance
   et non une position, mais dérivée d'une position.
5. **Une surveillance systématique, à apprécier.** La localisation est ponctuelle — deux
   points par passage —, mais elle porte sur **chaque** passage de **chaque** agent, et
   le salarié ne peut s'y soustraire qu'en renonçant à la certification.

Ce que l'AIPD peut opposer à ces deux critères, au titre de la proportionnalité, et qui
est vérifiable dans le code :

- **la minimisation** : les coordonnées sont jetées par le serveur, la photo perd sa
  position deux fois, la checklist garde le libellé et non une mesure ;
- **la limitation de l'accès** : aucune route ne montre une distance à la direction, qui
  ne lit que le statut ; l'agent lit la sienne ;
- **la durée** : un an pour les pointages, purgés par un script réel ; trois mois pour une
  photo traitée ;
- **l'absence de conséquence automatique** : un passage sans position compte, et aucun
  écran ne calcule de taux par agent ;
- **la transparence** : la notice affichée avant chaque geste, et la note du §7, remise
  avant la mise en service.

⚠️ **La doctrine invoquée est une analogie.** Les durées et la finalité viennent de la
norme NS-051 de la CNIL, écrite pour la géolocalisation des **véhicules** et sans valeur
juridique depuis le RGPD. Aucune page de la CNIL propre au téléphone d'un salarié n'a été
trouvée le 6 novembre 2026. C'est le premier point à soumettre à la relecture juridique.

### 9.7 — ✅ CORRIGÉ — un site ou un client sans contrat n'avait aucune date dont partir

Découvert en écrivant `conservation-donnees-metier.sql`, et c'est le genre de trou qu'on
ne voit qu'en essayant d'appliquer une politique de conservation.

`Sites` et `Clients` ne portaient **aucune date** : ni création, ni fin. Leur échéance se
déduisait de celle de leurs contrats — un site est hors délai quand tous ses contrats le
sont. Ce raisonnement ne fonctionne pas pour une ligne qui n'a **aucun** contrat : la
requête ne pouvait pas distinguer un site dont les contrats ont expiré puis été purgés
d'un site saisi hier et pas encore sous contrat. Les deux se présentaient identiquement.

Le trou était plus large qu'il n'y paraissait. La colonne `supprimable_maintenant` des
lignes `sites` et `clients` valait **toujours zéro** : elle exigeait qu'un contrat existe
pour dater la ligne, et qu'il n'en existe aucun pour la supprimer. Aucun site ni aucun
client n'était donc jamais purgeable, même après la purge de toute sa chaîne.

**Corrigé** le 28 octobre 2026 par la migration `DateCreationSurSitesEtClients` : une
colonne `DateCreation`, posée par le serveur, absente des deux `CreateDto` (donc ni un
`POST` ni un `PUT` ne la fixe), exposée en lecture dans `SiteDto` et `ClientDto`. La
règle devient : **5 ans après la fin du dernier contrat ; à défaut de contrat, 5 ans
après la création**. Pour un site qui a eu des contrats, la seconde condition ne retire
rien, puisqu'il a été créé avant leur échéance.

Trois choses à savoir sur cette correction :

- ⚠️ **Les lignes antérieures à la migration sont datées du jour de la migration.** Sans
  défaut en base, une colonne `timestamp` non nullable les aurait datées du 1er janvier
  de l'an 1, et le script les aurait toutes vues hors délai depuis deux mille ans : la
  purge la plus large possible, sur les lignes les moins bien connues. `now()` donne une
  borne **haute** de leur vraie création, donc une purge retardée, jamais avancée. Gardé
  par `Ligne_inseree_sans_DateCreation_est_datee_par_la_base`, éprouvé par un sabotage.
- **Deux gardes nouvelles dans les `DELETE`**, sur des liens que la base ne protège pas,
  faute de clé étrangère : un site qui porte encore du stock (`Stocks.EmplacementId`)
  n'est pas supprimé, et un client rattaché à un compte `ClientSyndic`
  (`AspNetUsers.ClientId`) non plus, ce compte devant d'abord être pseudonymisé.
  Vérifiées dans une transaction annulée.
  Depuis l'étape 22, le lien du stock au site est une vraie clé étrangère,
  `Stocks.SiteId`, et les mouvements en portent deux de plus : la base protège ce lien,
  et la garde du script ne sert plus qu'à ne pas compter comme supprimable ce que le
  `DELETE` refuserait.
- **Un prospect jamais signé suit la même règle de 5 ans.** Pour la prospection pure, le
  référentiel de la CNIL sur la gestion commerciale retient plutôt **3 ans** après le
  dernier contact. C'est une durée de §5 de plus à faire valider par l'exploitant.

`DateCreation` ne fait pas tomber l'exemption de `ClientDto` posée au §8.3 : c'est la
date d'ouverture de la relation contractuelle, de même nature que les dates d'un
contrat, et non une information sur l'interlocuteur.

### 9.8 — Le dossier d'un agent ne contient pas ses passages

Constaté en écrivant ce lot, le 6 novembre 2026. `GET /api/agents/{id}/dossier` a été
écrit à l'étape 17, avant les passages : il ne porte ni les **pointages** de l'agent, ni
la **checklist** qu'il a déclarée, ni les **anomalies** qu'il a signalées. Or ce sont des
données le concernant, et l'article 15 en exige la communication. Ce sont même les seules
dont il est l'**auteur** et non le sujet.

Le défaut a la forme que §6 redoutait : une réponse d'accès qui a l'air complète.
Proposition, à prendre : ajouter au dossier ses pointages — heure, distance, précision,
preuve —, ses déclarations de checklist et ses anomalies, description comprise, mais
**sans le contenu des photos**, dont il donnerait le nombre. Une photo peut montrer un
tiers, que l'article 15(4) protège ; l'agent qui la veut la demande, et l'administrateur
la lui remet après l'avoir regardée.

⚠️ La pseudonymisation d'un agent ne touche pas ses passages, et c'est juste : ils
tiennent à ses vacations, dont l'identifiant survit, et ne le nomment pas. Ils partent
avec leur durée — un an —, pas avec lui.

### 9.9 — Rien ne dit à l'agent de ne photographier personne

Constaté en écrivant §8.4. La description d'une anomalie porte la consigne « Sans nommer
de personne », dans son texte d'aide. Le bouton *Ajouter une photo* ne porte aucune
consigne équivalente, alors que c'est la photo qui peut montrer quelqu'un — l'occupant
d'un couloir, un collègue, l'agent lui-même dans un miroir.

C'est la même mesure que §9.5 sur les consignes d'un site, à la source et presque sans
coût : une phrase sous le bouton — « Photographier le lieu, jamais une personne » —, et un
test nommé qui la garde, parce qu'une phrase d'aide retirée ne casse rien de visible.
Elle est la condition de la décision de §8.4, et la note du §7 la promet déjà.

---

## 10. Mesures de sécurité, pour l'article 32

Résumé de ce qui existe, à reporter dans le registre et l'AIPD. Le détail est dans
`CLAUDE.md` §6, §6 bis et §7.

- **Cloisonnement** : vingt et un filtres de requête globaux, forçage inconditionnel
  d'`EntrepriseId` à l'écriture, et quatre axes d'isolation — entreprise, client, agent,
  et l'équipe d'un chef.
  `EntrepriseId` n'apparaît dans aucun DTO de création : sa seule source est le jeton.
- **Authentification** : mots de passe de dix caractères minimum avec empreinte
  Identity, verrouillage après cinq échecs, limitation de débit par adresse **et** par
  compte, jeton d'accès de quinze minutes en cookie `HttpOnly Secure SameSite=Strict`,
  jeton de rafraîchissement opaque de 256 bits stocké par empreinte, rotation avec
  détection de rejeu, session absolue de quatorze jours.
- **Double authentification** (étape 23) : codes TOTP à six chiffres, **imposée** aux
  rôles `Administrateur` et `Responsable` — ceux qui lisent les domiciles et les arrêts
  maladie —, facultative pour les autres. Clé chiffrée hors de la base, codes de
  récupération gardés par empreinte, un code ne sert qu'une fois, et un code faux compte
  dans le même verrouillage que le mot de passe.
- **Révocation immédiate** : `Actif` et `SecurityStamp` vérifiés à chaque requête ;
  une désactivation ou un changement de mot de passe périme les jetons en circulation
  sans attendre leur échéance.
- **Transport et navigateur** : HTTPS, HSTS hors développement, protection CSRF par
  double soumission, politique de sécurité du contenu en `default-src 'none'` sur
  l'API, limite de taille de corps appliquée avant toute lecture.
- **Journalisation des accès** : dix-sept actions — douze lectures, dont les trois que
  personne n'aurait cherchées, les deux routes de candidats et le planning, et cinq
  gestes d'administrateur sur le compte ou la fiche d'autrui.
- **Certification des passages** (étape 25) : coordonnées jetées par le serveur, qui
  n'en garde que la distance au site ; position demandée au seul geste ; métadonnées des
  photos retirées par le serveur, sans s'en remettre à l'écran ; photos relues en
  `Cache-Control: private, no-store` ; seul l'agent affecté pointe ; une vacation pointée
  est figée.
- **Mots de passe des comptes créés** (étape 24) : tirés par le serveur, montrés une seule
  fois, et remplacés obligatoirement à la première connexion — un administrateur ne garde
  la connaissance du mot de passe d'un collègue que jusque-là.
- **Traçabilité du code** : 687 tests côté serveur, 320 cas côté interface — dont 106
  rejoués dans cinq fuseaux —, et la discipline de validation par sabotage décrite dans
  `CLAUDE.md`.
- ⚠️ **Les valeurs de paramètres SQL ne sont pas journalisées** :
  `EnableSensitiveDataLogging` n'est activé nulle part, et ne doit jamais l'être en
  production — les requêtes d'EF Core sont tracées au niveau `Information`, et leurs
  paramètres y apparaîtraient en clair.

---

## 11. Ce qui reste à faire, et par qui

**Par l'exploitant**, hors logiciel : valider les durées du §5, faire relire ce
fichier, remettre la note du §7 — **avant** d'ouvrir *Ma journée* aux agents, et après
avoir consulté le CSE s'il existe (`DEPLOIEMENT.md` §11) —, ordonnancer
`purge-pointages.sql`, conclure les contrats de l'article 28 avec l'hébergeur,
le fournisseur SMTP et le stockage des sauvegardes, fixer la durée de conservation de
ces sauvegardes, tenir l'AIPD du §9.6, désigner un point de contact.

**Par le logiciel** — état au 6 novembre 2026, dans l'ordre de gravité où les écarts ont
été relevés :

| Écart | État |
|---|---|
| §9.1 — les polices chargées chez Google | ✅ servies en propre, garde dans `npm run verifier` |
| §9.2 — l'adresse IP des sessions jamais purgée | ✅ `purge-jetons-rafraichissement.sql` |
| §5 — aucun mécanisme de conservation métier | 📋 `conservation-donnees-metier.sql`, rapport avec `DELETE` commenté |
| §8.2 — le résidu de santé des absences `Maladie` | 📋 `degradation-absences-maladie.sql`, rapport avec `UPDATE` commenté |
| §9.5 — `Sites.Consignes` nommant une personne | ✅ aide du champ, gardée par un test |
| §9.3 — aucun effacement pour un compte sans fiche agent | ✅ `POST /api/auth/comptes/{id}/anonymiser` |
| §9.4 — aucun export ni portabilité | ✅ `GET /api/agents/{id}/dossier` |
| §9.7 — sites et clients sans contrat ni date | ✅ `DateCreation`, migration `DateCreationSurSitesEtClients` |
| §5 — les mouvements de stock et leur auteur, écrits à l'étape 22 sans conservation | 📋 `conservation-donnees-metier.sql` §8, rapport avec `DELETE` commenté |
| §5 — les pointages des passages, étape 25 | ✅ `purge-pointages.sql`, qui supprime sur `-v confirmer=1` |
| §5 — checklist, anomalies et photos, étape 25 | 📋 `conservation-donnees-metier.sql` §9, rapport avec `DELETE` commenté |
| §9.8 — le dossier d'un agent sans ses passages | **ouvert** |
| §9.9 — aucune consigne de ne photographier personne | **ouvert** |

**Deux écarts restent ouverts côté logiciel**, tous deux relevés en écrivant ce lot, et
tous deux petits : §9.8 complète une réponse d'accès, §9.9 est une phrase sous un bouton.
Le reste est à l'exploitant : valider les durées, dont celle des prospects (§9.7) et
celles des anomalies, et décommenter les purges.

⚠️ **Quatre des onze corrections sont des rapports, pas des purges**, et il ne faut pas lire
le tableau trop vite : `conservation-donnees-metier.sql` — pour trois lignes du
tableau — et `degradation-absences-maladie.sql` portent leurs `DELETE` et `UPDATE` en
commentaire. Ils
attendent la validation juridique des durées du §5, qui appartient à l'exploitant.
