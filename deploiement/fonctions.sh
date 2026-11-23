# shellcheck shell=bash
# Fonctions de l'installation de KLEAN (SPEC.md, étape 34, lot A1), sans effet de bord
# hors de leurs arguments : aucune n'appelle Docker ni le réseau. Sourcées par
# `klean` et par tests/tests.sh, c'est ce qui les rend éprouvables.

# Le dépôt qui publie les versions, `propriétaire/nom`. deploiement/publier.sh
# remplace @DEPOT@ ; une variable d'environnement l'emporte, pour les essais.
KLEAN_DEPOT_GITHUB="${KLEAN_DEPOT_GITHUB:-@DEPOT@}"

# L'alphabet de base32 privé des caractères ambigus — ni 0, O, 1, I ni L — : 31
# symboles, donc pas du base32 au sens strict. Jamais `$`, que docker compose interprète
# dans .env.
ALPHABET_CODE='ABCDEFGHJKMNPQRSTUVWXYZ23456789'

# Un octet aléatoire, de 0 à 255. Échoue plutôt que de rendre une chaîne vide : la
# boucle de rejet de generer_code tournerait alors sans fin.
octet_aleatoire() {
  local octet
  octet=$(od -An -N1 -tu1 /dev/urandom | tr -d ' \n') || return 1
  [[ "$octet" =~ ^[0-9]+$ ]] || return 1
  printf '%s\n' "$octet"
}

# Le code d'installation de l'assistant (lot A2) : 20 symboles, environ 99 bits.
generer_code() {
  local code='' octet
  while [ "${#code}" -lt 20 ]; do
    octet=$(octet_aleatoire) || return 1
    # 248 = 8 × 31. Au-delà, l'octet est rejeté : sinon les premiers symboles de
    # l'alphabet sortiraient plus souvent que les autres.
    if [ "$octet" -lt 248 ]; then
      code+="${ALPHABET_CODE:$((octet % 31)):1}"
    fi
  done
  printf '%s\n' "$code"
}

# Le code d'installation par groupes de cinq, pour le lire à l'écran et le recopier.
# L'API retire les tirets à la saisie.
code_lisible() {
  local code="$1" groupes=()
  while [ -n "$code" ]; do
    groupes+=("${code:0:5}")
    code="${code:5}"
  done
  (IFS=-; printf '%s\n' "${groupes[*]:-}")
}

# Un secret hexadécimal de $1 octets. Hexadécimal : un « ; » ou un « = » couperait la
# chaîne de connexion de PostgreSQL (.env.example).
generer_secret() {
  od -An -N"$1" -tx1 /dev/urandom | tr -d ' \n'
  printf '\n'
}

# La valeur de la clé $2 dans le fichier .env $1, ou une chaîne vide. Les apostrophes
# qui l'entourent, posées par poser_env, sont retirées.
lire_env() {
  local valeur
  valeur=$(grep -E "^$2=" "$1" 2> /dev/null | tail -n 1 | cut -d= -f2-) || true
  valeur="${valeur#\'}"
  valeur="${valeur%\'}"
  printf '%s\n' "$valeur"
}

# Pose $2=$3 dans le fichier .env $1 : remplace la ligne de la clé, ou l'ajoute.
#
# Une valeur hors de [A-Za-z0-9._@:/+-] est écrite entre apostrophes : docker compose
# lit alors la valeur littéralement, sans interpréter `$`. Une apostrophe ou un saut de
# ligne dans la valeur n'ont pas d'écriture sûre : refusés — le saut de ligne ferait de
# la suite une clé de plus. La clé elle-même est un nom de variable, rien d'autre :
# grep la lit comme une expression régulière.
#
# Le fichier est réécrit à côté puis renommé : jamais de .env à moitié écrit. Un .env
# retouché à la main peut finir sans saut de ligne : la ligne ajoutée commence alors
# par le sien, sinon elle se collerait à la valeur précédente.
poser_env() {
  local fichier="$1" cle="$2" valeur="$3" ligne temporaire
  if ! [[ "$cle" =~ ^[A-Za-z_][A-Za-z0-9_]*$ ]]; then
    printf 'Clé refusée : « %s » ne désigne pas une variable.\n' "$cle" >&2
    return 1
  fi
  case "$valeur" in
    *"'"* | *$'\n'* | *$'\r'*)
      printf 'Valeur refusée pour %s : une apostrophe ou un saut de ligne ne peuvent pas figurer dans .env.\n' "$cle" >&2
      return 1
      ;;
  esac
  if [[ "$valeur" =~ ^[A-Za-z0-9._@:/+-]*$ ]]; then
    ligne="$cle=$valeur"
  else
    ligne="$cle='$valeur'"
  fi
  temporaire=$(mktemp "$fichier.XXXXXX") || return 1
  if grep -qE "^$cle=" "$fichier" 2> /dev/null; then
    # ENVIRON plutôt que `awk -v` : -v interprète les barres obliques inverses.
    LIGNE="$ligne" CLE="$cle" awk 'index($0, ENVIRON["CLE"] "=") == 1 { print ENVIRON["LIGNE"]; next } { print }' \
      "$fichier" > "$temporaire" || { rm -f "$temporaire"; return 1; }
  else
    # Un sous-shell, pour que l'échec de cat en sorte sans quitter l'appelant.
    (
      if [ -s "$fichier" ]; then
        cat "$fichier" || exit 1
        [ -z "$(tail -c 1 "$fichier")" ] || printf '\n'
      fi
      printf '%s\n' "$ligne"
    ) > "$temporaire" || { rm -f "$temporaire"; return 1; }
  fi
  if ! { chmod 600 "$temporaire" && mv -f "$temporaire" "$fichier"; }; then
    rm -f "$temporaire"
    return 1
  fi
}

