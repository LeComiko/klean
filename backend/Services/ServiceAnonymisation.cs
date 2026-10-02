using Klean.Api.DTOs;
using Klean.Api.Data;
using Klean.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Klean.Api.Services;

/// <summary>
/// Pseudonymisation d'un agent : la réponse au conflit que <c>SPEC.md</c> nomme à
/// l'étape 17.
///
/// <b>Le conflit.</b> <c>Restrict</c> sur <c>Intervention.AgentId</c> rend un agent
/// impossible à supprimer dès qu'il a travaillé. C'est le bon choix pour l'intégrité du
/// planning — une suppression emporterait des vacations réalisées, donc la preuve de
/// passages facturés — et c'est incompatible avec une demande d'effacement. Remplacer
/// les données identifiantes en conservant la ligne satisfait les deux.
///
/// ⚠️ <b>Pseudonymisation et non anonymisation</b>, au sens de l'article 4(5) du RGPD.
/// L'<c>Id</c> survit, puisque les interventions en dépendent : la ligne reste donc
/// rattachable à une personne par quiconque détiendrait une correspondance externe. Le
/// registre des traitements doit l'écrire ainsi. La route s'appelle
/// <c>/anonymiser</c> parce que c'est l'intention de l'exploitant, pas parce que c'est
/// la qualification juridique.
///
/// <b>Trois endroits, pas un.</b> La personne est nommée dans <c>Agents</c>, dans
/// <c>AspNetUsers</c> si un compte lui est rattaché, et dans le <c>Motif</c> en texte
/// libre de ses absences. N'en traiter qu'un donne un effacement qui n'efface pas.
/// </summary>
public class ServiceAnonymisation
{
    /// <summary>
    /// Ce qui remplace le nom. Suivi des huit premiers caractères de l'identifiant.
    ///
    /// ⚠️ Le suffixe n'est pas une fuite : <c>InterventionDto.AgentId</c> expose déjà
    /// l'identifiant entier, et il n'a jamais été secret. Sans lui, une entreprise qui a
    /// pseudonymisé trois agents lit trois lignes identiques dans son planning et ne
    /// peut plus distinguer trois historiques distincts — ce qui n'ajoute aucune
    /// protection et retire toute lisibilité.
    /// </summary>
    public const string Pseudonyme = "Agent anonymisé";

    /// <summary>Ce qui remplace le nom d'un compte sans fiche agent (RGPD.md §9.3).</summary>
    public const string PseudonymeCompte = "Compte anonymisé";

    /// <summary>Domaine non routable, pour que l'adresse substituée ne joigne personne.</summary>
    private const string DomaineNeutre = "anonymise.invalid";

    private readonly KleanDbContext _db;
    private readonly UserManager<Utilisateur> _utilisateurs;
    private readonly ServiceSession _session;

    public ServiceAnonymisation(
        KleanDbContext db,
        UserManager<Utilisateur> utilisateurs,
        ServiceSession session)
    {
        _db = db;
        _utilisateurs = utilisateurs;
        _session = session;
    }

    public readonly record struct Resultat(AnonymisationDto? Valeur, string? Conflit)
    {
        public static Resultat Ok(AnonymisationDto valeur) => new(valeur, null);
        public static Resultat Refus(string conflit) => new(null, conflit);
    }

    public readonly record struct ResultatCompte(AnonymisationCompteDto? Valeur, string? Conflit)
    {
        public static ResultatCompte Ok(AnonymisationCompteDto valeur) => new(valeur, null);
        public static ResultatCompte Refus(string conflit) => new(null, conflit);
    }

