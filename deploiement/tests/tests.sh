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

# ---- .env --------------------------------------------------------------------

dossier=$(mktemp -d)
env="$dossier/.env"
: > "$env"

poser_env "$env" KLEAN_DOMAINE klean.exemple.fr
verifier 'une clé absente est ajoutée' egal "$(lire_env "$env" KLEAN_DOMAINE)" klean.exemple.fr

poser_env "$env" KLEAN_DOMAINE autre.exemple.fr
verifier 'une clé présente est remplacée' egal "$(lire_env "$env" KLEAN_DOMAINE)" autre.exemple.fr
verifier 'une seule ligne par clé' egal "$(grep -c '^KLEAN_DOMAINE=' "$env")" 1

poser_env "$env" KLEAN_SMTP_MOT_DE_PASSE 'a$b c=d\e'
verifier 'une valeur spéciale est relue telle quelle' egal "$(lire_env "$env" KLEAN_SMTP_MOT_DE_PASSE)" 'a$b c=d\e'
verifier 'une valeur spéciale est écrite entre apostrophes' \
  grep -qx "KLEAN_SMTP_MOT_DE_PASSE='a\$b c=d\\\\e'" "$env"

# Une fonction locale et non `bash -c` : un bash neuf ne connaîtrait pas poser_env.
refuse_apostrophe() { ! poser_env "$env" X "l'a" 2> /dev/null; }
verifier 'une apostrophe est refusée' refuse_apostrophe
refuse_saut_de_ligne() { ! poser_env "$env" X $'un\nINJECTEE=1' 2> /dev/null; }
verifier 'un saut de ligne est refusé' refuse_saut_de_ligne
refuse_cle() { ! poser_env "$env" 'KLEAN.*' x 2> /dev/null; }
verifier 'une clé qui n’est pas un nom de variable est refusée' refuse_cle

# Le remplacement passe par awk, l'ajout par cat : la valeur spéciale, deux fois.
poser_env "$env" KLEAN_SMTP_MOT_DE_PASSE 'f\g$h i'
verifier 'une valeur spéciale remplacée est relue telle quelle' \
  egal "$(lire_env "$env" KLEAN_SMTP_MOT_DE_PASSE)" 'f\g$h i'

sans_fin="$dossier/sans-fin.env"
printf 'B=deux' > "$sans_fin"
poser_env "$sans_fin" C 'x y'
verifier 'un .env sans saut de ligne final garde sa dernière valeur' \
  egal "$(lire_env "$sans_fin" B)" deux
verifier 'et reçoit la nouvelle clé sur sa propre ligne' egal "$(lire_env "$sans_fin" C)" 'x y'

completer_env "$env" KLEAN_DOMAINE ecrase.exemple.fr
verifier 'completer_env n’écrase jamais' egal "$(lire_env "$env" KLEAN_DOMAINE)" autre.exemple.fr
completer_env "$env" KLEAN_GEOCODAGE false
verifier 'completer_env ajoute ce qui manque' egal "$(lire_env "$env" KLEAN_GEOCODAGE)" false

verifier '.env reste en 600' egal "$(stat -c %a "$env")" 600
verifier 'une clé absente se lit vide' egal "$(lire_env "$env" ABSENTE)" ''

rm -rf "$dossier"

# ---- Bilan -------------------------------------------------------------------

printf '%s réussis, %s échoués\n' "$reussis" "$echoues"
[ "$echoues" -eq 0 ]
