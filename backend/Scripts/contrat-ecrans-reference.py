#!/usr/bin/env python3
"""Contrat des écrans de données de référence, éprouvé contre le vrai serveur.

Les sept écrans de l'étape 16 bis n'écrivent pas des corps plausibles : ils écrivent
ce que `frontend/src/reference/valeurs.ts` produit, règle par règle — un facultatif
vide à `null` et non à `""`, un nombre en nombre JSON, une date en `AAAA-MM-JJ`, un
champ masqué à `null` clé comprise. Ce script rejoue exactement ces corps.

Ce qu'il ajoute aux séries xUnit, qui couvrent déjà les mêmes règles : il passe par
**Kestrel** et non par `TestServer`, et il vise la base de développement plutôt qu'une
base vidée entre chaque test. Les deux sont utiles — voir CLAUDE.md §10, où plusieurs
défauts de surface n'existaient que sous l'un des deux hôtes.

    KLEAN_EMAIL=admin@klean.local KLEAN_MOTDEPASSE='…' \
        python3 backend/Scripts/contrat-ecrans-reference.py

Variables : KLEAN_URL (défaut http://localhost:5137), KLEAN_EMAIL, KLEAN_MOTDEPASSE.
Aucun mot de passe par défaut, et aucun écrit ici.

`KLEAN_RESTRICT=0` saute les trois vérifications de suppression : sans la migration
`SuppressionRestreinteSurLaChaineExploitation`, les six clés de la chaîne sont encore
en `CASCADE` et un refus attendu serait un 204 silencieux — qui effacerait la chaîne
au lieu de la protéger.

⚠️ Le script **défait ce qu'il a créé**, dans l'ordre inverse de la chaîne. Ce n'est
pas de la politesse : c'est la seconde moitié de la preuve. Un `Restrict` dont le
refus nomme une sortie ne vaut que si cette sortie est franchissable, et le seul moyen
de le savoir est de l'emprunter.
"""
import json, os, sys, urllib.error, urllib.request

URL = os.environ.get("KLEAN_URL", "http://localhost:5137").rstrip("/")
# La base visée porte-t-elle SuppressionRestreinteSurLaChaineExploitation ?
# Sans elle les six clés de la chaîne sont encore en CASCADE, et les deux refus
# de suppression ne sont pas éprouvables : ils seraient des 204 silencieux.
RESTRICT = os.environ.get("KLEAN_RESTRICT", "1") != "0"
EMAIL, MDP = os.environ["KLEAN_EMAIL"], os.environ["KLEAN_MOTDEPASSE"]
ok = fail = 0

class Api:
    def __init__(s, base): s.base, s.cookies = base, {}
    def _mem(s, r):
        for e in r.headers.get_all("Set-Cookie") or []:
            n, _, v = e.partition("=")
            s.cookies[n.strip()] = v.split(";", 1)[0]
    def brut(s, m, c, corps=None):
        d = None if corps is None else json.dumps(corps).encode()
        q = urllib.request.Request(s.base + c, data=d, method=m)
        if d is not None: q.add_header("Content-Type", "application/json")
        if s.cookies: q.add_header("Cookie", "; ".join(f"{n}={v}" for n, v in s.cookies.items()))
        if m not in ("GET", "HEAD") and "klean_csrf" in s.cookies:
            q.add_header("X-Klean-CSRF", s.cookies["klean_csrf"])
        try:
            with urllib.request.urlopen(q) as r:
                s._mem(r); t = r.read().decode()
                return r.status, (json.loads(t) if t else None)
        except urllib.error.HTTPError as e:
            return e.code, e.read().decode(errors="replace").strip()
    def appeler(s, m, c, corps=None):
        st, d = s.brut(m, c, corps)
        if st >= 400: sys.exit(f"✗ {m} {c} → {st}\n  {d}")
        return d

skip = 0

def verifier(libelle, condition, detail=""):
    global ok, fail
    if condition: ok += 1; print(f"  ok    {libelle}")
    else: fail += 1; print(f"  ÉCHEC {libelle}   {detail}")

def ignorer(libelle, pourquoi):
    global skip
    skip += 1
    print(f"  —     {libelle}   NON ÉPROUVÉ : {pourquoi}")

api = Api(URL)
api.appeler("POST", "/api/auth/connexion", {"email": EMAIL, "motDePasse": MDP})
print(f"→ {URL}\n")

# ---- Les corps exacts que produit corpsDe ----
print("Création par les corps de l'écran")
client = api.appeler("POST", "/api/clients", {
    "nom": "Syndic du Lot B", "type": "Syndic", "adresse": None,
    "codePostal": None, "ville": "Paris", "email": None, "telephone": None})
site = api.appeler("POST", "/api/sites", {
    "clientId": client["id"], "nom": "Résidence Lot B", "adresse": "1 rue du Test",
    "codePostal": None, "ville": "Paris", "latitude": 48.8421, "longitude": 2.3219,
    "consignes": None})
