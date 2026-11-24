# Déployer KLEAN

De zéro jusqu'à la première connexion, puis la vie d'une instance : mises à jour,
sauvegardes, retour arrière, secrets, journaux. Chaque commande de ce fichier a été
jouée telle quelle sur une instance d'essai avant d'être écrite ici.

**Le chemin recommandé est le script** : `README.md`, section *Installer KLEAN sur un
serveur*, une commande qui installe Docker, pose les questions, obtient le certificat et
installe la commande `klean` pour la suite (mises à jour, sauvegardes, restauration). Ce
document reste le **parcours manuel** — pour qui ne veut pas du script, ou veut
comprendre ce qu'il fait — et la référence de ce que `klean` décrit en une ligne. Les
deux ne se mêlent pas sur une même instance : une installation faite par le script se
tient par `klean`, et ses fichiers sont dans `/opt/klean`.

KLEAN tourne en trois conteneurs : **PostgreSQL**, **l'API**, et **Caddy**, qui
obtient le certificat HTTPS, sert l'interface et relaie `/api` vers l'API. Un
quatrième, `migrer`, ne tourne que quand vous le lancez.

```
navigateur ──https──► caddy :443
                       ├─ /        → l'interface
                       └─ /api/*   → api:8080, en clair sur le réseau interne
                                       └─► postgres:5432, jamais exposé
```

**Caddy est sur deux réseaux Docker.** `klean`, à l'adresse fixe `172.28.0.10`, le relie à
l'API ; `api`, `postgres` et `migrer` n'ont que celui-là. `public`, que Caddy est seul à
rejoindre, reçoit les visiteurs : il a l'IPv6, sur une plage privée `fd91:8576:6d::/64`,
et c'est lui qui laisse l'API compter chaque visiteur sous sa **vraie adresse**. Sur
`klean` seul, sans IPv6, Docker retransmet les connexions IPv6 depuis sa passerelle,
`172.28.0.1` : tous les visiteurs IPv6 partageraient alors une même adresse, donc un même
plafond de connexion. Pourquoi un second réseau plutôt qu'une option sur le premier :
Docker refuse de modifier un réseau où un conteneur est branché, et une mise à jour lance
la migration pendant que Caddy tourne. La plage de `public` n'a pas à être changée : aucune
adresse IPv4 n'y est fixée, et la plage IPv6 est privée. Si elle chevauche un réseau de la
machine, voir §9.

---

## 1. Ce qu'il faut

- Une machine Linux avec **Docker Engine** et le greffon **Docker Compose v2**
  (`docker compose version` doit répondre). La réception des visiteurs IPv6 sous leur
  vraie adresse (0.2.1) n'est **mesurée que sous Docker 29**, `ip6tables` actif — le
  défaut depuis Docker 27. Sur une version plus ancienne, ou avec `"ip6tables": false`
  dans `daemon.json`, les visiteurs IPv6 peuvent de nouveau arriver sous une adresse
  commune, sans aucun symptôme : faites la vérification du §9.
- Un **nom de domaine** dont l'enregistrement DNS pointe sur cette machine.
- Les ports **80 et 443** ouverts depuis Internet : Let's Encrypt vérifie le
  domaine par le port 80 avant d'émettre le certificat.
- `git` et `openssl`.

KLEAN n'a besoin d'**aucun accès sortant** pour servir son interface : les polices
sont servies en propre (`RGPD.md` §9.1). Les seules sorties sont Let's Encrypt, pour
le certificat, et votre serveur SMTP, si vous en configurez un. Avec le script, s'y
ajoutent le **téléchargement des versions** — `github.com` pour l'archive, `ghcr.io` pour
les images — et, à l'installation seulement, le dépôt de Docker : aucune donnée de
l'instance n'y est envoyée (`RGPD.md` §4).

**Une sortie de plus si une entreprise active la localisation des sites** : l'API appelle
`data.geopf.fr`, la Géoplateforme de l'IGN, en **HTTPS sur le port 443**, à chaque clic sur
*Localiser* dans la fiche d'un site. La localisation est désactivée par défaut, et se coche
dans l'écran *Entreprise*. Derrière un pare-feu sortant, ouvrez ce port vers cet hôte ;
fermé, le bouton répond que la Géoplateforme ne répond pas, et les coordonnées se
saisissent à la main. Si vous faites passer les sorties de l'API par un proxy de
l'environnement (`HTTPS_PROXY`), il voit l'hôte, pas le contenu : la requête est chiffrée
de bout en bout, et l'adresse recherchée, qui est dans son URL, ne lui est pas lisible —
sauf à un proxy qui déchiffre le TLS, ce qui demanderait d'installer son autorité dans le
conteneur de l'API.

---

## 2. Installation

### 2.1 Récupérer le code et préparer les secrets

```bash
git clone <adresse du dépôt> klean && cd klean
cp .env.example .env && chmod 600 .env
```

Ouvrez `.env` et renseignez au moins :

| Variable | Valeur |
|---|---|
| `KLEAN_DOMAINE` | le nom de domaine, sans `https://` |
| `POSTGRES_PASSWORD` | le résultat de `openssl rand -hex 32` |
| `KLEAN_JWT_CLE` | le résultat de `openssl rand -base64 64 \| tr -d '\n'` |
| `KLEAN_CODE_INSTALLATION` | **première voie** : un code de vingt symboles, tiré par la commande de `.env.example` — l'entreprise et son administrateur se créent dans le navigateur |
| `KLEAN_IPV6` | `true` (le défaut) ou `false` : l'IPv6 du réseau `public` de Caddy, qui reçoit les visiteurs sous leur vraie adresse. Le script l'écrit d'après la machine à l'installation ; une mise à jour l'ajoute à `true`. `false` pour une machine sans IPv6, à la main (§9) |
| `KLEAN_ADMIN_EMAIL`, `KLEAN_ADMIN_MOT_DE_PASSE`, `KLEAN_RAISON_SOCIALE` | **seconde voie** : le premier compte, et l'entreprise qu'il administre, créés au démarrage |

⚠️ Le mot de passe de PostgreSQL doit être **hexadécimal**. Un `;` ou un `=` couperait
la chaîne de connexion, et l'API échouerait au démarrage sur une erreur qui ne parle
pas de mot de passe.

**Une des deux voies, au choix**, pour créer l'entreprise et son premier administrateur.
Elles ne servent que sur une base sans aucun compte :

- **Le code d'installation** est la voie du script, et la seule où aucun mot de passe
  n'est écrit sur le disque : vous le choisissez dans le navigateur. Le code se tire
  ainsi, jamais recopié d'un exemple :

  ```bash
  LC_ALL=C tr -dc 'ABCDEFGHJKMNPQRSTUVWXYZ23456789' < /dev/urandom | head -c 20; echo
  ```

  Vingt symboles de cet alphabet exactement, sinon l'API refuse de démarrer : un code
  plus court serait configuré à moitié. Ne le passez dans aucune adresse ni aucun lien :
  il finirait dans l'historique du navigateur.
- **Les trois variables d'amorçage** créent l'entreprise et l'administrateur dès le
  démarrage. Le mot de passe suit les règles de KLEAN : **dix caractères au moins, une
  majuscule, une minuscule, un chiffre**. Sinon l'API refuse de démarrer, et dit pourquoi
  dans `docker compose logs api`. `KLEAN_SIRET`, facultative, donne le SIRET de
  l'entreprise ; sans elle, quatorze zéros.

Avec les trois variables, le code n'est pas lu. **Sans l'une ni l'autre voie, l'API refuse
de démarrer** sur une base sans compte, en nommant les deux. Une ou deux variables
d'amorçage seulement, sans code, la font aussi refuser : l'amorçage est incomplet.

