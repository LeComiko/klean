# Image de l'API KLEAN, et de son exécutable de migration (SPEC.md étape 18).
#
# Une seule image pour les deux : le service `migrer` de docker-compose.yml la
# reprend avec un autre point d'entrée. Deux images pourraient diverger d'une
# version de schéma, et une migration jouée par une autre version que l'API qui la
# lit est exactement le défaut qu'on ne voit qu'en production.
#
# Construite depuis la racine du dépôt : docker compose build

# ---------------------------------------------------------------------------
# Construction
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS construction
WORKDIR /src

# Restauration d'abord, sur le seul fichier de projet : la couche reste en cache
# tant que les dépendances ne changent pas, quel que soit le code modifié.
COPY Klean.Api.csproj ./
RUN dotnet restore Klean.Api.csproj

COPY . ./
RUN dotnet publish Klean.Api.csproj -c Release -o /publication --no-restore

# Le bundle de migration. Même version que Microsoft.EntityFrameworkCore.Design
# dans Klean.Api.csproj : un outil d'une autre version génère un bundle qui
# refuse le modèle, ou pire, l'interprète.
#
# Le bundle construit son contexte par Data/FabriqueDeConception.cs, sans
# Program.cs (SPEC.md étape 26) : il ne lui faut ni chaîne de connexion ni clé JWT
# à la construction, et aucune connexion n'est ouverte ici. Il reçoit la vraie
# chaîne à l'exécution, par l'environnement que lui passe docker-compose.yml.
#
# ⚠️ `--configuration` en toutes lettres : pour `dotnet ef`, `-c` est --context.
RUN dotnet tool install --global dotnet-ef --version 10.0.12
ENV PATH="${PATH}:/root/.dotnet/tools"
RUN dotnet ef migrations bundle --configuration Release -o /publication/efbundle --force

# ---------------------------------------------------------------------------
# Exécution
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

COPY --from=construction /publication ./

# Clés de Data Protection, qui signent les jetons de réinitialisation de mot de
# passe. Par défaut dans le répertoire personnel du conteneur, donc perdues à
# chaque recréation : un lien de réinitialisation envoyé une minute avant une mise
# à jour deviendrait invalide. Le répertoire est créé ici, au nom de l'utilisateur
# `app`, pour qu'un volume nommé monté dessus en hérite — créé par Docker, il
# appartiendrait à root et l'API ne pourrait pas y écrire.
RUN mkdir -p /home/app/.aspnet/DataProtection-Keys \
    && chown -R app:app /home/app/.aspnet

# Utilisateur non privilégié fourni par l'image .NET.
USER app

# En clair, sur le réseau interne : Caddy termine TLS devant (CLAUDE.md §6 bis).
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

ENTRYPOINT ["dotnet", "Klean.Api.dll"]
