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

1. Ouvrez `https://VOTRE-DOMAINE` et connectez-vous avec le compte de `.env`.
2. Écran **Compte** : changez le mot de passe.
3. **Videz** dans `.env` les quatre lignes `KLEAN_ADMIN_*`, `KLEAN_RAISON_SOCIALE`
   et `KLEAN_SIRET`.

Le troisième geste n'est pas cosmétique. Ces valeurs ne servent que si la base n'a
**aucun** compte ; ensuite elles ne servent plus à rien, et le mot de passe initial
resterait écrit en clair sur le disque. Il n'est plus valable une fois changé, mais
un mot de passe initial est souvent réemployé ailleurs.

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

### Restaurer

```bash
docker compose stop api
docker compose exec -T postgres pg_restore -U klean -d klean --clean --if-exists --no-owner --exit-on-error < klean-AAAA-MM-JJ.dump
docker compose start api
```

L'API est arrêtée pendant la restauration : elle écrirait sinon dans des tables en
train d'être remplacées. Tout ce qui a été saisi après la sauvegarde est **perdu**,
sessions comprises : qui s'est connecté depuis devra se reconnecter.

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
| `cles-api` | clés de Data Protection, renouvelées seules tous les 90 jours | les liens de réinitialisation de mot de passe en cours deviennent invalides, rien d'autre |
| `caddy-donnees` | le certificat HTTPS | Caddy en redemande un ; Let's Encrypt plafonne les émissions par domaine |

`docker compose down` les conserve. **`docker compose down -v` les détruit tous.**

---

## 7. Conservation des données

Les durées de conservation sont dans `RGPD.md` §5, et les scripts dans
`backend/Scripts/`. **Aucun ne tourne seul**, et aucun ne supprime tel quel : chacun
commence par un rapport, et ses `DELETE` sont en commentaire jusqu'à ce que vous ayez
validé les durées.

```bash
docker compose exec -T postgres psql -U klean -d klean < backend/Scripts/purge-journal-acces.sql
docker compose exec -T postgres psql -U klean -d klean < backend/Scripts/purge-jetons-rafraichissement.sql
docker compose exec -T postgres psql -U klean -d klean < backend/Scripts/conservation-donnees-metier.sql
docker compose exec -T postgres psql -U klean -d klean < backend/Scripts/degradation-absences-maladie.sql
```

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
