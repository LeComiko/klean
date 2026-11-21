#!/usr/bin/env bash
# Installe KLEAN sur un serveur neuf Debian ou Ubuntu (SPEC.md, étape 34, lot A1).
#
#     curl -fsSL https://github.com/@DEPOT@/releases/download/v@VERSION@/installer.sh | sudo bash
#
# Toujours depuis une version publiée, jamais depuis la branche principale :
# deploiement/publier.sh remplace les marques de dépôt, de version et d'empreinte.
#
# ⚠️ Tout est dans `principal`, appelée à la dernière ligne : un téléchargement
# interrompu au milieu n'exécute rien.
set -euo pipefail
umask 077

DEPOT='@DEPOT@'
VERSION_KLEAN='@VERSION@'
# L'empreinte SHA-256 de l'archive de cette version, gravée par publier.sh : l'archive
# est liée à cet installateur. Un .sha256 téléchargé à côté d'elle ne prouverait rien
# de plus que l'archive elle-même — qui peut changer l'une peut changer l'autre.
EMPREINTE_ARCHIVE='@EMPREINTE@'
REPERTOIRE=/opt/klean
# VERSION en dernier : c'est lui qui dit qu'une copie est complète.
FICHIERS='docker-compose.yml klean fonctions.sh klean.cron env.modele VERSION'

echouer() {
  printf '✗ %s\n' "$*" >&2
  exit 1
}

# Lu dans un sous-shell : /etc/os-release définit VERSION, entre autres.
# shellcheck source=/dev/null
os_release() { (. /etc/os-release && printf '%s' "${!1:-}"); }

# Sur un serveur neuf, la mise à jour automatique d'Ubuntu ou de Debian tient souvent
# le verrou d'apt pendant les premières minutes : attendre, plutôt qu'échouer sur
# « Could not get lock » sans rien dire de la cause.
paquets() {
  apt-get -o DPkg::Lock::Timeout=600 "$@" ||
    echouer "L'installation des paquets a échoué (apt-get $*). Vérifiez que le serveur est relié à Internet, puis relancez la même commande d'installation."
}

# Ce que `klean` suppose présent : cron pour la sauvegarde de la nuit (absent d'une
# image Debian minimale), iproute2 pour `ss` et `ip`, curl pour les téléchargements,
# sudo pour les commandes que `klean` donne à taper, absent de certains serveurs où
# l'on est root d'emblée.
#
# `update` réessaie seul : DPkg::Lock::Timeout ne couvre que le verrou de dpkg, pas
# celui des listes, que tient la mise à jour automatique d'un serveur neuf (apt-daily).
mettre_a_jour_listes() {
  local _
  for _ in $(seq 1 60); do
    apt-get update -qq && return 0
    sleep 10
  done
  echouer "La liste des paquets n'a pas pu être mise à jour pendant dix minutes : une mise à jour automatique du système est peut-être en cours, ou le serveur n'est pas relié à Internet. Attendez quelques minutes, puis relancez la même commande d'installation."
}

installer_outils() {
  mettre_a_jour_listes
  paquets install -y -qq ca-certificates curl cron iproute2 sudo
}