    /// <summary>
    /// Remplace les données identifiantes de l'agent, de son compte et de ses absences.
    /// Rend <c>null</c> si l'agent n'existe pas — ou appartient à une autre entreprise,
    /// que les filtres globaux écartent avant d'arriver ici.
    /// </summary>
    public async Task<Resultat?> ExecuterAsync(Guid agentId)
    {
        var agent = await _db.Agents.FirstOrDefaultAsync(a => a.Id == agentId);

        if (agent is null) return null;

        // ⚠️ Refus et non réussite silencieuse. L'opération est irréversible : dire « déjà
        // fait » à qui s'est trompé d'identifiant vaut mieux que de lui répondre « c'est
        // bon » et de le laisser croire qu'il a traité le bon agent.
        if (agent.DateAnonymisation is not null)
        {
            return Resultat.Refus(
                $"Cet agent a déjà été anonymisé le "
                + $"{agent.DateAnonymisation.Value:dd/MM/yyyy} : il ne porte plus de donnée "
                + "identifiante, et l'opération ne peut pas être rejouée.");
        }

        // ---- Le compte de connexion, s'il existe : chargé AVANT toute mutation ----
        //
        // ⚠️ AspNetUsers n'est pas filtrée par le multi-tenant (CLAUDE.md §7) : le filtre
        // sur EntrepriseId est écrit à la main, et il est obligatoire. Sans lui, un
        // agentId deviné pourrait neutraliser le compte d'une autre entreprise.
        var compte = await _db.Users.FirstOrDefaultAsync(
            u => u.AgentId == agentId && u.EntrepriseId == agent.EntrepriseId);

        // ⚠️ Jamais le dernier administrateur actif, et c'est **ici** que la garde est
        // atteignable — pas sur la route des comptes, où elle serait morte.
        //
        // `POST /api/auth/comptes` exige un `AgentId` pour le rôle Agent mais ne
        // l'interdit à aucun autre : un compte `Administrateur` peut donc être rattaché à
        // une fiche agent. Dans une entreprise de cinq personnes où le responsable fait
        // aussi le ménage, c'est le cas normal — et anonymiser sa propre fiche agent
        // neutraliserait son compte, sans passer par le refus de s'effacer soi-même, qui
        // garde l'autre route. L'entreprise se retrouverait sans aucun compte capable
        // d'en créer un, `POST /api/auth/comptes` étant réservé à l'administrateur, et
        // sans aucun rattrapage par l'API : il faudrait intervenir en base.
        if (compte is not null && await EstLeDernierAdministrateurAsync(compte))
        {
            return Resultat.Refus(
                "La fiche de cet agent est rattachée au compte du dernier administrateur "
                + "actif de l'entreprise. L'anonymiser neutraliserait ce compte et "
                + "laisserait l'entreprise sans aucun compte capable d'en créer un autre. "
                + "Créer d'abord un second administrateur.");
        }

        var pseudonyme = $"{Pseudonyme} {agent.Id.ToString()[..8]}";

        agent.Nom = Pseudonyme;
        agent.Prenom = agent.Id.ToString()[..8];
        agent.Email = null;
        agent.Telephone = null;
        agent.Adresse = null;

        // ⚠️ `null`, jamais `0`. Une latitude de zéro passe le [Range(-90, 90)], place
        // l'agent dans le golfe de Guinée et fausse silencieusement tous les classements
        // de remplacement ; `null` donne la note de distance neutre de 50 sur 100, qui
        // est la réponse honnête à « on ne sait pas où il habite » (CLAUDE.md §8).
        agent.Latitude = null;
        agent.Longitude = null;

        // Filtre dur du moteur : il ne sera plus proposé comme remplaçant.
        agent.Actif = false;
        agent.DateAnonymisation = DateTime.UtcNow;

        // ---- Les absences : le Motif est la seule donnée en texte libre du modèle ----
        var absences = await _db.Absences.Where(a => a.AgentId == agentId).ToListAsync();
        var motifsEffaces = 0;

        foreach (var absence in absences.Where(a => a.Motif is not null))
        {
            absence.Motif = null;
            motifsEffaces++;
        }

        var interventions = await _db.Interventions.CountAsync(i => i.AgentId == agentId);

        var sessions = 0;

        if (compte is not null)
        {
            compte.Nom = Pseudonyme;
            compte.Prenom = string.Empty;
            compte.PhoneNumber = null;
            compte.PhoneNumberConfirmed = false;
            compte.EmailConfirmed = false;
            compte.Actif = false;

            // La même colonne que celle du chemin des comptes sans fiche agent
            // (RGPD.md §9.3) : le registre doit pouvoir répondre « quand », quel que
            // soit le chemin qui a exécuté l'effacement.
            compte.DateAnonymisation = DateTime.UtcNow;

            // Identity impose l'unicité de l'adresse et exige un UserName non vide : on
            // substitue plutôt que d'effacer, sur un domaine réservé non routable.
            var adresse = $"{agentId}@{DomaineNeutre}";
            compte.Email = adresse;
            compte.NormalizedEmail = adresse.ToUpperInvariant();
            compte.UserName = adresse;
            compte.NormalizedUserName = adresse.ToUpperInvariant();

            // Le jeton d'accès est signé, donc rien ne le rappelle : changer l'empreinte
            // de sécurité est ce qui le périme, parce qu'elle est vérifiée à chaque
            // requête (CLAUDE.md §6). `Actif = false` seul laisserait quinze minutes.
            await _utilisateurs.UpdateSecurityStampAsync(compte);

            sessions = await _session.RevoquerToutAsync(compte.Id, "Anonymisation de l'agent");
        }

        await _db.SaveChangesAsync();

        return Resultat.Ok(new AnonymisationDto
        {
            AgentId = agentId,
            DateAnonymisation = agent.DateAnonymisation.Value,
            Pseudonyme = pseudonyme,
            InterventionsConservees = interventions,
            AbsencesConservees = absences.Count,
            MotifsEffaces = motifsEffaces,
            CompteNeutralise = compte is not null,
            SessionsRevoquees = sessions,
            Subsiste = Subsiste(interventions, absences)
        });
    }

