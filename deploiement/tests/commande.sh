#!/usr/bin/env bash
# Série de la commande `klean` (SPEC.md, étape 34, lot A2) : ses gestes autour du code
# d'installation, joués sans Docker ni PostgreSQL. `klean` est sourcé — `principal` n'y fait
# qu'afficher l'aide —, puis compose, psql et la sonde HTTPS sont remplacés par des doublures
# qui notent ce qu'on leur demande. Linux seulement : le verrou se lit dans /proc. Lancée par
# deploiement/tests/lancer.sh, après tests.sh, et par le CI.
# shellcheck disable=SC2329,SC2034,SC1112  # doublures appelées par klean ; réglages lus par klean ; ’ typographique dans des libellés
set -uo pipefail

ici=$(cd "$(dirname "$0")" && pwd)
R=$(mktemp -d)
trap 'rm -rf "$R"' EXIT
cp "$ici/../fonctions.sh" "$R/"
export KLEAN_REPERTOIRE="$R" KLEAN_VERROU="$R/verrou" KLEAN_SAUVEGARDES="$R/sauvegardes" \
  KLEAN_TROUSSEAU="$R/trousseau"
# shellcheck source-path=SCRIPTDIR source=../klean
. "$ici/../klean" aide > /dev/null
# klean pose -e : une série compte ses échecs au lieu de s'arrêter au premier.
set +e

reussis=0
echoues=0

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
egal() { [ "$1" = "$2" ] || { printf '       attendu « %s », obtenu « %s »\n' "$2" "$1"; return 1; }; }
contient() { [[ $1 == *"$2"* ]] || { printf '       « %s » manque dans : %s\n' "$2" "$1"; return 1; }; }
sans() { [[ $1 != *"$2"* ]] || { printf '       « %s » ne devrait pas paraître dans : %s\n' "$2" "$1"; return 1; }; }
valeur() { lire_env "$ENV" "$1"; }
journal() { cat "$R/journal" 2> /dev/null; }
recree_l_api() { contient "$(journal)" 'compose up -d --no-deps api'; }
aucun_demarrage() { sans "$(journal)" 'compose up'; }
statut() { egal "$1" "$2"; }
refus() { [ "$1" -ne 0 ] || { printf '       réussite inattendue\n'; return 1; }; }
code_valide() { [[ $1 =~ ^[$ALPHABET_CODE]{20}$ ]] || { printf '       code invalide : « %s »\n' "$1"; return 1; }; }

# ---- Doublures -----------------------------------------------------------------

COMPTES=0
ENTREPRISES=0
SONDE=200
EN_MARCHE=$'postgres\napi\ncaddy'
# Ce que l'environnement du conteneur de l'API porte encore : `printenv` l'écrit, vide ou non ;
# « absente » le fait échouer, comme une variable que le compose ne pose pas.
CODE_API=''
nombre_de_comptes() { [ "$COMPTES" != muet ] || return 1; printf '%s\n' "$COMPTES"; }
nombre_d_entreprises() { printf '%s\n' "$ENTREPRISES"; }
compose() {
  printf 'compose %s\n' "$*" >> "$R/journal"
  case "$*" in
    *'ps --status running --services'*) printf '%s\n' "$EN_MARCHE" ;;
    *'exec -T api printenv Installation__Code'*)
      [ "$CODE_API" != absente ] || return 1
      printf '%s\n' "$CODE_API"
      ;;
  esac
}
sonde_https() { printf 'sonde\n' >> "$R/journal"; printf '%s\n' "$SONDE"; }
verifier_https() { printf 'attente\n' >> "$R/journal"; [ "$SONDE" = 200 ]; }
exiger_root() { :; }
# Aucun volume : reprendre_env ne refuse pas une base orpheline.
volume_postgres_existe() { return 1; }

