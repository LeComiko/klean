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

# Un contrat entre le script et l'API : un code que l'API refuse l'empêche de démarrer,
# comptes ou non (CodeInstallation.Lire). Lus dans le code de l'API, jamais recopiés.
code_api="$ici/../../backend/Services/CodeInstallation.cs"
alphabet_api=$(sed -n 's/.*const string Alphabet = "\([^"]*\)";.*/\1/p' "$code_api")
longueur_api=$(sed -n 's/.*const int Longueur = \([0-9][0-9]*\);.*/\1/p' "$code_api")
verifier 'l’alphabet du script est celui de l’API' egal "$ALPHABET_CODE" "$alphabet_api"
verifier 'le code tiré a la longueur que l’API attend' egal "${#code}" "$longueur_api"
dans_l_alphabet_api() { [ -n "$alphabet_api" ] && [[ "$code" =~ ^["$alphabet_api"]+$ ]]; }
verifier 'le code tiré n’emploie que l’alphabet de l’API' dans_l_alphabet_api

verifier 'un secret de 32 octets fait 64 chiffres hexadécimaux' \
  bash -c '[[ "$1" =~ ^[0-9a-f]{64}$ ]]' _ "$(generer_secret 32)"

# Par groupes de cinq, pour être lu à l'écran et recopié ; l'API retire les tirets.
verifier 'le code se lit en quatre groupes de cinq' \
  bash -c '[[ "$1" =~ ^[ABCDEFGHJKMNPQRSTUVWXYZ23456789]{5}(-[ABCDEFGHJKMNPQRSTUVWXYZ23456789]{5}){3}$ ]]' _ "$(code_lisible "$code")"
lisible=$(code_lisible "$code")
verifier 'sans ses tirets, le code lisible est le code' egal "${lisible//-/}" "$code"
verifier 'un dernier groupe court reste court' egal "$(code_lisible ABCDEFG)" ABCDE-FG
verifier 'cinq symboles font un seul groupe' egal "$(code_lisible ABCDE)" ABCDE
verifier 'un code vide donne une ligne vide' egal "$(code_lisible '' | od -An -c | tr -d ' ')" '\n'

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

incomplet="$dossier/incomplet.env"
printf 'KLEAN_DOMAINE=klean.exemple.fr\nKLEAN_ADMIN_MOT_DE_PASSE=\n' > "$incomplet"
verifier 'les clés absentes ou vides sont nommées' \
  egal "$(cles_manquantes "$incomplet" KLEAN_DOMAINE POSTGRES_PASSWORD KLEAN_ADMIN_MOT_DE_PASSE | tr '\n' ' ')" \
  'POSTGRES_PASSWORD KLEAN_ADMIN_MOT_DE_PASSE '
verifier 'un .env complet ne manque de rien' egal "$(cles_manquantes "$incomplet" KLEAN_DOMAINE)" ''
printf "KLEAN_ADMIN_MOT_DE_PASSE=''\n" > "$incomplet"
verifier 'une valeur vide entre apostrophes manque aussi' \
  egal "$(cles_manquantes "$incomplet" KLEAN_ADMIN_MOT_DE_PASSE)" KLEAN_ADMIN_MOT_DE_PASSE

rm -rf "$dossier"

# ---- Réponses aux questions --------------------------------------------------

# accepte validation saisie attendu ; refuse validation saisie : la sortie de refus
# est la raison montrée, jamais vide.
accepte() { egal "$("$1" "$2")" "$3"; }
refuse() {
  local raison
  if raison=$("$1" "$2"); then printf '       accepté : « %s »\n' "$raison"; return 1; fi
  [ -n "$raison" ] || { printf '       refus sans raison\n'; return 1; }
}

verifier 'domaine copié du navigateur' accepte normaliser_domaine ' https://Klean.Exemple.FR/ ' klean.exemple.fr
verifier 'domaine en http, barres finales' accepte normaliser_domaine 'http://klean.exemple.fr//' klean.exemple.fr
verifier 'domaine sans extension refusé' refuse normaliser_domaine klean
verifier 'domaine avec un chemin refusé' refuse normaliser_domaine klean.exemple.fr/connexion
verifier 'domaine vide refusé' refuse normaliser_domaine ''