    /// <summary>
    /// Pseudonymise un <b>compte de connexion sans fiche agent</b> — `Responsable`,
    /// `Administrateur`, `ClientSyndic` (RGPD.md §9.3).
    ///
    /// Rend <c>null</c> si le compte n'existe pas, ou appartient à une autre entreprise.
    ///
    /// ⚠️ <b>Le filtre sur <c>EntrepriseId</c> est écrit à la main, et il est la seule
    /// garde de cadrage.</b> <c>AspNetUsers</c> n'est pas couverte par les filtres
    /// globaux (CLAUDE.md §7) : sans ce filtre, un identifiant deviné neutraliserait le
    /// compte d'une autre entreprise — et contrairement au chemin de l'agent, il n'y a
    /// ici <b>aucune</b> validation en amont qui rattraperait l'oubli.
    /// </summary>
    /// <param name="utilisateurId">Le compte à traiter.</param>
    /// <param name="entrepriseId">L'entreprise de l'appelant, tirée de son jeton.</param>
    /// <param name="appelantId">L'appelant, pour refuser qu'il s'efface lui-même.</param>
    public async Task<ResultatCompte?> ExecuterSurCompteAsync(
        Guid utilisateurId, Guid entrepriseId, Guid appelantId)
    {
        var compte = await _db.Users.FirstOrDefaultAsync(
            u => u.Id == utilisateurId && u.EntrepriseId == entrepriseId);

        if (compte is null) return null;

        // ⚠️ Refus et non réussite silencieuse, comme sur l'agent : l'opération est
        // irréversible, et dire « déjà fait » à qui s'est trompé d'identifiant vaut mieux
        // que de le laisser croire qu'il a traité le bon compte.
        if (compte.DateAnonymisation is not null)
        {
            return ResultatCompte.Refus(
                $"Ce compte a déjà été anonymisé le "
                + $"{compte.DateAnonymisation.Value:dd/MM/yyyy} : il ne porte plus de "
                + "donnée identifiante, et l'opération ne peut pas être rejouée.");
        }

        // ⚠️ Un seul chemin par cas, et le refus nomme la sortie. Un compte rattaché à un
        // agent doit passer par l'agent : lui seul traite les trois endroits où la
        // personne est nommée — la fiche, le compte et les motifs d'absence. Le traiter
        // ici donnerait un effacement qui laisse la fiche agent intacte, c'est-à-dire un
        // effacement qui n'efface pas.
        if (compte.AgentId is { } agentId)
        {
            return ResultatCompte.Refus(
                "Ce compte est rattaché à un agent. Passer par "
                + $"POST /api/agents/{agentId}/anonymiser, qui traite la fiche de l'agent, "
                + "ce compte et les motifs de ses absences en une seule opération. Le "
                + "faire ici laisserait la fiche agent intacte.");
        }

        // ⚠️ Pas soi-même. L'opération révoque les sessions et périme l'empreinte de
        // sécurité : l'appelant serait déconnecté au milieu de son propre geste, sans
        // savoir s'il a abouti. Et sur une opération irréversible, c'est le pire moment
        // pour douter.
        if (compte.Id == appelantId)
        {
            return ResultatCompte.Refus(
                "Un administrateur ne peut pas anonymiser son propre compte : "
                + "l'opération le déconnecterait au milieu de son exécution. Demander à "
                + "un autre administrateur de l'entreprise.");
        }

        // ⚠️ Pas de garde « dernier administrateur » ici, et ce n'est pas un oubli :
        // elle serait **inatteignable**. L'appelant est nécessairement un administrateur
        // actif — le rôle garde la route, `OnTokenValidated` garde l'activité — donc si
        // la cible est un autre administrateur, ils sont deux. La garde ne pourrait se
        // déclencher que pour cible = appelant, cas que le refus ci-dessus traite déjà.
        //
        // C'est donc le refus de s'effacer soi-même qui porte l'invariante « une
        // entreprise conserve toujours au moins un administrateur actif ». Un sabotage
        // l'a démontré : retirer une garde inatteignable ne fait rougir aucun test, parce
        // qu'il n'y a rien à faire rougir. Le chemin où le risque est réel est celui de
        // l'agent — voir `ExecuterAsync`.
        var adresse = $"{compte.Id}@{DomaineNeutre}";

        compte.Nom = PseudonymeCompte;
        compte.Prenom = compte.Id.ToString()[..8];
        compte.PhoneNumber = null;
        compte.PhoneNumberConfirmed = false;
        compte.EmailConfirmed = false;
        compte.Actif = false;
        compte.DateAnonymisation = DateTime.UtcNow;

        // Identity impose l'unicité de l'adresse et exige un UserName non vide : on
        // substitue plutôt que d'effacer, sur un domaine réservé non routable.
        compte.Email = adresse;
        compte.NormalizedEmail = adresse.ToUpperInvariant();
        compte.UserName = adresse;
        compte.NormalizedUserName = adresse.ToUpperInvariant();

        // Les deux gestes sont nécessaires, et aucun ne couvre l'autre (CLAUDE.md §6) :
        // l'empreinte périme les jetons d'accès déjà signés, la révocation tue les
        // sessions de rafraîchissement.
        await _utilisateurs.UpdateSecurityStampAsync(compte);

        var sessions = await _session.RevoquerToutAsync(compte.Id, "Anonymisation du compte");

        await _db.SaveChangesAsync();

        return ResultatCompte.Ok(new AnonymisationCompteDto
        {
            UtilisateurId = compte.Id,
            DateAnonymisation = compte.DateAnonymisation.Value,
            AdresseSubstituee = adresse,
            SessionsRevoquees = sessions,
            Subsiste = await SubsisteCompteAsync(compte)
        });
    }