# neuf « contenu de .env » : un .env neuf, un journal vide, aucun verrou tenu.
neuf() {
  : > "$R/journal"
  printf '%b' "$1" > "$ENV"
  KLEAN_VERROUILLE=''
  exec 9>&-
  CODE_TIRE=''
  ASSISTANT_OUVERT=''
  ENTREPRISE_ORPHELINE=''
  COMPTES=0
  ENTREPRISES=0
  SONDE=200
  EN_MARCHE=$'postgres\napi\ncaddy'
  CODE_API=''
}

CODE=$(generer_code)
LISIBLE=$(code_lisible "$CODE")
BASE="KLEAN_DOMAINE=k.exemple.fr\nKLEAN_VERSION=0.2.0\n"

# ---- Montrer le code -----------------------------------------------------------

neuf "${BASE}KLEAN_CODE_INSTALLATION=$CODE\n"
sortie=$(montrer_code_installation 2>&1)
s=$?
verifier 'une installation neuve montre le code' statut "$s" 0
verifier 'le code se montre par groupes' contient "$sortie" "$LISIBLE"
verifier 'le code ne se montre jamais d’un seul tenant' sans "$sortie" "$CODE"
verifier 'l’adresse se montre sans le code' contient "$sortie" 'https://k.exemple.fr —'
verifier 'c’est la dernière étape' contient "$sortie" 'Dernière étape'

neuf "${BASE}KLEAN_CODE_INSTALLATION=$CODE\n"
ENTREPRISES=1
sortie=$(montrer_code_installation 2>&1)
verifier 'une entreprise orpheline ne montre pas le code' sans "$sortie" "$LISIBLE"
verifier 'elle renvoie à DEPLOIEMENT.md' contient "$sortie" 'Une entreprise sans compte'

neuf "${BASE}KLEAN_CODE_INSTALLATION=$CODE\n"
COMPTES=2
sortie=$(montrer_code_installation 2>&1)
verifier 'une instance installée ne montre rien' egal "$sortie" ''

# ---- Retirer le code -----------------------------------------------------------

neuf "${BASE}KLEAN_CODE_INSTALLATION=$CODE\nKLEAN_ADMIN_EMAIL=p@exemple.fr\nKLEAN_ADMIN_MOT_DE_PASSE=\nKLEAN_RAISON_SOCIALE=Propre\nKLEAN_SIRET=12345678901234\n"
COMPTES=1
sortie=$(retirer_code_si_installe 2>&1)
verifier 'des comptes : le code est vidé' egal "$(valeur KLEAN_CODE_INSTALLATION)" ''
verifier 'l’adresse de l’amorçage est vidée' egal "$(valeur KLEAN_ADMIN_EMAIL)" ''
verifier 'la raison sociale est vidée' egal "$(valeur KLEAN_RAISON_SOCIALE)" ''
verifier 'le SIRET reste' egal "$(valeur KLEAN_SIRET)" 12345678901234
verifier 'le domaine reste' egal "$(valeur KLEAN_DOMAINE)" k.exemple.fr
verifier 'l’API, qui tourne, est recréée seule' recree_l_api
verifier 'une seule fois' egal "$(journal | grep -c 'compose up')" 1
verifier 'le message ne porte pas le code' sans "$sortie" "$CODE"
verifier '.env reste en 600' egal "$(stat -c %a "$ENV")" 600

neuf "${BASE}KLEAN_CODE_INSTALLATION=\n"
COMPTES=1
sortie=$(retirer_code_si_installe 2>&1)
verifier 'rien à retirer : silence' egal "$sortie" ''
verifier 'rien à retirer : l’API n’est pas recréée' aucun_demarrage

neuf "${BASE}KLEAN_CODE_INSTALLATION=$CODE\n"
COMPTES=muet
sortie=$(retirer_code_si_installe 2>&1)
verifier 'PostgreSQL muet : le code reste' egal "$(valeur KLEAN_CODE_INSTALLATION)" "$CODE"
verifier 'PostgreSQL muet : silence' egal "$sortie" ''

