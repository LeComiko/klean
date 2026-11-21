#!/usr/bin/env bash
# Série des fonctions du déploiement (SPEC.md, étape 34, lot A1). Bash et coreutils
# seulement. Lancée par deploiement/tests/lancer.sh, et par le CI.
# shellcheck disable=SC2329,SC2016,SC1112  # cas passés à `verifier` ; bash -c en apostrophes voulu ; ’ typographique dans des libellés
set -uo pipefail

ici=$(cd "$(dirname "$0")" && pwd)
# shellcheck source-path=SCRIPTDIR source=../fonctions.sh
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
    bash -c '[[ "$1" =~ ^[A-HJKMNP-Za-hjkmnp-z2-9]{16}$ && "$1" =~ [A-Z] && "$1" =~ [a-z] && "$1" =~ [0-9] ]]' _ "$(generer_mot_de_passe)"
done

# Dix tirages ne prouvent pas la règle : un mot sans chiffre sort environ une fois sur
# quatorze, donc la boucle de rejet retirée resterait verte une fois sur deux. Ici
# l'aléa est imposé — seize « a », puis A, a, 2 en boucle —, et le premier mot doit
# être rejeté. Le compteur vit dans un fichier : chaque appel est un sous-shell.
mot_de_passe_sans_chiffre_rejete() (
  compteur=$(mktemp)
  echo 0 >"$compteur"
  octet_aleatoire() {
    local n
    n=$(cat "$compteur")
    echo $((n + 1)) >"$compteur"
    if [ "$n" -lt 16 ]; then echo 23; else echo $(((n % 3) * 23 + (n % 3 == 2 ? 1 : 0))); fi
  }
  mot=$(generer_mot_de_passe)
  rm -f "$compteur"
  [ "$mot" != aaaaaaaaaaaaaaaa ] && [[ "$mot" =~ [A-Z] && "$mot" =~ [a-z] && "$mot" =~ [0-9] ]]
)
verifier 'un mot de passe sans chiffre est tiré de nouveau' mot_de_passe_sans_chiffre_rejete

# ---- Bilan -------------------------------------------------------------------

printf '%s réussis, %s échoués\n' "$reussis" "$echoues"
[ "$echoues" -eq 0 ]