    /// <summary>
    /// Vrai si ce compte est le dernier administrateur <b>actif et non anonymisé</b> de
    /// son entreprise.
    ///
    /// ⚠️ Les deux conditions comptent. Un administrateur désactivé ne peut pas créer de
    /// compte — <c>OnTokenValidated</c> refuse ses requêtes — donc le compter comme
    /// relève ferait croire à une sortie qui n'existe pas.
    /// </summary>
    private async Task<bool> EstLeDernierAdministrateurAsync(Utilisateur compte)
    {
        var administrateurs = await _db.UserRoles
            .Join(_db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, r.Name })
            .Where(x => x.Name == RolesKlean.Administrateur)
            .Join(_db.Users, x => x.UserId, u => u.Id, (_, u) => u)
            .Where(u => u.EntrepriseId == compte.EntrepriseId
                        && u.Actif
                        && u.DateAnonymisation == null)
            .CountAsync();

        // Si ce compte n'est pas administrateur, il n'est le dernier de personne.
        var estAdministrateur = await _db.UserRoles
            .Join(_db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, r.Name })
            .AnyAsync(x => x.UserId == compte.Id && x.Name == RolesKlean.Administrateur);

        return estAdministrateur && administrateurs <= 1;
    }

    /// <summary>
    /// Ce que la pseudonymisation d'un compte ne retire pas.
    ///
    /// ⚠️ La première ligne est la raison pour laquelle l'identifiant doit survivre, et
    /// c'est l'exact symétrique des interventions d'un agent : <c>AccesJournalise</c>
    /// attribue chaque lecture de données personnelles à un <c>UtilisateurId</c>.
    /// Supprimer le compte rendrait illisible le journal qui existe pour rendre des
    /// comptes — y compris les lignes qui documentent les accès d'autres personnes.
    /// </summary>
    private async Task<List<string>> SubsisteCompteAsync(Utilisateur compte)
    {
        var lignes = await _db.AccesJournalises.CountAsync(a => a.UtilisateurId == compte.Id);

        var reste = new List<string>
        {
            "L'identifiant technique du compte. C'est pourquoi l'opération est une "
            + "pseudonymisation au sens de l'article 4(5) du RGPD, et non une "
            + "anonymisation."
        };

        if (lignes > 0)
        {
            reste.Add(
                $"{lignes} ligne(s) du journal d'accès attribuées à ce compte, qui disent "
                + "quelles données personnelles il a consultées et quand. Elles sont "
                + "conservées douze mois et purgées par "
                + "Scripts/purge-journal-acces.sql : les supprimer maintenant retirerait "
                + "au journal la trace même qu'il existe pour conserver.");
        }

        reste.Add(
            "Le rôle du compte, qui indique la fonction occupée mais ne nomme personne. "
            + "Il rend les lignes du journal interprétables — « un administrateur a "
            + "consulté quarante fiches » — et le compte ne peut plus se connecter.");

        return reste;
    }

    /// <summary>
    /// Ce que la pseudonymisation ne retire pas, énuméré pour qui devra répondre à une
    /// demande d'effacement : il doit savoir ce qu'il peut promettre.
    /// </summary>
    private static List<string> Subsiste(int interventions, List<Absence> absences)
    {
        var reste = new List<string>
        {
            "L'identifiant technique de l'agent, dont dépendent toutes les lignes "
            + "ci-dessous. C'est pourquoi l'opération est une pseudonymisation au sens de "
            + "l'article 4(5) du RGPD, et non une anonymisation."
        };

        if (interventions > 0)
        {
            reste.Add(
                $"{interventions} intervention(s) passée(s), avec leurs dates, heures et "
                + "sites. Elles sont la preuve de passages facturés et ne peuvent pas "
                + "être supprimées sans détruire l'historique du planning.");
        }

        var sante = absences.Count(a => a.Type == TypeAbsence.Maladie);

        if (sante > 0)
        {
            // ⚠️ Le résidu le plus sensible, et il est dit en toutes lettres plutôt que
            // passé sous silence. Les motifs sont effacés, mais le type et les dates
            // restent : « absent pour maladie du 2 au 4 novembre » est une donnée de
            // santé. La supprimer demanderait d'effacer l'absence, ce qui casserait
            // l'historique des remplacements qui en découlent — c'est une question de
            // durée de conservation, pas de pseudonymisation.
            reste.Add(
                $"{sante} absence(s) de type Maladie, dont le motif a été effacé mais dont "
                + "le type et les dates subsistent — ce qui reste une donnée de santé. "
                + "Leur suppression relève des durées de conservation, pas de cette "
                + "opération.");
        }

        if (absences.Count > sante)
        {
            reste.Add(
                $"{absences.Count - sante} autre(s) absence(s), motifs effacés, type et "
                + "dates conservés.");
        }

        return reste;
    }
}
