#!/usr/bin/env python3
"""Jeu de démonstration de KLEAN — semaine du lundi 7 septembre 2026.

Crée tout par l'API, jamais en SQL : les règles métier doivent s'appliquer.
Un INSERT direct produirait un planning que l'application refuserait de créer.

    KLEAN_EMAIL=admin@klean.local KLEAN_MOTDEPASSE='…' \
        python3 backend/Scripts/jeu-de-demonstration.py

Variables : KLEAN_URL (défaut http://localhost:5137), KLEAN_EMAIL,
KLEAN_MOTDEPASSE. Aucun mot de passe par défaut, et aucun écrit ici.

⚠️ L'ordre des appels n'est pas arbitraire. L'absence est créée **en dernier**,
après les dix-sept interventions : depuis l'étape 10 bis, POST /api/interventions
passe par les filtres durs du moteur d'affectation, et la vacation du mercredi de
l'agent absent serait refusée en 400 si l'absence existait déjà. Sa création
déclenche DetecterImpactsAsync, qui pose le remplacement en Recherche.

Composition, imposée par les totaux attendus 17 vacations / 34 h avec des durées
de 120, 180 et 90 minutes — six mélanges seulement satisfont les deux :
8 × 120 + 3 × 180 + 6 × 90 = 2040 minutes. C'est celui qui se range en trois
récurrences lisibles d'une ligne.
"""

import json
import os
import sys
import urllib.error
import urllib.request

URL = os.environ.get("KLEAN_URL", "http://localhost:5137").rstrip("/")
EMAIL = os.environ.get("KLEAN_EMAIL", "")
MOT_DE_PASSE = os.environ.get("KLEAN_MOTDEPASSE", "")

LUNDI = "2026-09-07"
JOURS = {
    "lundi": "2026-09-07",
    "mardi": "2026-09-08",
    "mercredi": "2026-09-09",
    "jeudi": "2026-09-10",
    "vendredi": "2026-09-11",
}


class Api:
    """Client minimal. Porte les cookies à la main et respecte la double
    soumission anti-CSRF : le cookie klean_csrf est relu après chaque réponse,
    parce que le serveur le régénère à chaque rafraîchissement."""

    def __init__(self, base):
        self.base = base
        self.cookies = {}

    def _memoriser(self, reponse):
        for entete in reponse.headers.get_all("Set-Cookie") or []:
            nom, _, reste = entete.partition("=")
            self.cookies[nom.strip()] = reste.split(";", 1)[0]

    def appeler(self, methode, chemin, corps=None):
        donnees = None if corps is None else json.dumps(corps).encode()
        requete = urllib.request.Request(self.base + chemin, data=donnees, method=methode)

        if donnees is not None:
            requete.add_header("Content-Type", "application/json")

        if self.cookies:
            requete.add_header(
                "Cookie", "; ".join(f"{n}={v}" for n, v in self.cookies.items())
            )

        if methode not in ("GET", "HEAD") and "klean_csrf" in self.cookies:
            requete.add_header("X-Klean-CSRF", self.cookies["klean_csrf"])

        try:
            with urllib.request.urlopen(requete) as reponse:
                self._memoriser(reponse)
                brut = reponse.read().decode()
                return json.loads(brut) if brut else None
        except urllib.error.HTTPError as echec:
            detail = echec.read().decode(errors="replace").strip()
            arreter(f"{methode} {chemin} → {echec.code}\n   {detail}")


def arreter(message):
    print(f"\n✗ {message}", file=sys.stderr)
    sys.exit(1)


