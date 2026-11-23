#!/usr/bin/env bash
# Lance les deux séries du déploiement dans un conteneur Debian 12, comme sur un serveur :
# le bash de macOS (3.2) n'est pas celui des serveurs visés (5.x). tests.sh éprouve
# fonctions.sh ; commande.sh, ensuite, les gestes de `klean` sans Docker ni PostgreSQL.
# Les deux sont jouées, même si la première échoue.
#
#     deploiement/tests/lancer.sh
set -Eeuo pipefail
racine=$(cd "$(dirname "$0")/../.." && pwd)
exec docker run --rm -v "$racine:/klean:ro" debian:12 bash -c \
  'bash /klean/deploiement/tests/tests.sh; a=$?; bash /klean/deploiement/tests/commande.sh; b=$?; [ "$a" -eq 0 ] && [ "$b" -eq 0 ]'
