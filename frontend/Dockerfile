# Image Caddy servant le document KLEAN (SPEC.md étape 18).
#
# Construite depuis la racine du dépôt : docker compose build

# ---------------------------------------------------------------------------
# Construction du document, et de ses en-têtes
# ---------------------------------------------------------------------------
# 22.18 au moins : scripts/entetes-caddy.mjs charge csp.ts sous Node, qui ne
# dépouille les types TypeScript sans drapeau qu'à partir de cette version.
FROM node:22-alpine AS construction
WORKDIR /src

COPY package.json package-lock.json ./
RUN npm ci

COPY . ./
RUN npm run build \
    && node scripts/entetes-caddy.mjs > /entetes-document.caddy

# ---------------------------------------------------------------------------
# Service
# ---------------------------------------------------------------------------
FROM caddy:2-alpine

COPY Caddyfile /etc/caddy/Caddyfile
COPY --from=construction /entetes-document.caddy /etc/caddy/entetes-document.caddy
COPY --from=construction /src/dist /srv