siteVoisin = api.appeler("POST", "/api/sites", {
    "clientId": client["id"], "nom": "Résidence Voisine", "adresse": "2 rue du Test",
    "codePostal": None, "ville": None, "latitude": 48.85, "longitude": 2.33, "consignes": None})

# Contrat : dateFin facultative → null, et non "" (CLAUDE.md §10).
contrat = api.appeler("POST", "/api/contrats", {
    "siteId": site["id"], "dateDebut": "2026-01-01", "dateFin": None,
    "frequence": "Lundi, mercredi, vendredi", "actif": True})
verifier("un contrat sans échéance est accepté avec dateFin à null", contrat["dateFin"] is None)
verifier("la date de début revient à minuit UTC, lisible par jourIso",
         contrat["dateDebut"].startswith("2026-01-01T00:00:00"), contrat["dateDebut"])

contratVoisin = api.appeler("POST", "/api/contrats", {
    "siteId": siteVoisin["id"], "dateDebut": "2026-01-01", "dateFin": "2026-12-31",
    "frequence": "Mardi, jeudi", "actif": True})
verifier("une date de fin renseignée revient au bon jour",
         contratVoisin["dateFin"].startswith("2026-12-31T00:00:00"), contratVoisin["dateFin"])

# Prestation : la durée part en NOMBRE JSON, pas en chaîne.
prestation = api.appeler("POST", "/api/prestations", {
    "contratId": contrat["id"], "libelle": "Nettoyage parties communes",
    "dureeEstimeeMinutes": 120})
verifier("la durée part en nombre et revient en nombre", prestation["dureeEstimeeMinutes"] == 120)
api.appeler("POST", "/api/prestations", {
    "contratId": contratVoisin["id"], "libelle": "Nettoyage vitres",
    "dureeEstimeeMinutes": 90})

# Produit : reference facultative → null, seuil décimal.
produit = api.appeler("POST", "/api/produits", {
    "nom": "Détergent sols", "reference": None, "unite": "litre", "seuilAlerte": 12.5})
verifier("un produit sans référence fournisseur est accepté", produit["reference"] is None)
verifier("un seuil décimal traverse sans arrondi", float(produit["seuilAlerte"]) == 12.5,
         produit["seuilAlerte"])

agent = api.appeler("POST", "/api/agents", {
    "nom": "Diallo", "prenom": "Aminata", "email": None, "telephone": None,
    "adresse": None, "latitude": 48.8566, "longitude": 2.3522,
    "dateEmbauche": "2024-03-15", "actif": True})
autreAgent = api.appeler("POST", "/api/agents", {
    "nom": "Nguyen", "prenom": "Linh", "email": None, "telephone": None,
    "adresse": None, "latitude": None, "longitude": None,
    "dateEmbauche": "2024-05-02", "actif": True})

# ---- Le motif d'une absence Maladie ----
print("\nLe motif d'un arrêt maladie")
statut, detail = api.brut("POST", "/api/absences", {
    "agentId": agent["id"], "type": "Maladie", "dateDebut": "2026-11-02",
    "dateFin": "2026-11-04", "motif": "Lombalgie"})
verifier("un motif sur un arrêt maladie est refusé en 400", statut == 400, f"{statut}")
verifier("le refus nomme la donnée de santé", isinstance(detail, str) and "santé" in detail)

# Le corps que le formulaire masqué produit réellement : motif à null.
maladie = api.appeler("POST", "/api/absences", {
    "agentId": agent["id"], "type": "Maladie", "dateDebut": "2026-11-02",
    "dateFin": "2026-11-04", "motif": None})
verifier("le même corps, motif à null, est accepté", maladie["motif"] is None)

# Un autre type garde son motif.
conge = api.appeler("POST", "/api/absences", {
    "agentId": agent["id"], "type": "Conge", "dateDebut": "2026-12-21",
    "dateFin": "2026-12-26", "motif": "Congé payé posé en octobre"})
verifier("un autre type d'absence conserve son motif", conge["motif"] == "Congé payé posé en octobre")

# Le PUT doit pouvoir EFFACER un motif : c'est le corps du formulaire masqué.
statut, _ = api.brut("PUT", f"/api/absences/{conge['id']}", {
    "agentId": agent["id"], "type": "Maladie", "dateDebut": "2026-12-21",
    "dateFin": "2026-12-26", "motif": None})
verifier("passer un congé motivé en maladie avec motif null est accepté", statut == 204, f"{statut}")
relue = api.appeler("GET", f"/api/absences/{conge['id']}")
verifier("le motif a bien été effacé en base, pas laissé en place", relue["motif"] is None, str(relue["motif"]))

api.appeler("POST", "/api/absences", {
    "agentId": autreAgent["id"], "type": "Retard", "dateDebut": "2026-11-02",
    "dateFin": "2026-11-02", "motif": None})

# ---- La descente de niveau ----
print("\nLa descente de niveau, les quatre filtres")
sites = api.appeler("GET", f"/api/sites?page=1&taille=25&clientId={client['id']}")
verifier("les sites du client, et le total du filtre", sites["total"] == 2 and len(sites["elements"]) == 2,
         str(sites["total"]))