def main():
    if not EMAIL or not MOT_DE_PASSE:
        arreter(
            "KLEAN_EMAIL et KLEAN_MOTDEPASSE sont obligatoires.\n"
            "   Aucun identifiant n'est écrit dans ce script."
        )

    api = Api(URL)

    print(f"→ {URL}")
    session = api.appeler("POST", "/api/auth/connexion",
                          {"email": EMAIL, "motDePasse": MOT_DE_PASSE})
    profil = session["profil"]
    print(f"  connecté : {profil['email']}  rôles {', '.join(profil['roles'])}")

    # ---- Garde ----
    # La bonne condition n'est pas « mes sites existent-ils » mais « la semaine
    # cible est-elle vide ». Les totaux attendus (17 / 34 / 3) sont ceux du
    # planning de l'entreprise entière : n'importe quelle vacation préexistante
    # cette semaine-là les fausse, qu'elle vienne d'un second passage de ce script
    # ou d'ailleurs.
    #
    # Le cas s'est produit : une série de tests avait laissé une intervention dans
    # klean_test, et le jeu s'est empilé dessus — 18 / 36 / 4. La vérification
    # finale l'a bien refusé, mais après avoir tout écrit.
    deja = api.appeler("GET", f"/api/planning?lundi={LUNDI}")
    if deja["totalVacations"] > 0:
        existants = api.appeler("GET", "/api/sites?taille=200")
        miens = [s["nom"] for s in existants["elements"] if s["nom"] in NOMS_DE_SITES]

        arreter(
            f"La semaine du {LUNDI} porte déjà {deja['totalVacations']} vacation(s) "
            f"et {len(deja['agents'])} agent(s).\n"
            + ("   Le jeu de démonstration est déjà installé : "
               + ", ".join(miens) + ".\n" if miens else "")
            + f"   Supprimer le client « {CLIENT['nom']} » — ses sites, contrats, "
            + "prestations et interventions partent en cascade — ou vider les tables\n"
            + "   métier si la base est jetable. Les totaux attendus sont ceux de "
            + "l'entreprise entière, pas de ce seul jeu."
        )

    # ---- Client, sites, contrats, prestations ----
    client = api.appeler("POST", "/api/clients", CLIENT)
    print(f"  client   : {client['nom']}")

    prestations = {}
    for cle, site_dto, contrat_dto, prestation_dto in SITES:
        site = api.appeler("POST", "/api/sites", {**site_dto, "clientId": client["id"]})
        contrat = api.appeler("POST", "/api/contrats",
                              {**contrat_dto, "siteId": site["id"]})
        prestation = api.appeler("POST", "/api/prestations",
                                 {**prestation_dto, "contratId": contrat["id"]})
        prestations[cle] = prestation["id"]
        print(f"  site     : {site['nom']:26s} {prestation['dureeEstimeeMinutes']:3d} min"
              f"   « {contrat['frequence']} »")

    # ---- Agents ----
    agents = {}
    for cle, agent_dto in AGENTS:
        agent = api.appeler("POST", "/api/agents", agent_dto)
        agents[cle] = agent["id"]
        print(f"  agent    : {agent['prenom']} {agent['nom']}")

    # ---- Dix-sept interventions ----
    # Chaque appel traverse EstEligibleAsync : absence, chevauchement et repos de
    # onze heures entre journées de travail. Un 400 ici signale un horaire
    # illégal, pas une panne du script.
    print("\n  interventions")
    minutes = 0
    sans_agent = 0
    for cle_site, jour, heure, cle_agent in INTERVENTIONS:
        corps = {
            "prestationId": prestations[cle_site],
            "agentId": None if cle_agent is None else agents[cle_agent],
            "datePrevue": JOURS[jour],
            "heurePrevue": heure,
            "statut": "Planifiee",
        }
        api.appeler("POST", "/api/interventions", corps)
        duree = DUREES[cle_site]
        minutes += duree
        if cle_agent is None:
            sans_agent += 1
        etiquette = "— à pourvoir" if cle_agent is None else cle_agent
        print(f"    {jour:9s} {heure[:5]}  {cle_site:11s} {duree:3d} min  {etiquette}")

    print(f"\n  {len(INTERVENTIONS)} vacations, {minutes} minutes = "
          f"{minutes / 60:.0f} h, dont {sans_agent} sans agent")

    # ---- L'absence, en DERNIER ----
    absence = api.appeler("POST", "/api/absences", {
        "agentId": agents[AGENT_ABSENT],
        "type": "Maladie",
        "dateDebut": JOURS["mercredi"],
        "dateFin": JOURS["mercredi"],
        # Pas de motif : le backend refuse en 400 un arrêt maladie qui en porte
        # un — donnée de santé au sens de l'article 9 du RGPD, et le planning
        # n'a besoin de savoir QUE l'agent est absent.
    })
    print(f"\n  absence  : {AGENT_ABSENT}, maladie le mercredi "
          f"({absence['dateDebut'][:10]})")

    impacts = api.appeler("GET", "/api/remplacements?taille=200")
    print(f"  remplacements ouverts par la détection d'impacts : {impacts['total']}")

    # ---- Contrôle des totaux, sur la réponse du serveur ----
    planning = api.appeler("GET", f"/api/planning?lundi={LUNDI}")
    attendus = (17, 34.0, 3)
    obtenus = (planning["totalVacations"], planning["totalHeures"],
               planning["totalNonPourvues"])

    print(f"\n  KPI du serveur : {obtenus[0]} vacations / {obtenus[1]:.0f} h / "
          f"{obtenus[2]} non pourvues")
    print(f"  agents dans la grille : {len(planning['agents'])}")

    if obtenus != attendus:
        arreter(f"les totaux ne sont pas ceux de la spec : attendu {attendus}")

    if len(planning["agents"]) != 4:
        arreter(f"{len(planning['agents'])} agents dans la grille, 4 attendus")

    print("\n✓ Jeu de démonstration installé, totaux conformes à SPEC.md étape 14.")