# Le verrou tenu par une autre ouverture du fichier — flock les distingue, même dans un seul
# processus : sans attente ni course.
neuf "${BASE}KLEAN_CODE_INSTALLATION=$CODE\n"
COMPTES=1
exec 8> "$KLEAN_VERROU"
flock -n 8
sortie=$(retirer_code_si_installe 2>&1)
exec 8>&-
verifier 'verrou tenu ailleurs : le code reste' egal "$(valeur KLEAN_CODE_INSTALLATION)" "$CODE"
verifier 'verrou tenu ailleurs : silence' egal "$sortie" ''

neuf "${BASE}KLEAN_CODE_INSTALLATION=$CODE\nKLEAN_ADMIN_EMAIL=p@exemple.fr\nKLEAN_ADMIN_MOT_DE_PASSE=Provisoire\n"
COMPTES=1
retirer_code_si_installe > /dev/null 2>&1
verifier 'un mot de passe provisoire pas encore montré reste' egal "$(valeur KLEAN_ADMIN_MOT_DE_PASSE)" Provisoire
verifier 'avec l’adresse qu’il ouvre' egal "$(valeur KLEAN_ADMIN_EMAIL)" p@exemple.fr
verifier 'mais le code part' egal "$(valeur KLEAN_CODE_INSTALLATION)" ''

# ---- Une commande d'état ne défait pas un arrêt voulu ---------------------------

for etat_inacheve in KLEAN_MAJ_EN_COURS KLEAN_RETOUR_EN_COURS; do
  neuf "${BASE}KLEAN_CODE_INSTALLATION=$CODE\n$etat_inacheve=0.2.0\n"
  COMPTES=1
  cmd_etat > /dev/null 2>&1
  verifier "$etat_inacheve posé, etat : le code est vidé" egal "$(valeur KLEAN_CODE_INSTALLATION)" ''
  verifier "$etat_inacheve posé, etat : l’API n’est pas démarrée" aucun_demarrage
done

neuf "${BASE}KLEAN_CODE_INSTALLATION=$CODE\n"
COMPTES=1
EN_MARCHE=$'postgres\ncaddy'
cmd_etat > /dev/null 2>&1
verifier 'API arrêtée, etat : le code est vidé' egal "$(valeur KLEAN_CODE_INSTALLATION)" ''
verifier 'API arrêtée, etat : elle n’est pas démarrée' aucun_demarrage

neuf "${BASE}KLEAN_CODE_INSTALLATION=$CODE\n"
COMPTES=1
sortie=$(cmd_etat 2>&1)
verifier 'etat : l’API qui tourne est recréée sans le code' recree_l_api
verifier 'etat ne montre jamais le code' sans "$sortie" "$CODE"

# ---- Le conteneur relancé avec l'ancien code -----------------------------------
#
# .env n'a plus de code, mais un `docker compose start`, un `restart` ou un redémarrage de la
# machine ont relancé le conteneur d'avant le retrait, qui le garde dans son environnement.

neuf "${BASE}KLEAN_CODE_INSTALLATION=\n"
COMPTES=1
CODE_API="$CODE"
sortie=$(retirer_code_si_installe 2>&1)
verifier 'l’API relancée avec l’ancien code est recréée' recree_l_api
verifier 'une seule fois, rattrapage compris' egal "$(journal | grep -c 'compose up')" 1
verifier 'le rattrapage ne montre jamais le code' sans "$sortie" "$CODE"
verifier 'ni par groupes' sans "$sortie" "$LISIBLE"
verifier 'le rattrapage se dit' contient "$sortie" 'recréée sans lui'

neuf "${BASE}KLEAN_CODE_INSTALLATION=\n"
COMPTES=1
CODE_API="$CODE"
sortie=$(cmd_etat 2>&1)
verifier 'etat rattrape l’API relancée avec l’ancien code' recree_l_api
verifier 'etat ne montre jamais le code rattrapé' sans "$sortie" "$CODE"