### 2.2 Construire et démarrer

```bash
docker compose build
docker compose up -d postgres
docker compose run --rm migrer
docker compose up -d
```

Dans cet ordre. L'API ne crée pas les tables : elle échoue au démarrage tant que
`migrer` n'est pas passé, et Docker la relance en boucle.

### 2.3 Vérifier

```bash
curl https://VOTRE-DOMAINE/api/sante
```

Réponse attendue : `{"statut":"ok","baseDeDonnees":"joignable",…}`. Si rien ne
répond, `docker compose logs caddy` dit en général pourquoi le certificat n'a pas été
émis — presque toujours le DNS ou le port 80.

### 2.4 Première connexion

Ayez sous la main un téléphone portant une application d'authentification (Aegis,
FreeOTP, Google ou Microsoft Authenticator…) : le rôle administrateur impose la **double
authentification**, et la première connexion la fait équiper. Elle affiche un QR code,
puis remet **dix codes de récupération** : rangez-les hors de cette machine, ils sont la
seule sortie si le téléphone se perd (section 10).

#### Par le code d'installation

1. Ouvrez `https://VOTRE-DOMAINE`. Tant qu'aucun compte n'existe, KLEAN ouvre
   l'**assistant d'installation** au lieu de la connexion. Il compte quatre écrans :
   - **le code** de `.env`, tel quel, tirets et majuscules sans importance ;
   - **l'entreprise** : raison sociale, SIRET à quatorze chiffres, et territoire, qui
     fixe les jours fériés et l'heure de la journée ;
   - **votre compte** : adresse, nom, prénom, et le mot de passe, que vous choisissez —
     dix caractères au moins, une majuscule, une minuscule, un chiffre ;
   - **ce que vous devez savoir**, recopié ci-dessous, puisque cet écran ne se revoit pas.
2. **Connectez-vous tout de suite**, par *Se connecter*. Le mot de passe choisi dans
   l'assistant n'est pas provisoire : la connexion passe directement à la double
   authentification.
3. **Videz** dans `.env` la ligne `KLEAN_CODE_INSTALLATION`, puis recréez l'API, qui le
   garde sinon dans son environnement :

   ```bash
   docker compose up -d api
   ```

Le troisième geste n'est pas cosmétique. Le code ne sert plus — l'assistant
répond « déjà installé » dès qu'un compte existe —, mais il resterait un secret en clair
sur le disque, et dans l'environnement du conteneur. Avec le script, rien à faire :
`klean` le retire à sa commande suivante, au plus tard **dans l'heure** : la tâche horaire du
cron le retire à chaque passage, sans attendre la sauvegarde de 3 h. Ce passage n'écrit
que s'il a eu quelque chose à faire, au journal du système : `journalctl -t klean`.

Ce que dit le quatrième écran, et qui reste vrai après lui :

- **Vous êtes responsable du traitement** des données de vos salariés et de vos clients.
- **La note d'information aux agents** se remet avant d'importer leurs données, et avant
  qu'ils utilisent *Ma journée*. Son modèle est en `RGPD.md` §7 ; la section 11 dit le
  reste.
- **Les durées de conservation** proposées en `RGPD.md` §5 sont à valider (section 7).
- **La localisation automatique des sites est désactivée** : les coordonnées se
  saisissent à la main tant que vous ne l'activez pas. Elle s'active dans l'écran
  *Entreprise*, dont l'aide dit ce qui part vers l'IGN, et rien ne part avant ce choix ;
  `RGPD.md` §4 et T6 en disent le détail, et ce qu'elle vous demande envers vos clients
  particuliers. Une ligne `KLEAN_GEOCODAGE` restée dans le `.env` d'une 0.1.x ou d'une 0.2.x
  n'est lue par rien : le réglage vit dans l'application.
- **La double authentification s'équipe dès la première connexion**, comme ci-dessus.

Un code refusé ramène au premier écran. Au-delà de trente codes faux reçus par le serveur
en quinze minutes, de qui que ce soit, l'assistant le dit et demande de réessayer quelques
minutes plus tard. Un code juste n'est jamais retenu par ce plafond. Si l'assistant
s'arrête sur « Une entreprise existe déjà sans aucun compte », voyez la section 12.

⚠️ **Ce parcours a été joué au navigateur sur une base jetable, pas encore sur l'instance
d'essai.**

#### Par les variables d'amorçage

1. Ouvrez `https://VOTRE-DOMAINE` et connectez-vous avec le compte de `.env`. La connexion
   enchaîne deux étapes :
   - **Choisir votre mot de passe.** Celui de `.env` est provisoire : la connexion
     impose de le remplacer, et refuse de le reprendre tel quel.
   - **La double authentification**, comme ci-dessus.
2. **Videz** dans `.env` les quatre lignes `KLEAN_ADMIN_EMAIL`,
   `KLEAN_ADMIN_MOT_DE_PASSE`, `KLEAN_RAISON_SOCIALE` et `KLEAN_SIRET`.

Le second geste n'est pas cosmétique. Ces valeurs ne servent que si la base n'a
**aucun** compte ; ensuite elles ne servent plus à rien, et le mot de passe initial
resterait écrit en clair sur le disque. Il n'est plus valable après la première
connexion, mais un mot de passe initial est souvent réemployé ailleurs.

⚠️ **Le remplacement imposé du mot de passe initial n'a pas encore été vu sur une instance
neuve.** La série de tests éprouve désormais que l'amorçage marque ce compte provisoire
(`AmorcageTests`), et le parcours d'un compte provisoire est celui de tout compte créé.
Si la première connexion passe pourtant directement au QR code sans demander de mot de
passe, changez-le à la main à l'écran **Compte**, et signalez-le.

Les comptes suivants se créent à l'écran **Comptes**, section *Entreprise*. Aucun mot de
passe n'y est saisi : KLEAN en tire un, l'affiche **une seule fois**, et la personne le
remplace à sa première connexion. Transmettez-le par un autre canal que son adresse
électronique — de vive voix, par SMS —, ce qui n'en laisse aucune copie dans une boîte
aux lettres.

⚠️ **Avant de créer le premier compte d'agent, lisez la section 11.** Un compte d'agent
ouvre *Ma journée*, qui relève sa position à chaque passage : les salariés doivent en
avoir été informés avant, et le CSE consulté s'il existe.

### 2.5 Une instance de démonstration, facultative

Cette étape sert à montrer KLEAN rempli : à un client, à une équipe, avant de choisir.
Jouez-la juste après la création de l'entreprise — par l'assistant ou par les variables
d'amorçage, section 2.4 —, avant toute saisie :

```bash
docker compose run --rm api demonstration
```

Par le code d'installation, aucune entreprise n'existe avant l'assistant : la commande
refuse alors, et le dit. La commande remplit l'entreprise de l'instance avec :
- deux clients, cinq sites et six agents fictifs ;
- six semaines de vacations autour du jour même ;
- des remplacements, des passages pointés avec leurs anomalies, et un stock.

Elle crée aussi un compte par rôle : responsable, chef d'équipe, agent et client syndic.
Elle sort ensuite, et l'API qui tourne n'en est pas affectée.

Une ligne de journal en JSON la précède quand le SMTP n'est pas configuré. Elle est sans
conséquence.

⚠️ **Les mots de passe de ces comptes s'affichent une fois, dans la sortie de la
commande, et nulle part ailleurs.** Avec `--rm`, le conteneur et son journal
disparaissent à la sortie. Notez-les avant de fermer le terminal. Chacun est provisoire :
la première connexion le fait remplacer, et le compte responsable équipe un téléphone,
comme l'administrateur.