# Ajoute $2=$3 seulement si la clé $2 est absente. C'est ce qui rend une seconde
# installation et une mise à jour sûres : un mot de passe PostgreSQL régénéré face à
# un volume existant rendrait l'instance inaccessible (CLAUDE.md §10).
completer_env() {
  if ! grep -qE "^$2=" "$1" 2> /dev/null; then
    poser_env "$1" "$2" "$3"
  fi
}

# Les clés parmi $2… absentes du fichier .env $1, ou présentes mais vides, une par
# ligne. Vide pour une clé vaut absente : l'API refuserait de démarrer de même.
cles_manquantes() {
  local fichier="$1" cle
  shift
  for cle in "$@"; do
    [ -n "$(lire_env "$fichier" "$cle")" ] || printf '%s\n' "$cle"
  done
}

# ---- Réponses aux questions de l'installation ---------------------------------
#
# Chacune reçoit la saisie brute. Elle écrit la valeur retenue et réussit, ou écrit
# la raison du refus et échoue : `klean` repose alors la question en la montrant.
# Une saisie refusée ici est une saisie qui ne casserait rien plus loin — poser_env
# ne refuse plus rien, et l'API ne refuse pas de démarrer.

# Retire les espaces de début et de fin.
rogner() {
  local valeur="$1"
  valeur="${valeur#"${valeur%%[![:space:]]*}"}"
  valeur="${valeur%"${valeur##*[![:space:]]}"}"
  printf '%s\n' "$valeur"
}

# Le nom de domaine, tel qu'un patron le copie depuis son navigateur :
# « https://Klean.Exemple.fr/ » donne klean.exemple.fr.
normaliser_domaine() {
  local domaine
  domaine=$(rogner "$1")
  domaine="${domaine,,}"
  domaine="${domaine#http://}"
  domaine="${domaine#https://}"
  while [ "${domaine%/}" != "$domaine" ]; do domaine="${domaine%/}"; done
  if [[ "$domaine" =~ ^[a-z0-9]([a-z0-9.-]*[a-z0-9])?\.[a-z]{2,}$ ]]; then
    printf '%s\n' "$domaine"
  else
    printf '%s\n' "« $1 » n'est pas un nom de domaine (exemple : klean.mon-entreprise.fr)."
    return 1
  fi
}

# Une adresse de forme plausible, rien de plus : seul l'envoi d'un courriel prouve
# qu'elle existe. Ni apostrophe ni espace, que .env n'écrirait pas tels quels.
adresse_plausible() {
  local adresse
  adresse=$(rogner "$1")
  if [[ "$adresse" =~ ^[A-Za-z0-9._%+-]+@[A-Za-z0-9-]+(\.[A-Za-z0-9-]+)*\.[A-Za-z]{2,}$ ]]; then
    printf '%s\n' "$adresse"
  else
    printf '%s\n' "« $1 » n'est pas une adresse électronique (exemple : nom@mon-entreprise.fr)."
    return 1
  fi
}

# L'adresse d'expédition, obligatoire dès qu'un serveur est saisi : l'API refuse de
# démarrer sans elle (CLAUDE.md §2, « configuré à moitié »).
expediteur_smtp() {
  if [ -z "$(rogner "$1")" ]; then
    printf '%s\n' "Obligatoire avec un serveur de courriel. Sans elle, faites Ctrl-C et relancez en laissant le serveur vide : rien n'est encore écrit."
    return 1
  fi
  adresse_plausible "$1"
}

# Le serveur SMTP : vide pour s'en passer, sinon un nom ou une adresse IPv4.
hote_smtp() {
  local hote
  hote=$(rogner "$1")
  hote="${hote,,}"
  if [ -z "$hote" ] || [[ "$hote" =~ ^[a-z0-9]([a-z0-9.-]*[a-z0-9])?$ ]]; then
    printf '%s\n' "$hote"
  else
    printf '%s\n' "« $1 » n'est pas un nom de serveur (exemple : smtp.mon-fournisseur.fr)."
    return 1
  fi
}

# Un port de 1 à 65535, comme l'exige l'API au démarrage. 10# : « 0587 » n'est pas
# de l'octal.
port_smtp() {
  local port
  port=$(rogner "$1")
  if [[ "$port" =~ ^[0-9]{1,5}$ ]] && [ "$((10#$port))" -ge 1 ] && [ "$((10#$port))" -le 65535 ]; then
    printf '%s\n' "$((10#$port))"
  else
    printf '%s\n' "« $1 » n'est pas un port : un nombre, en général 587 ou 465."
    return 1
  fi
}

# L'identifiant SMTP, facultatif. Rogné : une espace finale copiée avec lui ferait
# échouer chaque envoi, sans rien dire au démarrage.
identifiant_smtp() {
  local identifiant
  identifiant=$(rogner "$1")
  case "$identifiant" in
    *"'"* | *[[:space:]]*)
      printf 'Un identifiant ne contient ni apostrophe ni espace.\n'
      return 1
      ;;
  esac
  printf '%s\n' "$identifiant"
}