# ---------------------------------------------------------------------------
# Les données. Séparées du code pour qu'elles se relisent comme un planning.
# ---------------------------------------------------------------------------

CLIENT = {
    "nom": "Bellevue Gestion",
    "type": "Syndic",
    "email": "contact@bellevue-gestion.example",
    "telephone": "01 45 67 89 10",
    "adresse": "18 rue de la Convention",
    "codePostal": "75015",
    "ville": "Paris",
}

DUREES = {"Bellevue": 120, "Haussmann": 180, "Daumesnil": 90}

NOMS_DE_SITES = {"Résidence Bellevue", "Immeuble Haussmann", "Copropriété Daumesnil"}

SITES = [
    (
        "Bellevue",
        {"nom": "Résidence Bellevue", "adresse": "12 rue de Vaugirard",
         "codePostal": "75015", "ville": "Paris",
         "latitude": 48.8421, "longitude": 2.3219,
         "consignes": "Badge à récupérer à la loge. Local poubelles au sous-sol."},
        {"dateDebut": "2026-01-01",
         "frequence": "Du lundi au jeudi, matin et après-midi", "actif": True},
        {"libelle": "Nettoyage des parties communes", "dureeEstimeeMinutes": 120},
    ),
    (
        "Haussmann",
        {"nom": "Immeuble Haussmann", "adresse": "40 boulevard Haussmann",
         "codePostal": "75009", "ville": "Paris",
         "latitude": 48.8735, "longitude": 2.3320,
         "consignes": "Accès par la cour. Monte-charge réservé de 8 h à 10 h."},
        {"dateDebut": "2026-01-01",
         "frequence": "Lundi, mercredi, vendredi", "actif": True},
        {"libelle": "Remise en état des sols", "dureeEstimeeMinutes": 180},
    ),
    (
        "Daumesnil",
        {"nom": "Copropriété Daumesnil", "adresse": "210 avenue Daumesnil",
         "codePostal": "75012", "ville": "Paris",
         "latitude": 48.8402, "longitude": 2.3950,
         "consignes": "Deux halls, A et B. Vitrerie extérieure au rez-de-chaussée."},
        {"dateDebut": "2026-01-01",
         "frequence": "Lundi, mercredi, vendredi, deux passages", "actif": True},
        {"libelle": "Vitrerie et halls", "dureeEstimeeMinutes": 90},
    ),
]