# Le seul test de l'apostrophe typographique dans poser_env : elle s'écrit, et se relit.
apostrophe_ecrivable() {
  local f
  f=$(mktemp)
  poser_env "$f" X 'Propreté d’Arvor' &&
    egal "$(lire_env "$f" X)" 'Propreté d’Arvor'
  local r=$?
  rm -f "$f"
  return "$r"
}
verifier 'une apostrophe typographique s’écrit dans .env' apostrophe_ecrivable

verifier 'adresse plausible' accepte adresse_plausible ' patron@propre-sarl.fr ' patron@propre-sarl.fr
verifier 'adresse sans domaine refusée' refuse adresse_plausible patron@exemple
verifier 'adresse à apostrophe refusée' refuse adresse_plausible "o'neil@exemple.fr"
verifier 'adresse vide refusée' refuse adresse_plausible ''
verifier 'expéditeur obligatoire' refuse expediteur_smtp ''
verifier 'expéditeur plausible' accepte expediteur_smtp klean@exemple.fr klean@exemple.fr

verifier 'serveur SMTP vide : on s’en passe' accepte hote_smtp '  ' ''
verifier 'serveur SMTP en minuscules' accepte hote_smtp SMTP.Exemple.fr smtp.exemple.fr
verifier 'serveur SMTP à espace refusé' refuse hote_smtp 'smtp exemple.fr'

verifier 'port 587' accepte port_smtp ' 587 ' 587
verifier 'port à zéro de tête' accepte port_smtp 0465 465
verifier 'port 65536 refusé' refuse port_smtp 65536
verifier 'port 0 refusé' refuse port_smtp 0
verifier 'port non numérique refusé' refuse port_smtp 58a7
verifier 'port de six chiffres refusé' refuse port_smtp 100000

verifier 'identifiant SMTP vide accepté' accepte identifiant_smtp '' ''
verifier 'identifiant SMTP rogné' accepte identifiant_smtp ' moi@exemple.fr ' moi@exemple.fr
verifier 'identifiant SMTP à apostrophe refusé' refuse identifiant_smtp "l'identifiant"
verifier 'mot de passe SMTP gardé tel quel' accepte mot_de_passe_smtp ' a$b c ' ' a$b c '
verifier 'mot de passe SMTP vide refusé' refuse mot_de_passe_smtp ''
verifier 'mot de passe SMTP à apostrophe refusé' refuse mot_de_passe_smtp "l'secret"
verifier 'mot de passe SMTP à saut de ligne refusé' refuse mot_de_passe_smtp $'un\ndeux'

verifier 'Oui' accepte reponse_oui_non ' Oui ' oui
verifier 'o' accepte reponse_oui_non o oui
verifier 'N' accepte reponse_oui_non N non
verifier 'peut-être refusé' refuse reponse_oui_non peut-être
verifier 'vide refusé' refuse reponse_oui_non ''

# ---- Versions et adresses ----------------------------------------------------

verifier 'version lue sur une URL de version' \
  egal "$(version_depuis_url https://github.com/a/b/releases/tag/v1.2.3)" 1.2.3
