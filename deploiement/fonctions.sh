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
# lit alors la valeur littéralement, sans interpréter `$`. Une apostrophe dans la
# valeur n'a pas d'écriture sûre : refusée.
#
# Le fichier est réécrit à côté puis renommé : jamais de .env à moitié écrit.
poser_env() {
  local fichier="$1" cle="$2" valeur="$3" ligne temporaire
  case "$valeur" in
    *"'"*)
      printf 'Valeur refusée pour %s : une apostrophe ne peut pas figurer dans .env.\n' "$cle" >&2
      return 1
      ;;
  esac
  if [[ "$valeur" =~ ^[A-Za-z0-9._@:/+-]*$ ]]; then
    ligne="$cle=$valeur"
  else
    ligne="$cle='$valeur'"
  fi
  temporaire=$(mktemp "$fichier.XXXXXX")
  if grep -qE "^$cle=" "$fichier" 2> /dev/null; then
    # ENVIRON plutôt que `awk -v` : -v interprète les barres obliques inverses.
    LIGNE="$ligne" CLE="$cle" awk 'index($0, ENVIRON["CLE"] "=") == 1 { print ENVIRON["LIGNE"]; next } { print }' \
      "$fichier" > "$temporaire"
  else
    { cat "$fichier" 2> /dev/null || true; printf '%s\n' "$ligne"; } > "$temporaire"
  fi
  chmod 600 "$temporaire"
  mv -f "$temporaire" "$fichier"
}

# Ajoute $2=$3 seulement si la clé $2 est absente. C'est ce qui rend une seconde
# installation et une mise à jour sûres : un mot de passe PostgreSQL régénéré face à
# un volume existant rendrait l'instance inaccessible (CLAUDE.md §10).
completer_env() {
  if ! grep -qE "^$2=" "$1" 2> /dev/null; then
    poser_env "$1" "$2" "$3"
  fi
}