# Le mot de passe SMTP, obligatoire avec un identifiant. Pas rogné : une espace peut
# en faire partie. L'apostrophe n'a pas d'écriture sûre dans .env (poser_env).
mot_de_passe_smtp() {
  case "$1" in
    '')
      printf '%s\n' "Obligatoire avec un identifiant : l'API refuserait de démarrer sans lui."
      return 1
      ;;
    *"'"* | *$'\n'* | *$'\r'*)
      printf '%s\n' "Ce mot de passe contient une apostrophe, que KLEAN ne sait pas enregistrer. Changez-le chez votre fournisseur de courriel, ou faites Ctrl-C et relancez en laissant le serveur vide : rien n'est encore écrit."
      return 1
      ;;
  esac
  printf '%s\n' "$1"
}

# oui, o, yes, y — en majuscules ou non — donnent « oui » ; non, n, no, « non ».
reponse_oui_non() {
  local reponse
  reponse=$(rogner "$1")
  case "${reponse,,}" in
    oui | o | yes | y) printf 'oui\n' ;;
    non | n | no) printf 'non\n' ;;
    *)
      printf 'Répondez oui ou non.\n'
      return 1
      ;;
  esac
}

# La version d'une URL de version GitHub : …/releases/tag/v1.2.3 donne 1.2.3. Échoue
# sur toute autre forme : la redirection de `releases/latest` sans version publiée
# mène à …/releases.
version_depuis_url() {
  local fin="${1##*/}"
  fin="${fin#v}"
  if [[ "$fin" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
    printf '%s\n' "$fin"
  else
    return 1
  fi
}

version_valide() {
  [[ "$1" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]
}

# Réussit si la version $1 précède strictement $2. Nombre par nombre, pas en texte :
# 0.10.0 vient après 0.9.0. 10# : « 08 » n'est pas de l'octal. Échoue sur une version
# mal formée, qui ne précède rien.
version_anterieure() {
  local a b i
  version_valide "$1" && version_valide "$2" || return 1
  IFS=. read -r -a a <<< "$1"
  IFS=. read -r -a b <<< "$2"
  for i in 0 1 2; do
    if ((10#${a[i]} < 10#${b[i]})); then return 0; fi
    if ((10#${a[i]} > 10#${b[i]})); then return 1; fi
  done
  return 1
}

# L'empreinte que porte le contenu $1 d'un fichier .sha256 publié par publier.sh, s'il
# décrit bien le fichier $2 : une seule ligne, 64 chiffres hexadécimaux, deux espaces,
# puis ce nom exactement. Sinon, rien, et un échec. Lu comme une donnée — jamais par
# `sha256sum -c`, qui vérifie le fichier que le .sha256 nomme, quel qu'il soit.
empreinte_publiee() {
  # `.` couvre aussi un saut de ligne : une seconde ligne tombe dans le nom, et le refus.
  [[ "$1" =~ ^([0-9a-f]{64})\ \ (.*)$ ]] || return 1
  [ "${BASH_REMATCH[2]}" = "$2" ] || return 1
  printf '%s\n' "${BASH_REMATCH[1]}"
}

# Les fichiers d'une version, d'après le contenu $1 de son manifeste FICHIERS (écrit par
# publier.sh) : un nom par ligne, VERSION en dernier. Rendus un par ligne, ou un échec
# si une seule ligne n'est pas un simple nom de fichier — ni `/`, ni `..`, ni nom
# commençant par un point : copiés dans /opt/klean par root, ils ne doivent rien
# atteindre d'autre, ni .env.
manifeste_valide() {
  local ligne derniere='' liste=''
  [ -n "$1" ] || return 1
  while IFS= read -r ligne; do
    [[ "$ligne" =~ ^[A-Za-z0-9_][A-Za-z0-9._-]*$ ]] || return 1
    [[ "$ligne" != *..* ]] || return 1
    liste+="$ligne"$'\n'
    derniere="$ligne"
  done <<< "$1"
  [ "$derniere" = VERSION ] || return 1
  printf '%s' "$liste"
}

url_archive() {
  printf 'https://github.com/%s/releases/download/v%s/klean-%s.tar.gz\n' "$KLEAN_DEPOT_GITHUB" "$1" "$1"
}

# Lue par sa redirection, sans l'API de GitHub : ni clé, ni plafond de débit.
url_derniere_version() {
  printf 'https://github.com/%s/releases/latest\n' "$KLEAN_DEPOT_GITHUB"
}

# Le préfixe des images. ghcr.io exige un propriétaire en minuscules.
nom_images() {
  printf 'ghcr.io/%s/klean\n' "$(printf '%s' "${KLEAN_DEPOT_GITHUB%%/*}" | tr '[:upper:]' '[:lower:]')"
}

# $1 : l'ID de /etc/os-release ; $2 : l'architecture selon dpkg. Les images ne sont
# publiées qu'en amd64 en V1 (SPEC.md, étape 34).
systeme_pris_en_charge() {
  case "$1" in debian | ubuntu) ;; *) return 1 ;; esac
  [ "$2" = amd64 ]
}

# $1 : MemTotal de /proc/meminfo, en kio. 1 Go, moins la part que le noyau se réserve.
memoire_suffisante() {
  [ "$1" -ge 900000 ]
}

# Vrai si la machine a l'IPv6. $1 : /proc/sys/net/ipv6/conf/all/disable_ipv6, qui doit
# exister et valoir 0 ; $2 : /proc/net/if_inet6, une ligne par adresse IPv6, qui ne doit
# pas être vide — sous disable_ipv6=1, il existe, mais vide. Sous ipv6.disable=1, au
# démarrage du noyau, aucun des deux n'existe.
#
# ⚠️ Lu, jamais mesuré : un fichier de /proc annonce une taille nulle quel que soit son
# contenu, et `[ -s ]` le dirait toujours vide.
ipv6_disponible() {
  [ "$(cat "$1" 2> /dev/null)" = 0 ] && grep -q . "$2" 2> /dev/null
}

# Vrai si $1 est une valeur de KLEAN_IPV6 que docker compose lit : true, false, ou vide —
# le compose retombe alors sur true. Tout autre mot, « oui » par exemple, fait échouer
# chacune de ses commandes sur « invalid boolean ».
reglage_ipv6_valide() {
  case "$1" in
    '' | true | false) return 0 ;;
    *) return 1 ;;
  esac
}

# $1 : les adresses du domaine, une par ligne ; $2 : celles de la machine. Écrit les
# adresses du domaine absentes de la machine. Réussit si toutes y sont ; échoue si une
# manque, ou si le domaine n'a aucune adresse.
#
# A et AAAA ensemble : Let's Encrypt préfère IPv6, et une AAAA oubliée vers une autre
# machine fait échouer le certificat sans rien dire.
adresses_etrangeres() {
  local domaine="$1" machine="$2" adresse etrangeres=0
  [ -n "$domaine" ] || return 1
  while IFS= read -r adresse; do
    [ -n "$adresse" ] || continue
    if ! printf '%s\n' "$machine" | grep -qxF "$adresse"; then
      printf '%s\n' "$adresse"
      etrangeres=1
    fi
  done <<< "$domaine"
  return "$etrangeres"
}

# Les sauvegardes à supprimer. $1 : les fichiers, un par ligne, nommés
# <préfixe>-AAAA-MM-JJTHHMM[…] ; $2 : la date limite AAAA-MM-JJ, strictement avant
# laquelle une sauvegarde part. Ne rend jamais la plus récente : la rotation ne
# supprime pas la dernière copie, même hors délai.
#
# Un nom qui ne suit pas ce motif n'est ni rendu ni compté : trié après les autres, il
# passerait pour la plus récente et laisserait partir la vraie. Une liste vide rend
# une sortie vide, sans échec — un grep sans résultat ferait quitter `klean`, sous
# pipefail.
sauvegardes_a_supprimer() {
  local fichiers dernier fichier nom date
  fichiers=$(printf '%s\n' "$1" | { grep -E '(^|/)[^/-]+-[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{4}[^/]*$' || true; } | sort)
  [ -n "$fichiers" ] || return 0
  dernier=$(printf '%s\n' "$fichiers" | tail -n 1)
  while IFS= read -r fichier; do
    [ "$fichier" != "$dernier" ] || continue
    nom="${fichier##*/}"
    date="${nom#*-}"
    date="${date:0:10}"
    if [[ "$date" < "$2" ]]; then
      printf '%s\n' "$fichier"
    fi
  done <<< "$fichiers"
}


# Vrai si $1 nomme un fuseau connu du système, Europe/Paris ou Indian/Reunion. `date`
# accepte n'importe quel TZ et compte alors en UTC sans rien dire : un fuseau mal écrit
# déplacerait la sauvegarde de la nuit en silence. Le motif écarte aussi « .. ».
fuseau_valide() {
  [[ "$1" =~ ^[A-Za-z][A-Za-z0-9_+-]*(/[A-Za-z0-9_+-]+)*$ ]] && [ -f "/usr/share/zoneinfo/$1" ]
}

# Vrai si l'instant $2 — secondes depuis 1970, maintenant par défaut — tombe dans
# l'heure de 3 h du fuseau $1. Appelée à chaque heure par la tâche de nuit, elle n'est
# vraie qu'une fois par jour, changement d'heure compris : à Paris, l'heure répétée ou
# sautée est celle de 2 h.
heure_de_la_sauvegarde() {
  local instant="${2:-$(date +%s)}"
  [ "$(TZ="$1" date -d "@$instant" +%H)" = 03 ]
}

# ---- Installation ---------------------------------------------------------------

# Ce que disent de l'installation le nombre de comptes $1 et, s'il est donné, le nombre
# d'entreprises $2, tels que psql les a écrits : « installee » dès qu'un compte existe ;
# sans compte, « orpheline » si une entreprise existe — le reste d'un amorçage d'avant le
# lot A2, que l'assistant ne reprend pas —, sinon « a-faire ». Une sortie illisible —
# vide, PostgreSQL muet, un message d'erreur — n'est ni l'une ni l'autre : un échec, et
# jamais « aucun compte », qui ferait tirer ou montrer un code sur une instance installée.
etat_installation() {
  [[ "$1" =~ ^[0-9]+$ ]] || return 1
  if [ "$1" -gt 0 ]; then
    printf 'installee\n'
    return
  fi
  if [ "$#" -ge 2 ]; then
    [[ "$2" =~ ^[0-9]+$ ]] || return 1
    if [ "$2" -gt 0 ]; then
      printf 'orpheline\n'
      return
    fi
  fi
  printf 'a-faire\n'
}

# Les clés du fichier .env $1 qu'un compte existant rend inutiles, d'après le nombre de
# comptes $2 : le code de l'assistant, et les restes de l'amorçage d'une version 0.1.x.
# Seulement celles qui portent une valeur, une par ligne ; rien sans compte, et un échec
# sur un nombre illisible (etat_installation).
#
# ⚠️ Un mot de passe provisoire encore là n'a pas été montré : une installation 0.1.x
# inachevée, que `klean installer` termine par montrer_puis_retirer_mot_de_passe. Le
# retirer, ou l'adresse qu'il ouvre, perdrait le seul accès au compte. Il reste, avec elle
# et la raison sociale : ils partiront une fois montré.
cles_d_installation_a_retirer() {
  local fichier="$1" etat cle
  etat=$(etat_installation "$2") || return 1
  [ "$etat" = installee ] || return 0
  [ -z "$(lire_env "$fichier" KLEAN_CODE_INSTALLATION)" ] || printf 'KLEAN_CODE_INSTALLATION\n'
  [ -z "$(lire_env "$fichier" KLEAN_ADMIN_MOT_DE_PASSE)" ] || return 0
  for cle in KLEAN_ADMIN_EMAIL KLEAN_RAISON_SOCIALE; do
    [ -z "$(lire_env "$fichier" "$cle")" ] || printf '%s\n' "$cle"
  done
}
