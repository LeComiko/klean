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

        // ---- Le compte de connexion, s'il existe ----
        //
        // ⚠️ AspNetUsers n'est pas filtrée par le multi-tenant (CLAUDE.md §7) : le filtre
        // sur EntrepriseId est écrit à la main, et il est obligatoire. Sans lui, un
        // agentId deviné pourrait neutraliser le compte d'une autre entreprise.
        var compte = await _db.Users.FirstOrDefaultAsync(
            u => u.AgentId == agentId && u.EntrepriseId == agent.EntrepriseId);

        var sessions = 0;

        if (compte is not null)
        {
            compte.Nom = Pseudonyme;
            compte.Prenom = string.Empty;
            compte.PhoneNumber = null;
            compte.PhoneNumberConfirmed = false;
            compte.EmailConfirmed = false;
            compte.Actif = false;

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
