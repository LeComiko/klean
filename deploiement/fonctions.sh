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

# Un octet aléatoire, de 0 à 255. Échoue plutôt que de rendre une chaîne vide : les
# boucles de rejet ci-dessous tourneraient alors sans fin.
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

# Un secret hexadécimal de $1 octets. Hexadécimal : un « ; » ou un « = » couperait la
# chaîne de connexion de PostgreSQL (.env.example).
generer_secret() {
  od -An -N"$1" -tx1 /dev/urandom | tr -d ' \n'
  printf '\n'
}

# Un mot de passe provisoire qui passe les règles d'Identity (CLAUDE.md §6) : 16
# caractères, une majuscule, une minuscule et un chiffre au moins.
generer_mot_de_passe() {
  local alphabet='ABCDEFGHJKMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz23456789'
  local mot octet
  while :; do
    mot=''
    while [ "${#mot}" -lt 16 ]; do
      octet=$(octet_aleatoire) || return 1
      # 216 = 4 × 54, pour la même raison que dans generer_code.
      if [ "$octet" -lt 216 ]; then
        mot+="${alphabet:$((octet % 54)):1}"
      fi
    done
    if [[ "$mot" =~ [A-Z] && "$mot" =~ [a-z] && "$mot" =~ [0-9] ]]; then
      printf '%s\n' "$mot"
      return
    fi
  done
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

