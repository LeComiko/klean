#!/usr/bin/env bash
# Série des fonctions du déploiement (SPEC.md, étape 34, lot A1). Bash et coreutils
# seulement. Lancée par deploiement/tests/lancer.sh, et par le CI.
# shellcheck disable=SC2329,SC2016,SC1112  # cas passés à `verifier` ; bash -c en apostrophes voulu ; ’ typographique dans des libellés
set -uo pipefail

ici=$(cd "$(dirname "$0")" && pwd)
# shellcheck source=../fonctions.sh
. "$ici/../fonctions.sh"

reussis=0
echoues=0

# verifier « libellé » commande… : compte un succès ou un échec, sans s'arrêter.
verifier() {
  local libelle="$1"
  shift
  if "$@"; then
    reussis=$((reussis + 1))
  else
    echoues=$((echoues + 1))
    printf 'ÉCHEC  %s\n' "$libelle"
  fi
}

egal() {
  [ "$1" = "$2" ] || { printf '       attendu « %s », obtenu « %s »\n' "$2" "$1"; return 1; }
}

# ---- Secrets -----------------------------------------------------------------

code=$(generer_code)
verifier 'le code fait 20 symboles de l’alphabet sans ambiguïté' \
  bash -c '[[ "$1" =~ ^[ABCDEFGHJKMNPQRSTUVWXYZ23456789]{20}$ ]]' _ "$code"
verifier 'deux codes diffèrent' bash -c '[ "$1" != "$2" ]' _ "$code" "$(generer_code)"

verifier 'un secret de 32 octets fait 64 chiffres hexadécimaux' \
  bash -c '[[ "$1" =~ ^[0-9a-f]{64}$ ]]' _ "$(generer_secret 32)"

for _ in 1 2 3 4 5 6 7 8 9 10; do
  verifier 'le mot de passe provisoire passe les règles d’Identity' \
    bash -c '[[ "$1" =~ ^[A-Za-z0-9]{16}$ && "$1" =~ [A-Z] && "$1" =~ [a-z] && "$1" =~ [0-9] ]]' _ "$(generer_mot_de_passe)"
done

# ---- Bilan -------------------------------------------------------------------

printf '%s réussis, %s échoués\n' "$reussis" "$echoues"
[ "$echoues" -eq 0 ]