version_absente() { ! version_depuis_url https://github.com/a/b/releases > /dev/null; }
verifier 'une URL sans version est refusée' version_absente

verifier '0.1.0 précède 0.2.0' version_anterieure 0.1.0 0.2.0
verifier '0.9.0 précède 0.10.0, en nombres et non en texte' version_anterieure 0.9.0 0.10.0
verifier '1.9.9 précède 2.0.0' version_anterieure 1.9.9 2.0.0
pas_anterieure() { ! version_anterieure "$1" "$2"; }
verifier 'une version ne se précède pas elle-même' pas_anterieure 1.2.3 1.2.3
verifier '0.10.0 ne précède pas 0.9.0' pas_anterieure 0.10.0 0.9.0
verifier '1.2.3 ne précède pas 1.2.2' pas_anterieure 1.2.3 1.2.2
verifier '08 n’est pas de l’octal' version_anterieure 0.8.0 0.08.1
verifier 'une version mal formée ne précède rien' pas_anterieure 1.2 1.2.3
verifier 'rien ne précède une version mal formée' pas_anterieure 1.2.3 v1.3.0
pas_valide() { ! version_valide "$1"; }
verifier '1.2.3 est une version' version_valide 1.2.3
verifier 'v1.2.3 n’en est pas une' pas_valide v1.2.3
verifier '1.2.3-rc1 n’en est pas une' pas_valide 1.2.3-rc1

hex=0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef
verifier 'l’empreinte publiée est lue' \
  egal "$(empreinte_publiee "$hex  klean-1.2.3.tar.gz" klean-1.2.3.tar.gz)" "$hex"
empreinte_refusee() { ! empreinte_publiee "$1" klean-1.2.3.tar.gz > /dev/null; }
verifier 'l’empreinte d’une autre version est refusée' empreinte_refusee "$hex  klean-1.2.2.tar.gz"
verifier 'une empreinte en majuscules est refusée' empreinte_refusee "${hex^^}  klean-1.2.3.tar.gz"
verifier 'une empreinte de 63 chiffres est refusée' empreinte_refusee "${hex:1}  klean-1.2.3.tar.gz"
verifier 'une seconde ligne est refusée' empreinte_refusee "$hex  klean-1.2.3.tar.gz"$'\n'"$hex  klean-1.2.3.tar.gz"
verifier 'un fichier vide est refusé' empreinte_refusee ''
verifier 'une page HTML est refusée' empreinte_refusee '<html>Not Found</html>'

verifier 'le manifeste est lu, un nom par ligne' \
  egal "$(manifeste_valide $'klean\nnouveau.sh\nVERSION' | tr '\n' ' ')" 'klean nouveau.sh VERSION '
manifeste_refuse() { ! manifeste_valide "$1" > /dev/null; }
verifier 'un chemin dans le manifeste est refusé' manifeste_refuse $'../etc/passwd\nVERSION'
verifier 'une barre oblique dans le manifeste est refusée' manifeste_refuse $'sous/klean\nVERSION'
verifier 'un nom à point initial est refusé' manifeste_refuse $'.env\nVERSION'
verifier 'deux points dans un nom sont refusés' manifeste_refuse $'a..b\nVERSION'
verifier 'une ligne vide est refusée' manifeste_refuse $'klean\n\nVERSION'
verifier 'VERSION doit venir en dernier' manifeste_refuse $'VERSION\nklean'
verifier 'un manifeste vide est refusé' manifeste_refuse ''

KLEAN_DEPOT_GITHUB=LeComiko/klean
verifier 'URL de l’archive' egal "$(url_archive 1.2.3)" \
  https://github.com/LeComiko/klean/releases/download/v1.2.3/klean-1.2.3.tar.gz
verifier 'URL de la dernière version' egal "$(url_derniere_version)" \
  https://github.com/LeComiko/klean/releases/latest
verifier 'le propriétaire passe en minuscules dans le nom des images' \
  egal "$(nom_images)" ghcr.io/lecomiko/klean

# ---- Machine -----------------------------------------------------------------

verifier 'Debian amd64 accepté' systeme_pris_en_charge debian amd64
verifier 'Ubuntu amd64 accepté' systeme_pris_en_charge ubuntu amd64
refuse_arm() { ! systeme_pris_en_charge debian arm64; }
verifier 'arm64 refusé' refuse_arm
refuse_fedora() { ! systeme_pris_en_charge fedora amd64; }
verifier 'Fedora refusé' refuse_fedora
verifier '1 Go suffit' memoire_suffisante 1000000
refuse_memoire() { ! memoire_suffisante 500000; }
verifier '512 Mo ne suffisent pas' refuse_memoire

# ---- DNS ---------------------------------------------------------------------

machine=$'51.75.1.2\n2001:41d0::1'
verifier 'toutes les adresses du domaine sont sur la machine' \
  adresses_etrangeres $'51.75.1.2\n2001:41d0::1' "$machine"
etrangere() { [ "$(adresses_etrangeres $'51.75.1.2\n2001:db8::9' "$machine")" = 2001:db8::9 ]; }
verifier 'une adresse IPv6 étrangère est nommée' etrangere
refuse_vide() { ! adresses_etrangeres '' "$machine"; }
verifier 'un domaine qui ne résout pas est refusé' refuse_vide

# ---- Rotation ----------------------------------------------------------------

liste=$'/s/klean-2026-10-01T0330.dump\n/s/klean-2026-11-10T0330.dump\n/s/klean-2026-11-20T0330-avant-1.2.0.dump'
verifier 'seules les sauvegardes antérieures à la limite partent' \
  egal "$(sauvegardes_a_supprimer "$liste" 2026-11-07)" /s/klean-2026-10-01T0330.dump
tout_vieux=$'/s/klean-2026-01-01T0330.dump\n/s/klean-2026-01-02T0330.dump'
verifier 'la plus récente reste même hors délai' \
  egal "$(sauvegardes_a_supprimer "$tout_vieux" 2026-11-07)" /s/klean-2026-01-01T0330.dump
verifier 'le trousseau suit la même règle' \
  egal "$(sauvegardes_a_supprimer $'/t/trousseau-2026-01-01T0330.tgz\n/t/trousseau-2026-11-20T0330.tgz' 2026-11-07)" \
  /t/trousseau-2026-01-01T0330.tgz
verifier 'une sauvegarde datée de la limite reste' \
  egal "$(sauvegardes_a_supprimer $'/s/klean-2026-11-07T0330.dump\n/s/klean-2026-11-20T0330.dump' 2026-11-07)" ''
verifier 'un nom étranger ne passe pas pour la plus récente' \
  egal "$(sauvegardes_a_supprimer $'/s/klean-2026-01-01T0330.dump\n/s/zzz' 2026-11-07)" ''
# `klean` nomme ses sauvegardes à la seconde depuis la tâche 9 : …THHMMSS suit le motif,
# et se trie avec les noms à la minute d'avant.
a_la_seconde=$'/s/klean-2026-10-01T033015.dump\n/s/klean-2026-11-20T0330-avant-0.2.0.dump\n/s/klean-2026-11-20T120005-avant-restauration.dump'
verifier 'une sauvegarde nommée à la seconde suit la rotation' \
  egal "$(sauvegardes_a_supprimer "$a_la_seconde" 2026-11-07)" /s/klean-2026-10-01T033015.dump
verifier 'la plus récente nommée à la seconde reste même hors délai' \
  egal "$(sauvegardes_a_supprimer $'/s/klean-2026-01-01T0330.dump\n/s/klean-2026-01-02T033059.dump' 2026-11-07)" \
  /s/klean-2026-01-01T0330.dump
# Un appel nu, sous `set -e` comme dans `klean` : une liste vide ne doit pas le faire quitter.
liste_vide_sans_echec() { bash -c 'set -euo pipefail; . "$1"; sauvegardes_a_supprimer "" 2026-11-07; echo fin' _ "$ici/../fonctions.sh" | grep -qx fin; }
verifier 'une liste vide ne fait pas quitter klean' liste_vide_sans_echec

refuse_prefixe() { ! adresses_etrangeres '51.75.1.2' '51.75.1.20' > /dev/null; }
verifier 'une adresse n’est pas reconnue par son préfixe' refuse_prefixe

# ---- Publication -------------------------------------------------------------

sortie=$(mktemp -d)
"$ici/../publier.sh" 1.2.3 LeComiko/klean "$sortie" > /dev/null
contenu=$(mktemp -d)
tar xzf "$sortie/klean-1.2.3.tar.gz" -C "$contenu"

archive_complete() {
  local f
  for f in docker-compose.yml fonctions.sh klean klean.cron env.modele FICHIERS VERSION; do
    [ -f "$contenu/$f" ] || { printf '       manque %s\n' "$f"; return 1; }
  done
}
verifier 'l’archive contient les sept fichiers' archive_complete
verifier 'le manifeste nomme exactement les fichiers de l’archive' \
  egal "$(tar tzf "$sortie/klean-1.2.3.tar.gz" | sort | tr '\n' ' ')" "$(sort "$contenu/FICHIERS" | tr '\n' ' ')"
manifeste_publie_valide() { manifeste_valide "$(cat "$contenu/FICHIERS")" > /dev/null; }
verifier 'le manifeste publié est valide, VERSION en dernier' manifeste_publie_valide
# installer.sh copie sa propre liste : elle doit être celle du manifeste de son archive.
installateur_suit_le_manifeste() {
  egal "$(sed -n "s/^FICHIERS='\(.*\)'$/\1/p" "$sortie/installer.sh")" "$(tr '\n' ' ' < "$contenu/FICHIERS" | sed 's/ $//')"
}
verifier 'installer.sh copie les fichiers du manifeste, dans son ordre' installateur_suit_le_manifeste
verifier 'VERSION porte la version' egal "$(cat "$contenu/VERSION")" 1.2.3
sans_build() { ! grep -qE '^[[:space:]]+build:' "$contenu/docker-compose.yml"; }
verifier 'le compose publié ne construit rien' sans_build
verifier 'le dépôt est écrit dans fonctions.sh' grep -q "KLEAN_DEPOT_GITHUB:-LeComiko/klean" "$contenu/fonctions.sh"
sans_marque() { ! grep -q '@DEPOT@\|@VERSION@\|@EMPREINTE@' "$sortie/installer.sh" "$contenu/fonctions.sh"; }
verifier 'aucune marque ne reste' sans_marque
empreintes() { (cd "$sortie" && sha256sum -c --status klean-1.2.3.tar.gz.sha256 installer.sh.sha256); }
verifier 'les empreintes sont justes' empreintes
empreinte_gravee() {
  egal "$(sed -n "s/^EMPREINTE_ARCHIVE='\\(.*\\)'$/\\1/p" "$sortie/installer.sh")" \
    "$(sha256sum "$sortie/klean-1.2.3.tar.gz" | cut -d' ' -f1)"
}
verifier 'l’installateur porte l’empreinte de l’archive' empreinte_gravee
# La forme que `klean mettre-a-jour` relit : si publier.sh en changeait, aucune mise à
# jour ne passerait plus.
verifier 'mettre-a-jour sait lire le .sha256 publié' \
  egal "$(empreinte_publiee "$(cat "$sortie/klean-1.2.3.tar.gz.sha256")" klean-1.2.3.tar.gz)" \
  "$(sha256sum "$sortie/klean-1.2.3.tar.gz" | cut -d' ' -f1)"
klean_executable() { [ -x "$contenu/klean" ]; }
verifier 'klean est exécutable dans l’archive' klean_executable
refuse_version() { ! "$ici/../publier.sh" 1.2 a/b "$sortie" 2> /dev/null; }
verifier 'une version mal formée est refusée' refuse_version
refuse_depot() { ! "$ici/../publier.sh" 1.2.3 a "$sortie" 2> /dev/null; }
verifier 'un dépôt mal formé est refusé' refuse_depot

# La même version, publiée de nouveau une seconde plus tard, donne la même archive —
# manifeste compris.
seconde=$(mktemp -d)
sleep 1
"$ici/../publier.sh" 1.2.3 LeComiko/klean "$seconde" > /dev/null
verifier 'l’archive est reproductible' \
  egal "$(cut -d' ' -f1 "$seconde/klean-1.2.3.tar.gz.sha256")" "$(cut -d' ' -f1 "$sortie/klean-1.2.3.tar.gz.sha256")"

rm -rf "$sortie" "$contenu" "$seconde"

# ---- Heure de la sauvegarde de nuit ------------------------------------------

instant() { date -u -d "$1" +%s; }
# Vrai si la commande échoue : un `bash -c '! …'` ne verrait pas les fonctions sourcées.
faux() { ! "$@"; }

verifier 'Paris l’hiver : 2 h 30 UTC est 3 h 30' heure_de_la_sauvegarde Europe/Paris "$(instant 2026-01-15T02:30:00Z)"
verifier 'Paris l’hiver : 3 h 30 UTC est 4 h 30' faux heure_de_la_sauvegarde Europe/Paris "$(instant 2026-01-15T03:30:00Z)"
verifier 'Paris l’été : 1 h 30 UTC est 3 h 30' heure_de_la_sauvegarde Europe/Paris "$(instant 2026-07-15T01:30:00Z)"
verifier 'Paris l’été : 2 h 30 UTC est 4 h 30' faux heure_de_la_sauvegarde Europe/Paris "$(instant 2026-07-15T02:30:00Z)"
verifier 'La Réunion : 23 h 30 UTC est 3 h 30 le lendemain' heure_de_la_sauvegarde Indian/Reunion "$(instant 2026-01-15T23:30:00Z)"

# Une fois par jour exactement : le cron appelle à chaque heure, à la demie UTC. Les
# deux jours de changement d'heure à Paris, un jour ordinaire, et un fuseau décalé
# d'une demi-heure, où la demie UTC tombe à l'heure pile.
sauvegardes_du_jour() {
  local debut n=0 h
  debut=$(instant "$2T00:30:00Z")
  for h in $(seq 0 23); do
    if heure_de_la_sauvegarde "$1" $((debut + h * 3600)); then n=$((n + 1)); fi
  done
  echo "$n"
}
verifier 'une seule sauvegarde le jour du passage à l’heure d’été' egal "$(sauvegardes_du_jour Europe/Paris 2026-03-29)" 1
verifier 'une seule sauvegarde le jour du retour à l’heure d’hiver' egal "$(sauvegardes_du_jour Europe/Paris 2026-10-25)" 1
verifier 'une seule sauvegarde un jour ordinaire' egal "$(sauvegardes_du_jour Europe/Paris 2026-11-22)" 1
verifier 'une seule sauvegarde dans un fuseau à la demi-heure' egal "$(sauvegardes_du_jour Asia/Kolkata 2026-11-22)" 1

verifier 'Europe/Paris est un fuseau' fuseau_valide Europe/Paris
verifier 'UTC est un fuseau' fuseau_valide UTC
verifier 'America/Martinique est un fuseau' fuseau_valide America/Martinique
verifier 'un fuseau mal écrit est refusé' faux fuseau_valide Europe/Pari
verifier 'un chemin qui remonte est refusé' faux fuseau_valide ../../../etc/passwd
verifier 'un fuseau vide est refusé' faux fuseau_valide ''
verifier 'un chemin absolu est refusé' faux fuseau_valide /usr/share/zoneinfo/UTC

# ---- IPv6 --------------------------------------------------------------------

# ipv6_disponible reçoit les deux fichiers de /proc qu'elle lit : disable_ipv6, puis
# if_inet6 — une ligne par adresse IPv6 de la machine, vide sous disable_ipv6=1.
dossier=$(mktemp -d)
printf '0\n' > "$dossier/actif"
printf '1\n' > "$dossier/desactive"
: > "$dossier/vide"
printf '00000000000000000000000000000001 01 80 10 80       lo\n' > "$dossier/interfaces"
verifier 'IPv6 disponible' ipv6_disponible "$dossier/actif" "$dossier/interfaces"
verifier 'sans disable_ipv6, pas d’IPv6' faux ipv6_disponible "$dossier/absent" "$dossier/interfaces"
verifier 'IPv6 désactivé : pas d’IPv6' faux ipv6_disponible "$dossier/desactive" "$dossier/vide"
verifier 'disable_ipv6=1 suffit à dire non' faux ipv6_disponible "$dossier/desactive" "$dossier/interfaces"
verifier 'aucune adresse IPv6 : pas d’IPv6' faux ipv6_disponible "$dossier/actif" "$dossier/vide"
verifier 'sans if_inet6, pas d’IPv6' faux ipv6_disponible "$dossier/actif" "$dossier/absent"
# Un fichier de /proc annonce une taille nulle, quel que soit son contenu : `[ -s ]` le
# dirait vide. /proc/self/status est toujours là, et jamais vide.
verifier 'un fichier de /proc se lit, il ne se mesure pas' ipv6_disponible "$dossier/actif" /proc/self/status
rm -rf "$dossier"

# Ce que docker compose lit à coup sûr dans enable_ipv6. Vide, le compose retombe sur true.
# Les autres valeurs ne sont pas refusées ici : klean les soumet à compose.
verifier 'KLEAN_IPV6=true est reconnu' reglage_ipv6_reconnu true
verifier 'KLEAN_IPV6=false est reconnu' reglage_ipv6_reconnu false
verifier 'KLEAN_IPV6 vide est reconnu' reglage_ipv6_reconnu ''
verifier 'KLEAN_IPV6=oui n’est pas reconnu d’office' faux reglage_ipv6_reconnu oui
verifier 'KLEAN_IPV6=1 n’est pas reconnu d’office' faux reglage_ipv6_reconnu 1
verifier 'KLEAN_IPV6 suivi d’un commentaire n’est pas reconnu d’office' faux reglage_ipv6_reconnu 'false # pas d’IPv6'
# Une mise à jour l'ajoute, avec ce défaut, aux .env qui ne l'ont pas.
verifier 'env.modele ajoute KLEAN_IPV6=true' egal "$(lire_env "$ici/../env.modele" KLEAN_IPV6)" true

# ---- Installation ------------------------------------------------------------

verifier 'des comptes : installée' egal "$(etat_installation 3)" installee
verifier 'aucun compte : à faire' egal "$(etat_installation 0)" a-faire
verifier 'aucun compte et aucune entreprise : à faire' egal "$(etat_installation 0 0)" a-faire
verifier 'aucun compte mais une entreprise : orpheline' egal "$(etat_installation 0 1)" orpheline
verifier 'des comptes : installée, quelles que soient les entreprises' egal "$(etat_installation 2 '')" installee
# PostgreSQL muet ne dit ni « installée » ni « à faire » : un code tiré, ou montré, sur une
# instance qui a ses comptes.
verifier 'un nombre de comptes vide est illisible' faux etat_installation ''
verifier 'un message d’erreur n’est pas un nombre de comptes' faux etat_installation 'psql: error'
verifier 'un nombre d’entreprises vide est illisible' faux etat_installation 0 ''

dossier=$(mktemp -d)
env="$dossier/.env"
cles_a_retirer() { cles_d_installation_a_retirer "$env" "$1" | tr '\n' ' '; }
# Un code tiré, jamais écrit en clair dans le dépôt : il serait valable pour l'API.
printf 'KLEAN_DOMAINE=klean.exemple.fr\nKLEAN_CODE_INSTALLATION=%s\nKLEAN_ADMIN_EMAIL=patron@exemple.fr\nKLEAN_ADMIN_MOT_DE_PASSE=\nKLEAN_RAISON_SOCIALE=Propre\n' "$code" > "$env"
verifier 'des comptes : le code et les restes d’amorçage partent' \
  egal "$(cles_a_retirer 1)" 'KLEAN_CODE_INSTALLATION KLEAN_ADMIN_EMAIL KLEAN_RAISON_SOCIALE '
# Le statut à part, et non à travers un tube : un échec y passerait pour « rien ».
rien_sans_compte() {
  local sortie
  sortie=$(cles_d_installation_a_retirer "$env" 0) || { printf '       échec\n'; return 1; }
  egal "$sortie" ''
}
verifier 'aucun compte : rien ne part' rien_sans_compte
verifier 'un nombre de comptes illisible ne retire rien' faux cles_d_installation_a_retirer "$env" ''
# Le mot de passe provisoire d'une installation 0.1.x inachevée n'a pas encore été montré :
# montrer_puis_retirer_mot_de_passe a besoin de lui, et de l'adresse qu'il ouvre.
printf 'KLEAN_CODE_INSTALLATION=%s\nKLEAN_ADMIN_EMAIL=patron@exemple.fr\nKLEAN_ADMIN_MOT_DE_PASSE=Provisoire2026\nKLEAN_RAISON_SOCIALE=Propre\n' "$code" > "$env"
verifier 'un mot de passe provisoire pas encore montré garde l’amorçage' \
  egal "$(cles_a_retirer 1)" 'KLEAN_CODE_INSTALLATION '
printf 'KLEAN_DOMAINE=klean.exemple.fr\nKLEAN_CODE_INSTALLATION=\nKLEAN_ADMIN_EMAIL=\n' > "$env"
rien_a_retirer() {
  local sortie
  sortie=$(cles_d_installation_a_retirer "$env" 4) || { printf '       échec\n'; return 1; }
  egal "$sortie" ''
}
verifier 'rien à retirer : rien ne part' rien_a_retirer
printf 'KLEAN_ADMIN_EMAIL=patron@exemple.fr\n' > "$env"
verifier 'un reste d’amorçage part même sans code' egal "$(cles_a_retirer 1)" 'KLEAN_ADMIN_EMAIL '
rm -rf "$dossier"

# ---- Bilan -------------------------------------------------------------------

printf '%s réussis, %s échoués\n' "$reussis" "$echoues"
[ "$echoues" -eq 0 ]
