#!/usr/bin/env bash
# Lance la série du déploiement dans un conteneur Debian 12, comme sur un serveur :
# le bash de macOS (3.2) n'est pas celui des serveurs visés (5.x).
#
#     deploiement/tests/lancer.sh
set -Eeuo pipefail
racine=$(cd "$(dirname "$0")/../.." && pwd)
exec docker run --rm -v "$racine:/klean:ro" debian:12 bash /klean/deploiement/tests/tests.sh