neuf "${BASE}KLEAN_CODE_INSTALLATION=\n"
COMPTES=1
CODE_API=absente
retirer_code_si_installe > /dev/null 2>&1
verifier 'une API qui n’a pas la variable n’est pas recréée' aucun_demarrage

neuf "${BASE}KLEAN_CODE_INSTALLATION=\n"
COMPTES=0
CODE_API="$CODE"
retirer_code_si_installe > /dev/null 2>&1
verifier 'sans compte, l’API garde son code' aucun_demarrage

neuf "${BASE}KLEAN_CODE_INSTALLATION=\n"
COMPTES=muet
CODE_API="$CODE"
retirer_code_si_installe > /dev/null 2>&1
verifier 'PostgreSQL muet : rien n’est rattrapé' aucun_demarrage

neuf "${BASE}KLEAN_CODE_INSTALLATION=\n"
COMPTES=1
CODE_API="$CODE"
EN_MARCHE=$'postgres\ncaddy'
retirer_code_si_installe > /dev/null 2>&1
verifier 'une API arrêtée n’est pas rattrapée' aucun_demarrage
verifier 'ni interrogée' sans "$(journal)" printenv

for etat_inacheve in KLEAN_MAJ_EN_COURS KLEAN_RETOUR_EN_COURS; do
  neuf "${BASE}KLEAN_CODE_INSTALLATION=\n$etat_inacheve=0.2.0\n"
  COMPTES=1
  CODE_API="$CODE"
  retirer_code_si_installe > /dev/null 2>&1
  verifier "$etat_inacheve posé : rien n’est rattrapé" aucun_demarrage
done

neuf "${BASE}KLEAN_CODE_INSTALLATION=\n"
COMPTES=1
CODE_API="$CODE"
exec 8> "$KLEAN_VERROU"
flock -n 8
retirer_code_si_installe > /dev/null 2>&1
exec 8>&-
verifier 'verrou tenu ailleurs : rien n’est rattrapé' aucun_demarrage

# ---- Le mot de passe provisoire d'une 0.1.x inachevée --------------------------

neuf "${BASE}KLEAN_CODE_INSTALLATION=$CODE\nKLEAN_ADMIN_EMAIL=p@exemple.fr\nKLEAN_ADMIN_MOT_DE_PASSE=Provisoire\nKLEAN_RAISON_SOCIALE=Propre\n"
COMPTES=1
# La question « tapez NOTE » répondue dans le sous-processus seul : la doublure ne survit pas.
sortie=$({
  demander() { REPONSE=NOTE; }
  montrer_puis_retirer_mot_de_passe && retirer_code_si_installe
} 2>&1)
verifier 'le mot de passe provisoire est montré' contient "$sortie" Provisoire
verifier 'puis retiré' egal "$(valeur KLEAN_ADMIN_MOT_DE_PASSE)" ''
verifier 'avec l’adresse de l’amorçage' egal "$(valeur KLEAN_ADMIN_EMAIL)" ''
verifier 'la raison sociale' egal "$(valeur KLEAN_RAISON_SOCIALE)" ''
verifier 'et le code' egal "$(valeur KLEAN_CODE_INSTALLATION)" ''
verifier 'l’API n’est recréée qu’une fois' egal "$(journal | grep -c 'compose up')" 1

# ---- Assurer un code -----------------------------------------------------------

neuf "$BASE"
assurer_code_installation api x > /dev/null
verifier 'aucun compte, aucun code : un code est tiré' code_valide "$(valeur KLEAN_CODE_INSTALLATION)"
verifier 'et noté tiré' egal "$CODE_TIRE" 1
verifier 'l’assistant est ouvert' egal "$ASSISTANT_OUVERT" 1
verifier 'l’API est recréée sur demande' contient "$(journal)" 'compose up -d api'

neuf "$BASE"
assurer_code_installation '' x > /dev/null
verifier 'sans demande, l’API n’est pas recréée' aucun_demarrage

