# shellcheck shell=bash
# Fonctions de l'installation de KLEAN (SPEC.md, étape 34, lot A1), sans effet de bord
# hors de leurs arguments : aucune n'appelle Docker ni le réseau. Sourcées par
# `klean` et par tests/tests.sh, c'est ce qui les rend éprouvables.

# Le dépôt qui publie les versions, `propriétaire/nom`. deploiement/publier.sh
# remplace @DEPOT@ ; une variable d'environnement l'emporte, pour les essais.
KLEAN_DEPOT_GITHUB="${KLEAN_DEPOT_GITHUB:-@DEPOT@}"

# Base32 sans caractères ambigus — ni 0, O, 1, I ni L — : 31 symboles. Jamais `$`, que
# docker compose interprète dans .env.
ALPHABET_CODE='ABCDEFGHJKMNPQRSTUVWXYZ23456789'

# Un octet aléatoire, de 0 à 255.
octet_aleatoire() {
  od -An -N1 -tu1 /dev/urandom | tr -d ' \n'
}

# Le code d'installation de l'assistant (lot A2) : 20 symboles, environ 99 bits.
generer_code() {
  local code='' octet
  while [ "${#code}" -lt 20 ]; do
    octet=$(octet_aleatoire)
    # 248 = 8 × 31. Au-delà, l'octet est rejeté : sinon les premiers symboles de
    # l'alphabet sortiraient plus souvent que les autres.
    if [ "$octet" -lt 248 ]; then
      code+="${ALPHABET_CODE:$((octet % 31)):1}"
    fi
  done
  printf '%s\n' "$code"
}

# Un secret hexadécimal de $1 octets. Hexadécimal : un « ; » ou un « = » couperait la
# chaîne de connexion de PostgreSQL (.env.example).
generer_secret() {
  od -An -N"$1" -tx1 /dev/urandom | tr -d ' \n'
  printf '\n'
}

# Un mot de passe provisoire qui passe les règles d'Identity (CLAUDE.md §6) : 16
# caractères, une majuscule, une minuscule et un chiffre au moins.
generer_mot_de_passe() {
  local alphabet='ABCDEFGHJKMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz23456789'
  local mot octet
  while :; do
    mot=''
    while [ "${#mot}" -lt 16 ]; do
      octet=$(octet_aleatoire)
      # 216 = 4 × 54, pour la même raison que dans generer_code.
      if [ "$octet" -lt 216 ]; then
        mot+="${alphabet:$((octet % 54)):1}"
      fi
    done
    if [[ "$mot" =~ [A-Z] && "$mot" =~ [a-z] && "$mot" =~ [0-9] ]]; then
      printf '%s\n' "$mot"
      return
    fi
  done
}