⚠️ **Elle ne s'installe que dans une instance neuve** : une seule entreprise, sans client
ni agent. Sinon, elle refuse en disant pourquoi, sort avec le code 1 et n'écrit rien. Elle
ne complète jamais des données existantes, même les siennes. Pour recommencer, videz la
base seule, puis reprenez la section 2.2 :

```bash
docker compose down
docker volume rm klean_postgres     # la base, et rien d'autre
```

La base neuve n'a aucun compte : remettez dans `.env` un code d'installation neuf, ou les
variables d'amorçage, que vous aviez vidés à la section 2.4. Sans l'un ni l'autre, l'API
refuse de démarrer.

Évitez `down -v` : il emporte aussi le trousseau `cles-api` et les certificats de Caddy,
et Let's Encrypt plafonne les émissions par domaine.

Elle n'a pas sa place sur l'instance qui recevra vos vraies données. Des agents fictifs
mêlés à vos salariés fausseraient le planning, le remplacement et le registre de
`RGPD.md`.

⚠️ **Elle n'est pas atomique.** Si une règle de KLEAN refuse un de ses gestes en cours
de route, elle s'arrête et le dit, mais ce qui précède reste écrit. Le cas n'est pas
censé arriver : la série de tests joue la commande à chaque passage. S'il arrive quand
même, signalez-le, et repartez d'un volume vide.

La commande ne laisse aucune ligne au journal d'accès. Elle passe par les règles de
l'API, mais pas par sa journalisation.

---

## 3. Mettre à jour

### Avec `klean`

```bash
sudo klean mettre-a-jour            # la dernière version publiée
sudo klean mettre-a-jour 1.2.3      # une version précise
```

Dans cet ordre, et chaque pas s'arrête sans rien changer s'il échoue :

1. **Téléchargement** de la version — vérifié contre l'empreinte publiée avec elle, et la
   version annoncée par l'archive doit être celle demandée — puis des images, **pendant que
   l'ancienne version tourne**. Un réseau coupé ici n'a rien arrêté.
2. **Sauvegarde** d'avant la mise à jour (`klean-….-avant-<version>.dump`), l'API en marche :
   le trousseau se lit dans son conteneur.
3. **Arrêt** de l'API, mise en place des fichiers de la nouvelle version, **migration** de
   la base, **démarrage**.
4. **Vérification** : KLEAN doit répondre en HTTPS.

⚠️ **Les saisies faites entre la sauvegarde et l'arrêt de l'API** — quelques secondes — ne
sont pas dans la sauvegarde : un `revenir` les perd (section 5).

**Depuis une version 0.1.x**, le code d'installation que celle-ci avait écrit reste dans
`.env` jusqu'à la commande `klean` suivante — `sudo klean etat` suffit — ou jusqu'au passage
horaire suivant du cron : la mise à jour est conduite par le `klean` de l'ancienne version,
qui ne le retire pas. Des comptes existant, il n'ouvre plus rien.

**Une mise à jour ratée se relance** par la même commande : elle reprend là où elle
s'était arrêtée, **sans nouvelle sauvegarde** — celle d'une base à moitié transformée
prendrait la place de celle d'avant. `sudo klean etat` signale une mise à jour en cours.

**Une version plus ancienne que l'installée est refusée.** Ses images ne connaissent pas
le schéma de la base, et ses migrations ne le défont pas : le code qui défait une
migration n'est que dans la version qui l'a introduite (`CLAUDE.md` §10). Le seul retour
est `sudo klean revenir`, section 5.

### À la main

```bash
# 1. Sauvegarder — toujours, avant toute migration (section 4)
docker compose exec -T postgres pg_dump -U klean -d klean -Fc > klean-$(date +%F).dump

# 2. Récupérer la nouvelle version
git fetch && git checkout <version>

# 3. Construire, migrer, redémarrer
docker compose build
docker compose run --rm migrer
docker compose up -d
```

⚠️ **Les migrations ne s'appliquent jamais seules.** Une nouvelle image ne change pas
le schéma tant que vous n'avez pas lancé `migrer` : c'est voulu, une migration ne doit
jamais se jouer sans sauvegarde ni lecture des notes de version. Si vous oubliez
l'étape, la nouvelle API tourne sur l'ancien schéma, et échoue au démarrage ou à la
première requête qui touche une colonne nouvelle.

**Une instance d'avant l'étape 26 prend la sécurité au niveau des lignes par cette même
marche**, sans geste de plus. La migration `SecuriteAuNiveauDesLignes` crée le rôle
`klean_app`, sous lequel l'API lit et écrit désormais, et pose une politique par table ;
elle ne change ni une table ni une donnée. ⚠️ L'oubli de `migrer` y est plus brutal
qu'ailleurs : le rôle n'existant pas encore, **toute** requête de la nouvelle API échoue.
`psql` et les scripts de la section 7, ouverts sous `klean`, propriétaire des tables,
voient toujours toutes les lignes, comme avant.

---

## 4. Sauvegarder et restaurer

### Sauvegarder, avec `klean`

Une tâche cron fait `klean sauvegarder --nuit` à chaque heure, et elle ne sauvegarde qu'**une
fois par nuit, à 3 h** dans le fuseau `KLEAN_FUSEAU` de `.env` — `Europe/Paris` par défaut,
changement d'heure compris (`/etc/cron.d/klean`, journal dans `/var/log/klean-sauvegarde.log`).
Le cron de Debian suit le fuseau du serveur, souvent UTC, et ignore `CRON_TZ` : d'où cet
appel horaire. La commande se lance aussi à la main :

```bash
sudo klean sauvegarder
```

- **La base** : `/var/backups/klean/klean-AAAA-MM-JJTHHMMSS.dump`.
- **Le trousseau** de l'API : `/var/lib/klean-trousseau/trousseau-AAAA-MM-JJTHHMMSS.tgz`,
  **séparément** — deux racines, et non deux dossiers frères, pour qu'une copie du dossier
  des sauvegardes n'emporte jamais les deux ensemble (la raison est plus bas). Une archive
  n'est écrite que si le trousseau a changé ; la plus récente contient toutes les clés.
- **Quatorze jours** sont gardés, et la plus récente n'est jamais supprimée, même hors délai.
- **Chaque sauvegarde est relue en entier** (`pg_restore -f /dev/null`) avant d'être
  gardée : un `pg_dump` interrompu laisserait sinon un fichier vide, que la rotation
  prendrait pour la bonne copie.
- **Une seule commande `klean` à la fois** (`/run/klean.lock`). La tâche de nuit attend
  jusqu'à trente minutes qu'une mise à jour ou une commande à la main finisse, plutôt que
  d'abandonner.

⚠️ **Elles restent sur le serveur.** `sudo klean etat` le rappelle à chaque appel. Copiez
`/var/backups/klean` et `/var/lib/klean-trousseau` ailleurs, **chacune de son côté** — la
séparation du trousseau est expliquée plus bas. Les trois conséquences de l'encadré
suivant valent aussi pour elles.

### Sauvegarder, à la main

```bash
docker compose exec -T postgres pg_dump -U klean -d klean -Fc > klean-$(date +%F).dump
```

Le format `-Fc` est compressé, et `pg_restore --list klean-AAAA-MM-JJ.dump` en montre
le contenu sans rien restaurer. ⚠️ `--list` ne lit que la table des matières : il réussit
sur un fichier tronqué. Pour vérifier une sauvegarde, `pg_restore -f /dev/null` la relit
en entier :