neuf "${BASE}KLEAN_CODE_INSTALLATION=$CODE\n"
assurer_code_installation api x > /dev/null
verifier 'un code présent n’est jamais remplacé' egal "$(valeur KLEAN_CODE_INSTALLATION)" "$CODE"
verifier 'ni noté tiré' egal "$CODE_TIRE" ''

neuf "$BASE"
ENTREPRISES=1
assurer_code_installation '' x > /dev/null
verifier 'une entreprise orpheline reçoit un code, sans quoi l’API ne démarre pas' \
  code_valide "$(valeur KLEAN_CODE_INSTALLATION)"
verifier 'elle est notée orpheline' egal "$ENTREPRISE_ORPHELINE" 1
verifier 'et l’assistant n’est pas dit ouvert' egal "$ASSISTANT_OUVERT" ''

neuf "$BASE"
COMPTES=1
assurer_code_installation api x > /dev/null
verifier 'des comptes : aucun code tiré' egal "$(valeur KLEAN_CODE_INSTALLATION)" ''

neuf "$BASE"
COMPTES=muet
sortie=$(assurer_code_installation api 'Relancez.' 2>&1)
s=$?
verifier 'PostgreSQL muet : assurer échoue' refus "$s"
verifier 'PostgreSQL muet : aucun code écrit' egal "$(valeur KLEAN_CODE_INSTALLATION)" ''

# ---- klean code ----------------------------------------------------------------

neuf "$BASE"
sortie=$(cmd_code 2>&1)
s=$?
verifier 'code sans code : réussit' statut "$s" 0
verifier 'code sans code : en tire un, puis attend l’HTTPS' contient "$(journal)" attente
verifier 'puis le montre' contient "$sortie" 'Il demande ce code'

neuf "${BASE}KLEAN_CODE_INSTALLATION=$CODE\n"
sortie=$(cmd_code 2>&1)
verifier 'code avec code : un seul essai' contient "$(journal)" sonde
verifier 'sans attente' sans "$(journal)" attente
verifier 'puis le code' contient "$sortie" "$LISIBLE"

neuf "${BASE}KLEAN_CODE_INSTALLATION=$CODE\n"
SONDE=502
sortie=$(cmd_code 2>&1)
s=$?
verifier 'code sans HTTPS : refus' refus "$s"
verifier 'code sans HTTPS : pas de code' sans "$sortie" "$LISIBLE"
verifier 'code sans HTTPS : la reprise est nommée' contient "$sortie" 'relancez : sudo klean code'

neuf "${BASE}KLEAN_CODE_INSTALLATION=$CODE\n"
COMPTES=3
sortie=$(cmd_code 2>&1)
verifier 'code sur une instance installée : déjà installé' contient "$sortie" 'déjà installé'
verifier 'et le code est retiré' egal "$(valeur KLEAN_CODE_INSTALLATION)" ''

neuf "${BASE}KLEAN_CODE_INSTALLATION=$CODE\n"
COMPTES=muet
sortie=$(cmd_code 2>&1)
s=$?
verifier 'code, PostgreSQL muet : refus' refus "$s"
verifier 'code, PostgreSQL muet : pas de code' sans "$sortie" "$LISIBLE"

for etat_inacheve in KLEAN_MAJ_EN_COURS KLEAN_RETOUR_EN_COURS; do
  neuf "${BASE}KLEAN_CODE_INSTALLATION=$CODE\n$etat_inacheve=0.2.0\n"
  sortie=$(cmd_code 2>&1)
  s=$?
  verifier "code, $etat_inacheve posé : refus" refus "$s"
  verifier "code, $etat_inacheve posé : pas de code" sans "$sortie" "$LISIBLE"
  verifier "code, $etat_inacheve posé : rien n’est démarré" aucun_demarrage
  verifier "code, $etat_inacheve posé : la sortie est nommée" contient "$sortie" 'sudo klean revenir'
done

