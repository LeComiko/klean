# Déployer KLEAN

De zéro jusqu'à la première connexion, puis la vie d'une instance : mises à jour,
sauvegardes, retour arrière, secrets, journaux. Chaque commande de ce fichier a été
jouée telle quelle sur une instance d'essai avant d'être écrite ici.

KLEAN tourne en trois conteneurs : **PostgreSQL**, **l'API**, et **Caddy**, qui
obtient le certificat HTTPS, sert l'interface et relaie `/api` vers l'API. Un
quatrième, `migrer`, ne tourne que quand vous le lancez.

```
navigateur ──https──► caddy :443
                       ├─ /        → l'interface
                       └─ /api/*   → api:8080, en clair sur le réseau interne
                                       └─► postgres:5432, jamais exposé
```

---

## 1. Ce qu'il faut

- Une machine Linux avec **Docker Engine** et le greffon **Docker Compose v2**
  (`docker compose version` doit répondre).
- Un **nom de domaine** dont l'enregistrement DNS pointe sur cette machine.
- Les ports **80 et 443** ouverts depuis Internet : Let's Encrypt vérifie le
  domaine par le port 80 avant d'émettre le certificat.
- `git` et `openssl`.

KLEAN n'a besoin d'**aucun accès sortant** pour servir son interface : les polices
sont servies en propre (`RGPD.md` §9.1). Les seules sorties sont Let's Encrypt, pour
le certificat, et votre serveur SMTP, si vous en configurez un.

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
| `KLEAN_ADMIN_EMAIL`, `KLEAN_ADMIN_MOT_DE_PASSE`, `KLEAN_RAISON_SOCIALE` | le premier compte, et l'entreprise qu'il administre |

⚠️ Le mot de passe de PostgreSQL doit être **hexadécimal**. Un `;` ou un `=` couperait
la chaîne de connexion, et l'API échouerait au démarrage sur une erreur qui ne parle
pas de mot de passe.

Le mot de passe administrateur suit les règles de KLEAN : **dix caractères au moins,
une majuscule, une minuscule, un chiffre**. Sinon l'API refuse de démarrer, et dit
pourquoi dans `docker compose logs api`.

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

1. Ouvrez `https://VOTRE-DOMAINE` et connectez-vous avec le compte de `.env`. Ayez sous la
   main un téléphone portant une application d'authentification (Aegis, FreeOTP, Google
   ou Microsoft Authenticator…). La connexion enchaîne deux étapes :
   - **Choisir votre mot de passe.** Celui de `.env` est provisoire : la connexion
     impose de le remplacer, et refuse de le reprendre tel quel.
   - **La double authentification**, que le rôle administrateur impose. La connexion
     affiche un QR code, puis remet **dix codes de récupération** : rangez-les hors de
     cette machine, ils sont la seule sortie si le téléphone se perd (section 10).
2. **Videz** dans `.env` les quatre lignes `KLEAN_ADMIN_*`, `KLEAN_RAISON_SOCIALE`
   et `KLEAN_SIRET`.

Le second geste n'est pas cosmétique. Ces valeurs ne servent que si la base n'a
**aucun** compte ; ensuite elles ne servent plus à rien, et le mot de passe initial
resterait écrit en clair sur le disque. Il n'est plus valable après la première
connexion, mais un mot de passe initial est souvent réemployé ailleurs.

⚠️ **Le remplacement imposé du mot de passe initial n'a pas encore été vu sur une instance
neuve** : la série de tests ne peut pas offrir une base sans aucun compte. Si la première
connexion passe directement au QR code sans demander de mot de passe, changez-le à la main
à l'écran **Compte**, et signalez-le.

Les comptes suivants se créent à l'écran **Comptes**, section *Entreprise*. Aucun mot de
passe n'y est saisi : KLEAN en tire un, l'affiche **une seule fois**, et la personne le
remplace à sa première connexion. Transmettez-le par un autre canal que son adresse
électronique — de vive voix, par SMS —, ce qui n'en laisse aucune copie dans une boîte
aux lettres.

⚠️ **Avant de créer le premier compte d'agent, lisez la section 11.** Un compte d'agent
ouvre *Ma journée*, qui relève sa position à chaque passage : les salariés doivent en
avoir été informés avant, et le CSE consulté s'il existe.

---

## 3. Mettre à jour

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

### Sauvegarder

```bash
docker compose exec -T postgres pg_dump -U klean -d klean -Fc > klean-$(date +%F).dump
```

Le format `-Fc` est compressé, et `pg_restore --list klean-AAAA-MM-JJ.dump` en montre
le contenu sans rien restaurer. Exemple de tâche quotidienne, dans `crontab -e` :

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

### Restaurer

```bash
docker compose stop api
docker compose exec -T postgres pg_restore -U klean -d klean --clean --if-exists --no-owner --exit-on-error < klean-AAAA-MM-JJ.dump
docker compose start api
```

⚠️ **Sur une machine neuve, créez d'abord le rôle `klean_app`.** Un rôle vit dans la
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
l'API — l'archive la plus récente, puisqu'elle contient aussi les clés plus anciennes :

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
| `KLEAN_ADMIN_MOT_DE_PASSE` | ne pas le tourner : le **vider** (§2.4) | — |

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

Les journaux sont plafonnés à cinq fichiers de 10 Mo par conteneur, puis remplacés. Ils
ne contiennent ni adresse IP, ni identité de l'utilisateur, ni contenu de requête
(`RGPD.md` §3). Caddy n'écrit **aucun** journal d'accès ; en ajouter un consignerait
l'adresse IP de chaque visiteur, avec la durée de conservation que cela suppose.

---

## 9. Cas particuliers

**Le réseau `172.28.0.0/24` existe déjà sur la machine.** Changez-le dans
`docker-compose.yml`, en trois endroits qui doivent rester cohérents : `subnet`,
`ipv4_address` de Caddy, et `Proxy__Reseaux__0` de l'API. Ce dernier doit désigner
Caddy **seul**, en `/32`. Plus large, il laisserait un autre conteneur choisir
l'adresse sous laquelle il est compté par la limitation de débit.

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
