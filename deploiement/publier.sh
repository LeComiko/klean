#!/usr/bin/env bash
# Fabrique les fichiers d'une version publiée (SPEC.md, étape 34, lot A1) :
#   klean-<version>.tar.gz          docker-compose.yml sans build:, fonctions.sh, klean,
#                                   klean.cron, env.modele, FICHIERS, VERSION
#   installer.sh                    @DEPOT@ et @VERSION@ remplacés, et @EMPREINTE@ par
#                                   l'empreinte de l'archive : l'installateur n'accepte
#                                   qu'elle
#   *.sha256                        une empreinte par fichier publié
#
#     deploiement/publier.sh 1.2.3 proprietaire/depot dist
set -Eeuo pipefail

version="${1:-}"
depot="${2:-}"
sortie="${3:-}"

[[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || { printf 'Version invalide : « %s » (attendu : 1.2.3).\n' "$version" >&2; exit 1; }
[[ "$depot" =~ ^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$ ]] || { printf 'Dépôt invalide : « %s » (attendu : proprietaire/nom).\n' "$depot" >&2; exit 1; }
[ -n "$sortie" ] || { printf 'Dossier de sortie manquant.\n' >&2; exit 1; }

racine=$(cd "$(dirname "$0")/.." && pwd)
# Les fichiers d'une version, VERSION en dernier. Écrits aussi dans le manifeste
# FICHIERS de l'archive : c'est lui que lit le `klean` de la version installée pour
# mettre celle-ci en place, et non sa propre liste, figée dans le code qui tourne — un
# fichier ajouté par une version future ne serait sinon jamais posé.
fichiers='docker-compose.yml fonctions.sh klean klean.cron env.modele FICHIERS VERSION'
travail=$(mktemp -d)
trap 'rm -rf "$travail"' EXIT
mkdir -p "$sortie"

# Le serveur n'a pas le code source : un `build:` resté dans le compose ferait échouer
# `docker compose up` quand une image manque, au lieu de la télécharger.
sed '/^    build: /d' "$racine/docker-compose.yml" > "$travail/docker-compose.yml"
sed "s|@DEPOT@|$depot|g" "$racine/deploiement/fonctions.sh" > "$travail/fonctions.sh"
cp "$racine/deploiement/klean" "$racine/deploiement/klean.cron" "$racine/deploiement/env.modele" "$travail/"
chmod 755 "$travail/klean"
printf '%s\n' "$version" > "$travail/VERSION"
# shellcheck disable=SC2086  # liste de noms simples, découpée exprès
printf '%s\n' $fichiers > "$travail/FICHIERS"

# Une marque restée dans un fichier publié pointerait le serveur vers un dépôt nommé
# « @DEPOT@ » : refusé ici, avant que l'archive existe, plutôt que découvert à la
# première installation.
if grep -l -e '@DEPOT@' -e '@VERSION@' -e '@EMPREINTE@' "$travail"/*; then
  printf 'Marque non remplacée dans les fichiers ci-dessus.\n' >&2
  exit 1
fi

# Reproductible : ni date, ni propriétaire, ni ordre de lecture du disque dans
# l'archive — deux publications du même commit donnent la même empreinte, qu'un tiers
# peut refaire. GNU tar, celui du CI et de la série.
# shellcheck disable=SC2086  # idem
tar --sort=name --mtime='@0' --owner=0 --group=0 --numeric-owner -cf - -C "$travail" $fichiers |
  gzip -n > "$sortie/klean-$version.tar.gz"

# L'archive d'abord, puis l'installateur qui porte son empreinte.
empreinte=$(sha256sum "$sortie/klean-$version.tar.gz" | cut -d' ' -f1)
sed -e "s|@DEPOT@|$depot|g" -e "s|@VERSION@|$version|g" -e "s|@EMPREINTE@|$empreinte|g" \
  "$racine/deploiement/installer.sh" > "$sortie/installer.sh"
chmod 755 "$sortie/installer.sh"

# L'installateur, la seule marque qui reste à poser après l'archive, est relu à la fin.
if grep -l -e '@DEPOT@' -e '@VERSION@' -e '@EMPREINTE@' "$sortie/installer.sh"; then
  rm -f "$sortie/klean-$version.tar.gz" "$sortie/installer.sh"
  printf 'Marque non remplacée dans les fichiers ci-dessus.\n' >&2
  exit 1
fi

cd "$sortie"
sha256sum "klean-$version.tar.gz" > "klean-$version.tar.gz.sha256"
sha256sum installer.sh > installer.sh.sha256