# Deux Docker que l'installation ne doit pas trancher seule.
refuser_docker_etranger() {
  local chemin
  chemin=$(command -v docker || true)
  # Celui de snap est confiné : il ne voit pas /opt/klean, et chaque commande
  # échouerait sur un fichier introuvable qui existe.
  if [[ "$chemin" == /snap/* || "$(readlink -f "$chemin" 2> /dev/null)" == /snap/* ]]; then
    echouer "Docker est installé en snap, qui ne voit pas /opt/klean : sudo snap remove docker, puis relancez la même commande d'installation."
  fi
}

installer_docker() {
  if command -v docker > /dev/null && docker compose version > /dev/null 2>&1; then
    return
  fi
  # Celui d'Ubuntu ou de Debian, sans compose : le Docker officiel le remplacerait en
  # silence, conteneurs existants compris. Le retrait est un geste de l'exploitant.
  # Le statut, et non `dpkg -s` seul, qui réussit aussi sur un paquet retiré dont la
  # configuration est restée.
  if dpkg-query -W -f='${Status}' docker.io 2> /dev/null | grep -q 'install ok installed'; then
    echouer "Le Docker fourni par $(os_release NAME) (paquet docker.io) est installé, et KLEAN s'installe avec celui de Docker. Retirez-le : sudo apt-get remove docker.io, puis relancez la même commande d'installation."
  fi
  printf 'Installation de Docker, depuis le dépôt officiel de Docker…\n'
  local id
  id=$(os_release ID)
  install -m 0755 -d /etc/apt/keyrings
  curl -fsSL "https://download.docker.com/linux/$id/gpg" -o /etc/apt/keyrings/docker.asc ||
    echouer "La clé du dépôt de Docker n'a pas pu être téléchargée. Vérifiez que le serveur est relié à Internet, puis relancez la même commande d'installation."
  chmod a+r /etc/apt/keyrings/docker.asc
  printf 'deb [arch=%s signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/%s %s stable\n' \
    "$(dpkg --print-architecture)" "$id" "$(os_release VERSION_CODENAME)" > /etc/apt/sources.list.d/docker.list
  chmod 644 /etc/apt/sources.list.d/docker.list
  mettre_a_jour_listes
  paquets install -y -qq docker-ce docker-ce-cli containerd.io docker-compose-plugin
}

# Une version déjà en place, dont l'installation a commencé — .env existe —, ne se
# remplace pas ici : relancer l'installateur d'une autre version ne doit ni rétrograder
# ni mettre à jour en silence. La même version reprend ; sans .env, rien n'est encore
# installé, et les fichiers sont remplacés.
refuser_autre_version() {
  local installee
  [ -f "$REPERTOIRE/.env" ] && [ -f "$REPERTOIRE/VERSION" ] || return 0
  installee=$(cat "$REPERTOIRE/VERSION")
  [ "$installee" != "$VERSION_KLEAN" ] || return 0
  echouer "KLEAN $installee est déjà installé. Pour passer à la nouvelle version : sudo klean mettre-a-jour"
}

principal() {
  [ "$(id -u)" -eq 0 ] || echouer "L'installation se lance en root : … | sudo bash"
  [ -r /etc/os-release ] || echouer "Système inconnu : KLEAN s'installe sur Debian ou Ubuntu."
  case "$(os_release ID)" in
    debian | ubuntu) ;;
    *) echouer "KLEAN s'installe sur Debian ou Ubuntu (trouvé : $(os_release ID))." ;;
  esac
  [ "$(dpkg --print-architecture)" = amd64 ] ||
    echouer "KLEAN s'installe sur un serveur amd64 (x86-64). Les serveurs ARM ne sont pas encore pris en charge."
  # Une marque restée : cet installateur ne vient pas d'une version publiée.
  [[ "$EMPREINTE_ARCHIVE" =~ ^[0-9a-f]{64}$ ]] ||
    echouer "Cet installateur ne vient pas d'une version publiée : prenez celui de la page des versions de KLEAN."
  refuser_autre_version
  refuser_docker_etranger

  # Lancé par `curl … | bash`, l'entrée standard est le script lui-même : coupée, pour
  # qu'aucun apt-get ni dpkg ne la lise. Les questions de `klean` passent par /dev/tty.
  exec < /dev/null
  export DEBIAN_FRONTEND=noninteractive

  installer_outils
  installer_docker

  local dossier="$REPERTOIRE/versions/$VERSION_KLEAN" archive url fichier obtenue mode
  install -d -m 700 "$REPERTOIRE" "$REPERTOIRE/versions" "$dossier"
  archive="$dossier/klean-$VERSION_KLEAN.tar.gz"
  url="https://github.com/$DEPOT/releases/download/v$VERSION_KLEAN/klean-$VERSION_KLEAN.tar.gz"
  curl -fsSL -o "$archive" "$url" ||
    echouer "Téléchargement impossible : $url. Vérifiez que le serveur est relié à Internet, puis relancez la même commande d'installation."
  # L'empreinte du fichier téléchargé, comparée à celle gravée ci-dessus. Pas
  # `sha256sum -c`, qui vérifie le fichier nommé dans le .sha256, quel qu'il soit.
  obtenue=$(sha256sum "$archive" | cut -d' ' -f1)
  [ "$obtenue" = "$EMPREINTE_ARCHIVE" ] ||
    echouer "L'archive téléchargée n'est pas celle de KLEAN $VERSION_KLEAN : installation arrêtée, rien n'a été installé. Réessayez plus tard ; si cela se répète, signalez-le."
  # --no-same-owner : extraite par root, l'archive ne doit pas poser un uid étranger.
  tar xzf "$archive" -C "$dossier" --no-same-owner

  # Une installation de cette version garde ses fichiers : relancer l'installateur la
  # reprend. Chaque fichier est copié à côté puis renommé, VERSION en dernier : une
  # copie interrompue n'a pas de VERSION, et la relance recopie tout.
  if [ "$(cat "$REPERTOIRE/VERSION" 2> /dev/null || true)" != "$VERSION_KLEAN" ]; then
    rm -f "$REPERTOIRE/VERSION"
    for fichier in $FICHIERS; do
      mode=600
      [ "$fichier" != klean ] || mode=700
      install -m "$mode" "$dossier/$fichier" "$REPERTOIRE/.$fichier.nouveau"
      mv -f "$REPERTOIRE/.$fichier.nouveau" "$REPERTOIRE/$fichier"
    done
  fi

  exec "$REPERTOIRE/klean" installer
}

principal "$@"