neuf "${BASE}KLEAN_CODE_INSTALLATION=$CODE\n"
ENTREPRISES=1
sortie=$(cmd_code 2>&1)
verifier 'code sur une entreprise orpheline : pas de code' sans "$sortie" "$LISIBLE"
verifier 'code sur une entreprise orpheline : DEPLOIEMENT.md' contient "$sortie" 'Une entreprise sans compte'

# ---- KLEAN_IPV6 ----------------------------------------------------------------
#
# docker compose refuse toutes ses commandes sur un enable_ipv6 qui n'est ni true ni false :
# klean le dit avant, et nomme la correction.

neuf "${BASE}KLEAN_IPV6=oui\n"
sortie=$(principal etat 2>&1)
s=$?
verifier 'KLEAN_IPV6=oui : refus' refus "$s"
verifier 'le refus nomme la variable fautive' contient "$sortie" '« oui »'
verifier 'et la correction' contient "$sortie" 'KLEAN_IPV6=false'
verifier 'rien n’est demandé à compose' egal "$(journal)" ''

for reglage in true false ''; do
  neuf "${BASE}KLEAN_IPV6=$reglage\n"
  (principal etat) > /dev/null 2>&1
  verifier "KLEAN_IPV6=« $reglage » : etat interroge compose" contient "$(journal)" 'compose ps'
done

neuf "${BASE}KLEAN_IPV6=oui\n"
sortie=$(principal aide 2>&1)
s=$?
verifier 'l’aide se montre même avec un réglage faux' statut "$s" 0
verifier 'sans parler du réglage' sans "$sortie" KLEAN_IPV6

# La détection lit deux fichiers de /proc, remplacés ici.
IPV6_DESACTIVE="$R/disable_ipv6"
IPV6_INTERFACES="$R/if_inet6"
avec_ipv6() { printf '0\n' > "$IPV6_DESACTIVE"; printf '00000000000000000000000000000001 01 80 10 80 lo\n' > "$IPV6_INTERFACES"; }
sans_ipv6() { printf '1\n' > "$IPV6_DESACTIVE"; : > "$IPV6_INTERFACES"; }
COMPLET="${BASE}POSTGRES_PASSWORD=x\nKLEAN_JWT_CLE=y\nKLEAN_IMAGES=ghcr.io/essai/klean\n"

neuf "$COMPLET"
avec_ipv6
reprendre_env > /dev/null 2>&1
verifier 'reprise, machine en IPv6 : KLEAN_IPV6=true' egal "$(valeur KLEAN_IPV6)" true

neuf "$COMPLET"
sans_ipv6
reprendre_env > /dev/null 2>&1
verifier 'reprise, machine sans IPv6 : KLEAN_IPV6=false' egal "$(valeur KLEAN_IPV6)" false

neuf "${COMPLET}KLEAN_IPV6=true\n"
sans_ipv6
reprendre_env > /dev/null 2>&1
verifier 'reprise : un KLEAN_IPV6 écrit ne change pas' egal "$(valeur KLEAN_IPV6)" true

# Une installation neuve : les questions répondues dans le sous-processus seul.
printf '0.2.1\n' > "$R/VERSION"
for machine in avec_ipv6 sans_ipv6; do
  neuf ''
  rm -f "$ENV"
  "$machine"
  ( demander_valide() { REPONSE=k.exemple.fr; }
    verifier_dns() { :; }
    demander_smtp() { SMTP_ENV=(); }
    preparer_env ) > /dev/null 2>&1
  attendu=true
  [ "$machine" = avec_ipv6 ] || attendu=false
  verifier "installation neuve, $machine : KLEAN_IPV6=$attendu" egal "$(valeur KLEAN_IPV6)" "$attendu"
  verifier "installation neuve, $machine : le domaine est écrit avec" egal "$(valeur KLEAN_DOMAINE)" k.exemple.fr
done

# ---- Bilan ---------------------------------------------------------------------

printf '%s réussis, %s échoués\n' "$reussis" "$echoues"
[ "$echoues" -eq 0 ]