```bash
docker compose exec -T postgres pg_restore -f /dev/null < klean-AAAA-MM-JJ.dump
```
 Exemple de tâche quotidienne, dans `crontab -e` :

```
30 3 * * *  cd /chemin/vers/klean && docker compose exec -T postgres pg_dump -U klean -d klean -Fc > sauvegardes/klean-$(date +\%F).dump
```

⚠️ **Une sauvegarde contient tout**, y compris les données personnelles des salariés et
les traces d'arrêts maladie. Trois conséquences :

- **Hors de la machine.** Une sauvegarde sur le même disque ne survit pas à la panne
  qu'elle est censée couvrir.
- **Chiffrée**, par exemple `gpg --symmetric`, si elle quitte un stockage que vous
  maîtrisez.
- **Elle a une durée de conservation**, comme les données qu'elle contient
  (`RGPD.md` §5). Une sauvegarde gardée cinq ans conserve cinq ans ce que les scripts
  de purge ont supprimé de la base.

**Le trousseau de l'API se sauvegarde aussi**, avec la même régularité que la base :

```bash
docker compose exec -T api tar czf - -C /home/app/.aspnet DataProtection-Keys > cles-api-$(date +%F).tgz
```

⚠️ **Cette commande et celles de la restauration ci-dessous n'ont pas encore été jouées**
sur l'instance d'essai, contrairement au reste de ce fichier. Après une première
sauvegarde, vérifiez que l'archive n'est pas vide : `tar tzf cles-api-AAAA-MM-JJ.tgz`
doit lister au moins un fichier `key-….xml`.

La clé de double authentification de chaque compte est chiffrée par ce trousseau, qui
vit dans le volume `cles-api` et **pas** dans la base. Une base restaurée sans lui garde
des clés que plus rien ne déchiffre : tous les comptes équipés sont à rééquiper
(section 10).

⚠️ **Jamais dans la même archive que la base, ni au même endroit en clair.** La séparation
est toute la protection : une copie de la base seule ne livre aucune clé, une copie des
deux les livre toutes. Deux destinations, ou deux chiffrements distincts.

### Restaurer, avec `klean`

```bash
sudo klean restaurer /var/backups/klean/klean-AAAA-MM-JJTHHMMSS.dump
```

Sans argument, la commande liste les sauvegardes disponibles. Elle demande de taper
`RESTAURER` après avoir dit ce qu'elle va faire, et **rien n'est détruit avant** :

1. La sauvegarde est **relue en entier**. Illisible, elle est refusée : rien n'a changé.
2. L'API s'arrête, et **l'état actuel est copié** dans `/var/backups/klean`
   (`klean-….-avant-restauration.dump`, gardé quatorze jours) : une restauration lancée
   sur la mauvaise sauvegarde ne coûte pas les saisies d'aujourd'hui, et `sudo klean
   restaurer` sur cette copie défait la restauration.
3. **La base est supprimée puis recréée**, puis la sauvegarde y est restaurée **en une seule
   transaction** : un échec laisse une base vide, jamais à moitié remplie, et la commande
   dit comment reprendre.
4. **Toutes les sessions sont révoquées** : chacun doit se reconnecter. Un jeton de
   rafraîchissement d'alors rouvrirait sinon la session d'un compte que la sauvegarde
   croyait ouvert.
5. **Le trousseau est fusionné** à celui du volume `cles-api`, depuis l'archive la plus
   récente de `/var/lib/klean-trousseau`, puis les migrations de la version installée
   sont rejouées et les services redémarrés. Le rôle `klean_app` est recréé s'il manque.

⚠️ **Pourquoi la base est recréée, et non restaurée avec `--clean`.** `pg_restore --clean`
ne supprime que les objets que la sauvegarde contient. Après une mise à jour, les tables
qu'ajoutait la nouvelle version resteraient en place, et la mise à jour suivante échouerait
en les recréant.

⚠️ **Le trousseau se fusionne, jamais « seulement s'il est vide ».** L'API crée une clé
de Data Protection dès son démarrage, sans requête : sur une machine neuve où l'installation
a déjà démarré l'API, le volume n'est jamais vide, et une archive restaurée « si vide »
ne le serait jamais. Chaque clé a son identifiant : celles de l'archive s'ajoutent à celle de
la nouvelle installation, qui reste valable à côté.

⚠️ **Ce qui revient à son état d'alors.** Un compte fermé depuis la sauvegarde est de
nouveau ouvert ; un mot de passe changé depuis est de nouveau l'ancien ; les
pseudonymisations et les purges faites depuis sont défaites — rejouez-les (section 7).
Tout ce qui a été saisi après la sauvegarde est perdu.

**Une sauvegarde prise avant le premier compte rouvre l'assistant d'installation.** L'ancien
code a quitté `.env` depuis : la commande en tire un neuf avant de redémarrer l'API, et
`sudo klean code` le montre. Une sauvegarde qui porte une entreprise sans compte est
signalée : voir la section 12.

### Restaurer, à la main

```bash
docker compose stop api
docker compose exec -T postgres psql -U klean -d postgres -v ON_ERROR_STOP=1 \
    -c 'DROP DATABASE IF EXISTS klean WITH (FORCE)' \
    -c 'CREATE DATABASE klean OWNER klean'
# le rôle klean_app, s'il manque : voir ci-dessous
docker compose exec -T postgres pg_restore -U klean -d klean --no-owner --exit-on-error --single-transaction < klean-AAAA-MM-JJ.dump
docker compose start api
```

La base est recréée, pas restaurée avec `--clean` : voir plus haut. Révoquez ensuite les
sessions de la sauvegarde (la requête `UPDATE "JetonsRafraichissement"` de la section 6).

⚠️ **Sur une machine neuve, créez d'abord le rôle `klean_app`**, avant le `pg_restore`. Un rôle vit dans la
grappe PostgreSQL, pas dans la base : la sauvegarde porte les droits et les politiques
qui le nomment, pas le rôle lui-même. Sans lui, `pg_restore` s'arrête sur
`role "klean_app" does not exist`, après les tables mais avant les droits, et l'API ne
lit plus rien. Sur l'instance d'origine, le rôle existe déjà : rien à faire.

```bash
docker compose exec -T postgres psql -U klean -d klean -v ON_ERROR_STOP=1 \
    -c 'CREATE ROLE klean_app NOLOGIN NOSUPERUSER NOBYPASSRLS NOINHERIT' \
    -c 'GRANT klean_app TO CURRENT_USER'
```

Éprouvé le 6 novembre 2026 sur un conteneur `postgres:16-alpine` jetable, pas sur
l'instance d'essai : une base posée par le bundle, sauvegardée, puis restaurée dans une
grappe neuve. Sans le rôle, l'arrêt ; avec lui, une restauration complète, ses vingt et
une politiques et ses droits.

L'API est arrêtée pendant la restauration : elle écrirait sinon dans des tables en
train d'être remplacées. Tout ce qui a été saisi après la sauvegarde est **perdu**,
sessions comprises : qui s'est connecté depuis devra se reconnecter.

Le trousseau, s'il a été perdu avec la machine, se restaure **avant** de redémarrer
l'API — l'archive la plus récente, puisqu'elle contient aussi les clés plus anciennes. La
commande **s'ajoute** aux clés présentes, elle n'écrase rien : c'est ce qui la rend sûre
sur une machine dont l'API a déjà créé sa première clé.

```bash
docker compose up -d api
docker compose exec -T api tar xzf - -C /home/app/.aspnet < cles-api-AAAA-MM-JJ.tgz
docker compose restart api
```