# Distances croissantes depuis le barycentre des trois sites, pour que la note de
# distance du moteur d'affectation s'étale au lieu de saturer.
AGENTS = [
    ("Diallo", {"nom": "Diallo", "prenom": "Aminata",
                "email": "a.diallo@bellevue-proprete.example",
                "telephone": "06 12 34 56 78", "dateEmbauche": "2023-03-15",
                "latitude": 48.8566, "longitude": 2.3522, "actif": True}),
    ("Nguyen", {"nom": "Nguyen", "prenom": "Linh",
                "email": "l.nguyen@bellevue-proprete.example",
                "telephone": "06 23 45 67 89", "dateEmbauche": "2024-09-02",
                "latitude": 48.8760, "longitude": 2.3590, "actif": True}),
    ("Costa", {"nom": "Costa", "prenom": "Rui",
               "email": "r.costa@bellevue-proprete.example",
               "telephone": "06 34 56 78 90", "dateEmbauche": "2022-01-10",
               "latitude": 48.8156, "longitude": 2.3600, "actif": True}),
    ("Moreau", {"nom": "Moreau", "prenom": "Élodie",
                "email": "e.moreau@bellevue-proprete.example",
                "telephone": "06 45 67 89 01", "dateEmbauche": "2025-06-01",
                "latitude": 48.9360, "longitude": 2.3550, "actif": True}),
]

AGENT_ABSENT = "Moreau"

# (site, jour, heure, agent ou None)
#
# Journées vérifiées une à une contre le repos de onze heures entre journées de
# travail : le plus serré est Diallo, mercredi 16:30–18:00 puis jeudi 08:00, soit
# quatorze heures. Deux vacations d'une même journée en sont exemptées, seul le
# chevauchement les sépare (CLAUDE.md §8).
INTERVENTIONS = [
    # Bellevue — 120 min, lundi à jeudi, 08:00 et 14:00 → 8
    ("Bellevue",  "lundi",     "08:00:00", "Diallo"),
    ("Bellevue",  "lundi",     "14:00:00", "Nguyen"),
    ("Bellevue",  "mardi",     "08:00:00", "Diallo"),
    ("Bellevue",  "mardi",     "14:00:00", "Nguyen"),
    ("Bellevue",  "mercredi",  "08:00:00", "Diallo"),
    ("Bellevue",  "mercredi",  "14:00:00", "Nguyen"),
    ("Bellevue",  "jeudi",     "08:00:00", "Diallo"),
    ("Bellevue",  "jeudi",     "14:00:00", None),        # à pourvoir 1/2

    # Haussmann — 180 min, lundi, mercredi, vendredi, 08:00 → 3
    ("Haussmann", "lundi",     "08:00:00", "Costa"),
    ("Haussmann", "mercredi",  "08:00:00", "Costa"),
    ("Haussmann", "vendredi",  "08:00:00", None),        # à pourvoir 2/2

    # Daumesnil — 90 min, lundi, mercredi, vendredi, 11:00 et 16:30 → 6
    ("Daumesnil", "lundi",     "11:00:00", "Moreau"),
    ("Daumesnil", "lundi",     "16:30:00", "Moreau"),
    ("Daumesnil", "mercredi",  "11:00:00", "Moreau"),    # la seule du mercredi
    ("Daumesnil", "mercredi",  "16:30:00", "Diallo"),
    ("Daumesnil", "vendredi",  "11:00:00", "Moreau"),
    ("Daumesnil", "vendredi",  "16:30:00", "Nguyen"),
]

if __name__ == "__main__":
    main()
