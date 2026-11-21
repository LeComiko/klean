#!/usr/bin/env bash
# Installe KLEAN sur un serveur neuf Debian ou Ubuntu (SPEC.md, étape 34, lot A1).
#
#     curl -fsSL https://github.com/@DEPOT@/releases/download/v@VERSION@/installer.sh | sudo bash
#
# Toujours depuis une version publiée, jamais depuis la branche principale :
# deploiement/publier.sh remplace @DEPOT@ et @VERSION@.
#
# ⚠️ Tout est dans `principal`, appelée à la dernière ligne : un téléchargement
# interrompu au milieu n'exécute rien.
set -Eeuo pipefail
umask 077

DEPOT='@DEPOT@'
VERSION_KLEAN='@VERSION@'
REPERTOIRE=/opt/klean
FICHIERS='docker-compose.yml klean fonctions.sh klean.cron env.modele VERSION'

echouer() {
  printf '✗ %s\n' "$*" >&2
  exit 1
}

# Lu dans un sous-shell : /etc/os-release définit VERSION, entre autres.
# shellcheck source=/dev/null
os_release() { (. /etc/os-release && printf '%s' "${!1:-}"); }

# Ce que `klean` suppose présent : cron pour la sauvegarde de la nuit (absent d'une
# image Debian minimale), iproute2 pour `ss` et `ip`, curl pour les téléchargements.
installer_outils() {
  apt-get update -qq
  apt-get install -y -qq ca-certificates curl cron iproute2
}

installer_docker() {
  if command -v docker > /dev/null && docker compose version > /dev/null 2>&1; then
    return
  fi
  printf 'Installation de Docker, depuis le dépôt officiel de Docker…\n'
  local id
  id=$(os_release ID)
  apt-get update -qq
  apt-get install -y -qq ca-certificates curl
  install -m 0755 -d /etc/apt/keyrings
  curl -fsSL "https://download.docker.com/linux/$id/gpg" -o /etc/apt/keyrings/docker.asc
  chmod a+r /etc/apt/keyrings/docker.asc
  printf 'deb [arch=%s signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/%s %s stable\n' \
    "$(dpkg --print-architecture)" "$id" "$(os_release VERSION_CODENAME)" > /etc/apt/sources.list.d/docker.list
  chmod 644 /etc/apt/sources.list.d/docker.list
  apt-get update -qq
  apt-get install -y -qq docker-ce docker-ce-cli containerd.io docker-compose-plugin
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

  # Lancé par `curl … | bash`, l'entrée standard est le script lui-même : coupée, pour
  # qu'aucun apt-get ni dpkg ne la lise. Les questions de `klean` passent par /dev/tty.
  exec < /dev/null
  export DEBIAN_FRONTEND=noninteractive

  installer_outils
  installer_docker

  local dossier="$REPERTOIRE/versions/$VERSION_KLEAN" archive url fichier
  install -d -m 700 "$REPERTOIRE" "$REPERTOIRE/versions" "$dossier"
  archive="$dossier/klean-$VERSION_KLEAN.tar.gz"
  url="https://github.com/$DEPOT/releases/download/v$VERSION_KLEAN/klean-$VERSION_KLEAN.tar.gz"
  curl -fsSL -o "$archive" "$url" || echouer "Téléchargement impossible : $url"
  curl -fsSL -o "$archive.sha256" "$url.sha256" || echouer "Empreinte introuvable : $url.sha256"
  (cd "$dossier" && sha256sum -c --status "klean-$VERSION_KLEAN.tar.gz.sha256") ||
    echouer "L'archive téléchargée ne correspond pas à son empreinte : installation arrêtée."
  # --no-same-owner : extraite par root, l'archive ne doit pas poser un uid étranger.
  tar xzf "$archive" -C "$dossier" --no-same-owner

  # Une installation existante garde ses fichiers : relancer l'installateur reprend,
  # il ne rétrograde ni ne met à jour.
  if [ ! -f "$REPERTOIRE/klean" ]; then
    for fichier in $FICHIERS; do
      install -m 600 "$dossier/$fichier" "$REPERTOIRE/$fichier"
    done
    chmod 700 "$REPERTOIRE/klean"
  fi

  exec "$REPERTOIRE/klean" installer
}

principal "$@"