L'archive s'extrait en tant qu'utilisateur `app` de l'image, propriétaire du dossier :
aucun droit à reprendre.

⚠️ **Restaurer ressuscite ce qui avait été purgé depuis la sauvegarde.** Après une
restauration, rejouez les scripts de conservation de la section 7.

---

## 5. Revenir en arrière

### Avec `klean`

```bash
sudo klean revenir
```

`revenir` ne joue **aucune migration descendante** : il remet les fichiers et les images de
la version précédente, et **restaure la sauvegarde prise juste avant la mise à jour**
(`klean-….-avant-<version>.dump`). C'est le choix que la fin de cette section recommande
déjà, rendu systématique : défaire une migration qui a ajouté une colonne supprime ce
qu'elle contenait, et le code qui la défait n'existe que dans la version qui l'a
introduite.

- **Dans les quatorze jours**, comme les sauvegardes : au-delà, la sauvegarde d'avant la
  mise à jour a été supprimée par la rotation, `revenir` le dit et ne touche à rien.
- Tout est vérifié **avant le premier arrêt** : les fichiers de la version précédente, la
  sauvegarde (relue en entier), les images.
- ⚠️ **Tout ce qui a été saisi depuis la mise à jour est perdu** — y compris les quelques
  secondes entre sa sauvegarde et l'arrêt de l'API. La commande le dit et demande de taper
  `REVENIR`. L'état actuel est d'abord copié (`…-avant-restauration.dump`, quatorze jours),
  et toutes les sessions sont fermées.
- **Un retour raté se relance** par `sudo klean revenir`, qui reprend : tant qu'il n'a pas
  abouti, la base est peut-être vide, et une mise à jour est refusée. `sudo klean etat` le
  signale.
- Une fois la base remise et KLEAN redémarré, si cette base a ses comptes, le code
  d'installation resté dans `.env` en est retiré, et l'API recréée sans lui — comme après
  une mise à jour.

### À la main

Une version qui pose problème se défait en **deux gestes, dans cet ordre** :

```bash
# 1. Le schéma d'abord, avec la NOUVELLE image encore en place
docker compose run --rm migrer NomDeLaDerniereMigrationAGarder

# 2. Le code ensuite
git checkout <version précédente>
docker compose build
docker compose up -d
```

⚠️ **L'ordre est imposé.** Défaire une migration exécute son code de retour, et ce code
n'existe que dans la version qui l'a introduite. L'ancienne image ne connaît pas la
nouvelle migration, et ne peut donc pas la défaire.

Le nom à donner est celui de la **dernière migration à conserver** :

```bash
docker compose exec -T postgres psql -U klean -d klean -c 'SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY 1 DESC LIMIT 5'
```

Retirez le préfixe d'horodatage : `20261002231904_AnonymisationDesComptes` se donne
comme `AnonymisationDesComptes`.

⚠️ Défaire une migration qui a **ajouté** une colonne supprime cette colonne **et ce
qu'elle contenait**. Si la nouvelle version a tourné quelques jours, restaurer la
sauvegarde de la section 3 est souvent plus sûr que défaire le schéma : on perd les
saisies depuis la mise à jour, mais on retrouve un état cohérent.

---

## 6. Secrets

