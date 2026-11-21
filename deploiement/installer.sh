#!/usr/bin/env bash
# Squelette : complété à la tâche 8.
set -Eeuo pipefail
DEPOT='@DEPOT@'
VERSION='@VERSION@'
principal() { printf 'Installation de KLEAN %s depuis %s\n' "$VERSION" "$DEPOT"; }
principal "$@"