contrats = api.appeler("GET", f"/api/contrats?page=1&taille=25&siteId={site['id']}")
verifier("les contrats du site", contrats["total"] == 1 and contrats["elements"][0]["id"] == contrat["id"])
prestations = api.appeler("GET", f"/api/prestations?page=1&taille=25&contratId={contrat['id']}")
verifier("les prestations du contrat",
         prestations["total"] == 1 and prestations["elements"][0]["id"] == prestation["id"])
absences = api.appeler("GET", f"/api/absences?page=1&taille=25&agentId={agent['id']}")
verifier("les absences de l'agent", absences["total"] == 2, str(absences["total"]))

vide = api.appeler("GET", f"/api/sites?page=1&taille=25&clientId={prestation['id']}")
verifier("un parent inconnu rend une page vide, pas une erreur",
         vide["total"] == 0 and vide["elements"] == [])

statut, _ = api.brut("GET", "/api/sites?page=1&taille=25&clientId=pas-un-identifiant")
verifier("un identifiant illisible est refusé en 400 plutôt qu'ignoré", statut == 400, f"{statut}")

tous = api.appeler("GET", "/api/sites?page=1&taille=25")
verifier("sans filtre la liste reste complète", tous["total"] >= 2, str(tous["total"]))

# ---- Les refus qui nomment la sortie, sur la chaîne du lot B ----
print("\nLes refus de suppression")
if RESTRICT:
    statut, detail = api.brut("DELETE", f"/api/contrats/{contrat['id']}")
    verifier("supprimer un contrat porteur d'une prestation est refusé en 409",
             statut == 409, f"{statut}")
    verifier("le refus compte ses enfants",
             isinstance(detail, str) and "1 prestation" in detail, str(detail)[:90])
    verifier("le refus nomme la sortie",
             isinstance(detail, str) and "Supprimer d'abord" in detail)

    statut, detail = api.brut("DELETE", f"/api/agents/{agent['id']}")
    verifier("supprimer un agent porteur d'absences est refusé en 409", statut == 409, f"{statut}")
    verifier("le refus de l'agent compte ses absences",
             isinstance(detail, str) and "2 absence" in detail, str(detail)[:90])
else:
    # ⚠️ Les refus sont sautés, le démontage **non** : un script qui s'arrête ici
    # laisserait une chaîne complète dans la base de développement à chaque passage.
    sans = "la base visée n'a pas la migration Restrict, les clés sont en CASCADE"
    ignorer("le refus de supprimer un contrat porteur d'une prestation", sans)
    ignorer("le refus de supprimer un agent porteur d'absences", sans)

# Une absence, elle, se supprime : Remplacement reste en cascade exprès.
# Une absence, elle, se supprime : Remplacement reste en Cascade exprès, parce
# qu'aucun écran ne liste un remplacement et qu'une absence saisie par erreur doit
# pouvoir être défaite.
statut, _ = api.brut("DELETE", f"/api/absences/{maladie['id']}")
verifier("une absence saisie par erreur se supprime bien", statut == 204, f"{statut}")

# ---- Le démontage, qui emprunte la sortie que les refus nomment ----
print("\nLe démontage, dans l'ordre inverse de la chaîne")

def supprimer(libelle, route):
    statut, detail = api.brut("DELETE", route)
    verifier(libelle, statut == 204, f"{statut} {str(detail)[:70]}")

# ⚠️ Les deux agents, pas seulement celui qui porte les absences du scénario : le
# second en porte une aussi, et l'oublier faisait échouer sa suppression en 409 —
# défaut trouvé en écrivant ce démontage, par le refus lui-même.
for porteur in (agent, autreAgent):
    restantes = api.appeler("GET", f"/api/absences?page=1&taille=200&agentId={porteur['id']}")
    for absence in restantes["elements"]:
        supprimer("une absence restante se supprime", f"/api/absences/{absence['id']}")

    supprimer("l'agent se supprime une fois ses absences parties", f"/api/agents/{porteur['id']}")
supprimer("le produit se supprime", f"/api/produits/{produit['id']}")

for contratCourant, site_ in ((contrat, site), (contratVoisin, siteVoisin)):
    for p in api.appeler(
            "GET", f"/api/prestations?page=1&taille=200&contratId={contratCourant['id']}")["elements"]:
        supprimer("la prestation se supprime", f"/api/prestations/{p['id']}")
    supprimer("le contrat se supprime une fois vidé de ses prestations",
              f"/api/contrats/{contratCourant['id']}")
    supprimer("le site se supprime une fois vidé de ses contrats", f"/api/sites/{site_['id']}")

supprimer("le client se supprime une fois vidé de ses sites", f"/api/clients/{client['id']}")

restants = api.appeler("GET", f"/api/sites?page=1&taille=25&clientId={client['id']}")
verifier("la chaîne ne laisse rien derrière elle", restants["total"] == 0, str(restants["total"]))

print(f"\n{ok} vérifications passées, {fail} en échec, {skip} non éprouvées")
sys.exit(1 if fail else 0)