Tous dans `.env`, qui ne doit jamais entrer dans git (il y est ignoré) ni dans une
image (il n'y est pas copié).

| Secret | Quand le tourner | Effet de la rotation |
|---|---|---|
| `KLEAN_JWT_CLE` | une fois par an, ou si `.env` a pu être lu | **aucun pour les utilisateurs** — voir ci-dessous |
| `POSTGRES_PASSWORD` | si `.env` a pu être lu | aucun, si l'ordre est respecté — voir ci-dessous |
| `KLEAN_SMTP_MOT_DE_PASSE` | selon votre fournisseur | aucun |
| `KLEAN_CODE_INSTALLATION` | s'il a pu être lu **avant** le premier compte : en tirer un autre (§2.1), puis `docker compose up -d api`. Après le premier compte : le **vider** (§2.4) | l'ancien code ne passe plus |
| `KLEAN_ADMIN_MOT_DE_PASSE` | ne pas le tourner : le **vider** (§2.4) | — |

⚠️ **Le code d'installation est en clair** dans `.env`, et dans l'environnement du conteneur
de l'API, jusqu'au premier compte. Le script le retire alors, à sa commande suivante ou au
passage horaire du cron — donc dans l'heure —, et recrée l'API. À la main, c'est à vous de le retirer, puis de recréer l'API : vider `.env`
seul le laisse dans le conteneur. Avant le premier compte, quiconque le lit peut créer
l'entreprise à votre place : c'est la seule garde de l'assistant. Après, il n'ouvre plus
rien, mais reste un secret écrit en clair.

### La clé de session

```bash
# remplacer KLEAN_JWT_CLE dans .env par une nouvelle valeur, puis :
docker compose up -d api
```

Mesuré : les sessions **survivent**. Le jeton d'accès signé par l'ancienne clé est
refusé, l'interface demande un nouveau jeton avec le jeton de rafraîchissement, qui
est stocké en base et non signé, et l'utilisateur ne voit rien. C'est bien pour une
rotation de routine, mais **insuffisant après une compromission** : voir plus bas.

### Le mot de passe de PostgreSQL

⚠️ **Changer `.env` seul casse l'instance.** `POSTGRES_PASSWORD` n'est lu qu'à la
**création** du volume de données. Ensuite le mot de passe vit dans la base, et l'API,
qui présente le nouveau, est refusée : `502` sur toutes les pages. L'ordre est donc :

```bash
NOUVEAU=$(openssl rand -hex 32)
docker compose exec -T postgres psql -U klean -d klean -c "ALTER USER klean PASSWORD '$NOUVEAU'"
# puis remplacer POSTGRES_PASSWORD dans .env par $NOUVEAU, et :
docker compose up -d
```

### Après une compromission : déconnecter tout le monde

Deux gestes, et **les deux sont nécessaires**. Révoquer les sessions de
rafraîchissement ne périme pas les jetons d'accès déjà émis, qui valent encore quinze
minutes. Tourner la clé seule ne déconnecte personne, comme on l'a vu plus haut.

```bash
docker compose exec -T postgres psql -U klean -d klean -c "UPDATE \"JetonsRafraichissement\" SET \"DateRevocation\" = now(), \"MotifRevocation\" = 'Révocation générale par l''exploitant' WHERE \"DateRevocation\" IS NULL"
# puis tourner KLEAN_JWT_CLE comme ci-dessus
```

Pour un seul compte, l'écran **Compte** porte *Déconnexion de tous les appareils*.

### Ce qui n'est pas dans `.env`, et doit être conservé

| Volume | Contenu | Si on le perd |
|---|---|---|
| `postgres` | la base | tout — d'où la section 4 |
| `cles-api` | clés de Data Protection, renouvelées seules tous les 90 jours | **toutes les doubles authentifications** : plus aucune clé ne se déchiffre, et chaque compte équipé est à rééquiper (section 10). Les liens de réinitialisation de mot de passe en cours deviennent aussi invalides |
| `caddy-donnees` | le certificat HTTPS | Caddy en redemande un ; Let's Encrypt plafonne les émissions par domaine |

`docker compose down` les conserve. **`docker compose down -v` les détruit tous.**

---

## 7. Conservation des données

Les durées de conservation sont dans `RGPD.md` §5, et les scripts dans
`backend/Scripts/`. **Aucun ne tourne seul**, et chacun commence par un rapport. Les
quatre premiers ne suppriment rien tel quel : leurs `DELETE` sont en commentaire jusqu'à
ce que vous ayez validé les durées.

```bash
docker compose exec -T postgres psql -U klean -d klean < backend/Scripts/purge-journal-acces.sql
docker compose exec -T postgres psql -U klean -d klean < backend/Scripts/purge-jetons-rafraichissement.sql
docker compose exec -T postgres psql -U klean -d klean < backend/Scripts/conservation-donnees-metier.sql
docker compose exec -T postgres psql -U klean -d klean < backend/Scripts/degradation-absences-maladie.sql
```

**Les pointages se purgent pour de bon**, à un an, et ne demandent aucune modification du
script : sans option il rapporte, avec `-v confirmer=1` il rapporte puis supprime. Il se
rejoue à l'identique — la seconde exécution ne trouve plus rien —, ce qui permet de
l'ordonnancer, une fois par semaine par exemple :

```bash
docker compose exec -T postgres psql -U klean -d klean < backend/Scripts/purge-pointages.sql
docker compose exec -T postgres psql -U klean -d klean -v confirmer=1 < backend/Scripts/purge-pointages.sql
```

⚠️ Ces deux lignes sont les seules de ce fichier qui n'ont pas été jouées sur l'instance
d'essai Docker : le script a été éprouvé sous cette forme, lu sur l'entrée standard, contre
la base de test locale, le 6 novembre 2026.

⚠️ **L'ordre compte entre deux scripts.** Une vacation de plus de cinq ans n'est purgée
par `conservation-donnees-metier.sql` qu'une fois partis ses pointages, sa checklist et
ses anomalies : le rapport le montre dans sa colonne `supprimable_maintenant`. Une
vacation retenue attend `purge-pointages.sql`, ou le traitement d'une anomalie restée
ouverte — c'est un script à rejouer, pas à forcer.

---

## 8. Journaux et erreurs

```bash
docker compose logs -f api           # en continu
docker compose logs api | grep 8636344d0a3c1c0a01ae7cf0af64da27
```

L'API écrit une ligne JSON par requête, et chaque erreur avec sa cause complète. Sur
une erreur serveur, le message affiché à l'utilisateur se termine par
« Identifiant : » suivi de 32 caractères. Demandez-le-lui et cherchez-le dans le
journal : il désigne la requête, et l'erreur avec sa pile.

Ce que fait `klean` de lui-même, la tâche horaire du cron comprise, ne passe pas par
Docker : quand elle retire le code d'installation, elle l'écrit au journal du système,
étiqueté `klean` — `sudo journalctl -t klean`. Elle ne dit rien quand il n'y avait rien à
faire. La sauvegarde de 3 h a son propre fichier, `/var/log/klean-sauvegarde.log`.

Les journaux sont plafonnés à cinq fichiers de 10 Mo par conteneur, puis remplacés. Ils
ne contiennent ni adresse IP, ni identité de l'utilisateur, ni contenu de requête
(`RGPD.md` §3), ni l'adresse d'un site envoyée à l'IGN : le client qui l'appelle n'écrit
aucun journal. Laissez à `Warning` la catégorie `System.Net.Http.HttpClient` de
`appsettings.json` : c'est la seconde garde, et la passer à `Information` ou à `Debug` la
retirerait. Caddy n'écrit **aucun** journal d'accès ; en ajouter un consignerait
l'adresse IP de chaque visiteur, avec la durée de conservation que cela suppose.

---

## 9. Cas particuliers

**Une machine sans IPv6.** Le réseau `public` demande l'IPv6 : sur un noyau qui n'en a pas
(`ipv6.disable=1`, ou `disable_ipv6=1`), sa création peut échouer. Le script l'évite de
lui-même **à l'installation** : `klean installer` écrit `KLEAN_IPV6=false` si le noyau n'a
pas l'IPv6, et Docker crée alors `public` en IPv4 seul. **Une mise à jour ne regarde pas
la machine** : quand `.env` n'a pas encore la ligne, elle ajoute le `true` du modèle — d'où
la reprise ci-dessous. Une valeur déjà écrite, même vide, n'est jamais réécrite :
c'est la vôtre. `klean` n'accepte d'office que `true`, `false` ou une valeur vide ; pour
toute autre, il laisse `docker compose` trancher, et vous dit de corriger la ligne s'il la
refuse — « oui » ou `1`, par exemple, ne passent pas.

⚠️ **Si une mise à jour vers la 0.2.1 échoue sur une machine sans IPv6** — la mise à jour
est conduite par le `klean` de l'ancienne version, qui ne regarde pas l'IPv6, et l'échec
tombe après l'arrêt de l'API et la migration —, la reprise est :

```bash
sudo nano /opt/klean/.env             # KLEAN_IPV6=false
sudo klean mettre-a-jour              # reprend là où elle s'était arrêtée
```

⚠️ **Si `up -d` répond « not connected to the network klean_public »**, un réseau `public`
resté d'un essai précédent a une autre configuration que celle que `.env` demande, et
Compose ne le recrée pas : il a été créé avec l'ancienne valeur de `KLEAN_IPV6`, ou par une
version qui n'est plus celle-ci. Il ne contient rien à garder :

```bash
sudo docker network rm klean_public   # Docker refuse tant qu'un conteneur s'en sert
```

puis relancez la commande qui a échoué : `sudo klean mettre-a-jour` reprend une mise à
jour inachevée ; après un `restaurer`, ou pour appliquer un `KLEAN_IPV6` changé à la main
sur une instance qui tourne, `sudo docker compose --project-directory /opt/klean up -d`.

`sudo klean revenir` retire ce réseau de lui-même à la fin d'un retour réussi, pour que la
mise à jour suivante n'y bute pas.

⚠️ **Une machine qui reçoit sa route IPv6 des annonces du routeur** peut perdre son IPv6
quelques heures après l'activation du réseau `public`. Un réseau Docker IPv6 exige le
transfert IPv6 (`net.ipv6.conf.all.forwarding=1`), et une interface dont `accept_ra` vaut
`1` cesse alors de suivre ces annonces : la route par défaut expire, et le domaine ne répond
plus en IPv6. Non mesuré — aucune machine de ce genre n'était à disposition —, mais c'est le
comportement documenté du noyau. La vérification, avant la mise à jour :

```bash
ip -6 route show default                          # « proto ra » : la route vient des annonces
cat /proc/sys/net/ipv6/conf/<interface>/accept_ra  # l'interface de cette route
```

Une route `proto static` ou `proto kernel`, ou `accept_ra` à `0` ou `2`, ne sont pas
concernées : c'est le cas de netplan et de systemd-networkd, qui traitent les annonces
eux-mêmes. Sinon — typiquement ifupdown en `inet6 auto` —, faites suivre les annonces malgré
le transfert, puis rechargez :

```bash
echo 'net.ipv6.conf.<interface>.accept_ra = 2' | sudo tee /etc/sysctl.d/90-klean-ipv6.conf
sudo sysctl --system
```

**Vérifier que les visiteurs IPv6 sont comptés sous leur adresse.** Connectez-vous dans un
navigateur, depuis une connexion IPv6 — un téléphone sur le réseau mobile, par exemple :
seule une connexion ouvre une session, une simple requête n'en laisse aucune trace. Puis
lisez `AdresseIp` de la dernière session :

```bash
sudo docker compose --project-directory /opt/klean exec -T postgres psql -U klean -d klean -c 'SELECT "AdresseIp", "DateCreation" FROM "JetonsRafraichissement" ORDER BY "DateCreation" DESC LIMIT 3'
```

L'adresse IPv6 du téléphone. Ni `172.28.0.1`, ni une adresse du réseau `public` —
`fd91:8576:6d::` suivi d'un suffixe, ou l'IPv4 de sa passerelle, que donne
`sudo docker network inspect klean_public` : dans ces trois cas, tous les visiteurs IPv6
partagent encore une adresse. Cette colonne est la seule adresse IP du produit ;
sa durée est dans `RGPD.md` §9.2.

**Le réseau `172.28.0.0/24` existe déjà sur la machine.** Changez-le dans
`docker-compose.yml`, en trois endroits qui doivent rester cohérents : `subnet`,
`ipv4_address` de Caddy, et `Proxy__Reseaux__0` de l'API. Ce dernier doit désigner
Caddy **seul**, en `/32`. Plus large, il laisserait un autre conteneur choisir
l'adresse sous laquelle il est compté par la limitation de débit. Le réseau `public` n'a
rien de tel à changer côté IPv4, Docker l'attribuant de son pool ; seule sa plage IPv6,
privée, peut chevaucher un réseau de la machine, auquel cas changez le `subnet` de
`public` en un `fd` d'une autre valeur. ⚠️ Une retouche de `docker-compose.yml` est
écrasée à chaque mise à jour, qui installe les fichiers de la nouvelle version : elle est à
refaire après chacune.

**Un réseau interne, sans domaine public ni accès à Let's Encrypt.** Avec
`KLEAN_DOMAINE=localhost`, Caddy émet un certificat depuis sa propre autorité, que
les navigateurs signalent comme non reconnue : c'est ainsi que ce fichier a été
éprouvé, sur une seule machine. Pour un nom interne et un certificat de votre propre
autorité, il faut ajouter une directive `tls` au `Caddyfile`, ce que ce dépôt ne fait
pas et n'a pas éprouvé.

**Un autre proxy inverse que Caddy.** Construisez seulement l'image de l'API, et
reportez-vous au `README.md`, section *Déploiement* : `Proxy:Reseaux` est
**obligatoire** derrière tout proxy, et la politique de sécurité du contenu de
l'interface se lit avec `node frontend/scripts/entetes-caddy.mjs`, au lieu d'être
recopiée.

**Une base PostgreSQL hébergée, dont le compte principal n'est pas superutilisateur.**
C'est le cas ordinaire des offres gérées. `SET ROLE klean_app`, que l'API joue à chaque
connexion, y exige d'être membre de ce rôle : la migration `SecuriteAuNiveauDesLignes`
y ajoute donc le compte qui la joue, par `GRANT klean_app TO CURRENT_USER`. Deux
conditions en découlent :

- **Un seul compte pour les migrations et pour l'API**, propriétaire des tables. Les
  droits que reçoivent les tables des migrations futures sont ceux du propriétaire qui
  les crée ; sous un autre compte, une table nouvelle resterait fermée à l'API.
- **Ce compte doit pouvoir créer un rôle** (`CREATEROLE`), sans quoi la migration échoue
  dès sa première instruction. Si `klean_app` existe déjà dans la grappe, créé par un
  autre compte, le `GRANT` peut être refusé.

⚠️ **Non éprouvé**, faute d'une telle base : c'est ce que dit la documentation de
PostgreSQL, pas ce qu'une instance a montré.

---

## 10. Double authentification

Imposée aux rôles **Administrateur** et **Responsable**, facultative pour les autres.
Le code à six chiffres change toutes les trente secondes ; chaque compte équipé a reçu
dix codes de récupération à usage unique.

### L'horloge du serveur

⚠️ **Elle doit être synchronisée.** Le serveur calcule le code attendu à partir de sa
propre heure, avec une tolérance d'un pas de trente secondes de part et d'autre. Au-delà
d'une minute d'écart environ, **plus aucun code n'est accepté** — et chaque refus compte
dans les cinq échecs du verrouillage, avec ceux du mot de passe. Les conteneurs lisent
l'horloge de la machine :

```bash
timedatectl | grep -i synchroni     # attendu : « System clock synchronized: yes »
```

Sinon, activez la synchronisation (`sudo timedatectl set-ntp true` sur une distribution
avec systemd). Le symptôme d'une horloge dérivée ne l'accuse jamais : tout le monde
« se trompe de code », puis tout le monde est verrouillé.

### Un téléphone perdu

**Avec ses codes de récupération**, la personne s'en sort seule : à la connexion,
*Téléphone perdu ? Utiliser un code de récupération* ; puis écran **Compte**, *Changer de
téléphone*, avec la même bascule et **un autre** code — chacun ne sert qu'une fois. Toutes
ses sessions se ferment, et la connexion suivante équipe le nouveau téléphone.

**Sans code de récupération** : un administrateur de l'entreprise réinitialise la double
authentification du compte, par `POST /api/auth/comptes/{id}/reinitialiser-second-facteur`.
Le geste est journalisé, et coupe les sessions du compte. ⚠️ Il n'a pas encore d'écran :
il viendra avec la liste des comptes.

**Le dernier administrateur, sans téléphone ni codes** : personne ne peut le faire pour
lui dans l'application, et c'est voulu — une route qui retire le second facteur sans code
est ce qu'un voleur de mot de passe chercherait. Le geste passe par la base, donc par
vous. **Vérifiez d'abord, hors de KLEAN, que la personne qui le demande est bien
titulaire du compte** : le script ne peut pas le faire.

```bash
# 1. Le rapport : qui est visé, rien n'est modifié
docker compose exec -T postgres psql -U klean -d klean -v email=nom@exemple.fr < backend/Scripts/reinitialiser-second-facteur.sql

# 2. Le retrait, après avoir relu le nom, l'entreprise et le rôle
docker compose exec -T postgres psql -U klean -d klean -v email=nom@exemple.fr -v confirmer=1 < backend/Scripts/reinitialiser-second-facteur.sql
```

Le second appel retire la double authentification, efface la clé et les codes, et coupe
toutes les sessions du compte. ⚠️ `-v confirmer` agit **quelle que soit sa valeur** :
`confirmer=0` confirme aussi. À sa connexion suivante, la personne équipe un nouveau
téléphone et reçoit dix codes neufs. Ce geste ne laisse **aucune ligne** au journal
d'accès de KLEAN : consignez-le de votre côté — qui l'a demandé, quand, et comment
l'identité a été vérifiée.

### Le trousseau perdu

Si le volume `cles-api` a été perdu sans sauvegarde, plus aucune clé ne se déchiffre. Une
connexion par le code de l'application échoue alors en **erreur serveur**, pas en « code faux », et le
journal de l'API dit pourquoi :

```bash
docker compose logs api | grep 'ne se déchiffre plus'
```

Chaque compte équipé est à rééquiper, et **les codes de récupération y suffisent** : ils
sont gardés par empreinte, sans le trousseau. Chacun se connecte par un code, puis *Changer
de téléphone* avec un autre, comme ci-dessus ; un administrateur ainsi reconnecté peut
réinitialiser ses collègues qui n'ont plus de codes. Pour qui n'a plus ni codes ni
administrateur, le script se rejoue compte par compte, à partir de cette liste :

```bash
docker compose exec -T postgres psql -U klean -d klean -Atc 'SELECT "Email" FROM "AspNetUsers" WHERE "TwoFactorEnabled" ORDER BY 1'
```

---

## 11. Avant d'ouvrir *Ma journée* aux agents

*Ma journée* est l'écran du rôle **Agent** : ses vacations du jour, les consignes,
*Démarrer*, la checklist, les anomalies et leurs photos, *Terminer*. *Démarrer* et
*Terminer* relèvent la position du téléphone une fois chacun. Le serveur en garde la
distance au site et jette les coordonnées (`RGPD.md`, T7). C'est néanmoins un dispositif
qui collecte une information sur chaque salarié à chaque passage, et deux obligations du
Code du travail s'appliquent **avant** la mise en service — pas après, ni « au fil de
l'eau ».

⚠️ **Ce qui suit n'est pas un avis juridique.** Ces deux points sont à faire confirmer par
votre conseil, comme les durées de `RGPD.md` §5.

1. **Informer chaque salarié concerné, par écrit, avant.** Aucune information le
   concernant personnellement ne peut être collectée par un dispositif qui n'a pas été
   porté préalablement à sa connaissance (article L1222-4). Le modèle de note est en
   `RGPD.md` §7 ; complétez-le, remettez-le contre émargement ou par un moyen qui laisse
   une date, et affichez-le. Un salarié embauché plus tard la reçoit **avant** son
   premier compte.
2. **Consulter le CSE, s'il en existe un.** Dans une entreprise d'au moins cinquante
   salariés, il est informé et consulté, préalablement à la décision, sur les moyens
   permettant un contrôle de l'activité des salariés (article L2312-38). Présentez-lui la
   note, le traitement T7 de `RGPD.md` et ses durées. Sous ce seuil, informez-le s'il
   existe.

**Ensuite seulement, créez les comptes d'agent** (section 2.4), chacun rattaché à sa fiche.
Un compte d'agent sans information préalable est exactement ce que l'article L1222-4
interdit, quelle que soit la qualité du dispositif.

Trois choses à vérifier pendant le pilote :

- **Le rayon de 200 m et la précision de 100 m** sont des constantes du code
  (`ServicePassage`), posées sans mesure de terrain. Un site dont tous les passages
  ressortent « position imprécise » est souvent un bâtiment où le téléphone ne voit pas le
  ciel : notez-le avant de conclure que les agents ne sont pas venus.
- **Un site saisi à `0, 0`** passe les bornes de saisie, et tous ses passages seront « hors
  du rayon ». Vérifiez les coordonnées de chaque site avant le premier passage.
- **Une équipe sans téléphone** : un responsable ou son chef d'équipe peut enregistrer
  une vacation « non certifiée » à la main, ce qui veut dire « faite, sans preuve ». ⚠️
  Aujourd'hui par l'API seulement (`PUT /api/interventions/{id}`) : aucun écran ne propose
  encore ce geste. Aucune saisie ne peut dire une vacation certifiée.

⚠️ **Un passage non certifié n'est pas une faute de l'agent.** Le refus de la
localisation, un téléphone sans réseau dans un sous-sol, une batterie vide le produisent
tous. Ne faites pas du statut un indicateur individuel sans l'avoir présenté comme tel au
CSE et aux salariés : `RGPD.md`, T7, explique pourquoi cela changerait le traitement.

**Ordonnancez `purge-pointages.sql`** dès la mise en service (section 7) : un an de
pointages, c'est la durée retenue, pas un plafond qui s'applique seul.

---

## 12. Une entreprise sans compte

C'est le reste d'une installation interrompue par une version 0.1.x. L'entreprise et son
administrateur n'y étaient pas créés d'un seul geste : un administrateur refusé — un mot de
passe hors des règles, par exemple — laissait l'entreprise seule en base. Depuis, ils sont créés en une transaction, et ce reste ne
se produit plus ; il peut encore se trouver dans une base ancienne, ou dans une
sauvegarde.

**Le symptôme dépend de la voie** de la section 2.1 :

- **Par le code d'installation**, l'API démarre, mais l'assistant s'arrête sur « Une
  entreprise existe déjà sans aucun compte » (réponse 409), sans rien créer.
  `sudo klean installer`, `sudo klean restaurer` et `sudo klean code` le signalent aussi,
  et ne montrent pas le code : il n'ouvrirait que ce refus.
- **Par les variables d'amorçage**, l'API refuse de démarrer, et
  `docker compose logs api` porte le même message.

L'assistant ne la reprend pas, et n'offre pas de la contourner : en changer le SIRET
laisserait l'orpheline en base. Elle se retire à la main.

**Le rapport d'abord.** Il ne modifie rien. Depuis le dossier de KLEAN — `/opt/klean`
pour une installation par le script, que le script crée en mode 700 : en root, donc, par
`sudo -i` d'abord :

```bash
cd /opt/klean
docker compose exec -T postgres psql -U klean -d klean -c \
 'SELECT e."Id", e."RaisonSociale",
    (SELECT count(*) FROM "AspNetUsers"     u WHERE u."EntrepriseId" = e."Id") AS comptes,
    (SELECT count(*) FROM "Clients"         c WHERE c."EntrepriseId" = e."Id") AS clients,
    (SELECT count(*) FROM "Agents"          a WHERE a."EntrepriseId" = e."Id") AS agents,
    (SELECT count(*) FROM "Produits"        p WHERE p."EntrepriseId" = e."Id") AS produits,
    (SELECT count(*) FROM "Emplacements"    m WHERE m."EntrepriseId" = e."Id") AS emplacements,
    (SELECT count(*) FROM "TypesPrestation" t WHERE t."EntrepriseId" = e."Id") AS types_prestation
  FROM "Entreprises" e;'
```

**Toutes les colonnes à zéro** : une entreprise orpheline, que la commande suivante
retire. **Sinon, ne supprimez rien** : ce sont des données sans compte, pas un reste
d'installation. Demandez de l'aide, en joignant le rapport.

```bash
docker compose exec -T postgres psql -U klean -d klean -c \
 'DELETE FROM "Entreprises" e
   WHERE NOT EXISTS (SELECT 1 FROM "AspNetUsers"     WHERE "EntrepriseId" = e."Id")
     AND NOT EXISTS (SELECT 1 FROM "Clients"         WHERE "EntrepriseId" = e."Id")
     AND NOT EXISTS (SELECT 1 FROM "Agents"          WHERE "EntrepriseId" = e."Id")
     AND NOT EXISTS (SELECT 1 FROM "Produits"        WHERE "EntrepriseId" = e."Id")
     AND NOT EXISTS (SELECT 1 FROM "Emplacements"    WHERE "EntrepriseId" = e."Id")
     AND NOT EXISTS (SELECT 1 FROM "TypesPrestation" WHERE "EntrepriseId" = e."Id");'
docker compose restart api
```

`psql` répond `DELETE 1` pour une orpheline retirée, et `DELETE 0` s'il n'a rien trouvé à
retirer. Le redémarrage est nécessaire : l'API ne cherche l'orpheline qu'au démarrage.
Ensuite, rechargez la page de l'assistant ; avec le script, `sudo klean code` montre le
code.

⚠️ **Les clés vers `Entreprises` sont en cascade** : clients, agents, produits,
emplacements et types de prestation, et par eux tout le reste. Un `DELETE` sans ces
conditions emporterait toutes les données de l'entreprise, en silence, et sans demander
de confirmation. Ne le raccourcissez pas. Les comptes, eux, ne sont pas rattachés à
l'entreprise par une clé : sans la première condition, un compte survivrait à son
entreprise, et n'ouvrirait plus rien.

Ce rapport et ce `DELETE` ont été joués tels quels le 23 novembre 2026, sur une base
jetable au schéma des migrations. Il y avait cinq entreprises : une orpheline vide, puis
quatre autres portant chacune une seule chose, un client, un type de prestation, un
emplacement ou un compte. Seule l'orpheline est partie (`DELETE 1`), et une seconde
exécution n'a rien retiré (`DELETE 0`). Le catalogue de la base confirme que les cinq
tables nommées plus haut sont les seules à pointer vers `Entreprises`.
