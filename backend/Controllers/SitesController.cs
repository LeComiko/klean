using System.Globalization;
using System.Security.Claims;
using System.Text;
using Klean.Api.DTOs;
using Klean.Api.Data;
using Klean.Api.Models;
using Klean.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Klean.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/sites")]
public class SitesController : ControllerBase
{
    private readonly KleanDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly LimiteurDebit _debit;
    private readonly ServiceGeocodage _geocodage;

    public SitesController(KleanDbContext db, ITenantContext tenant, LimiteurDebit debit, ServiceGeocodage geocodage)
    {
        _db = db;
        _tenant = tenant;
        _debit = debit;
        _geocodage = geocodage;
    }

    // Les refus de « Localiser ». ⚠️ Recopiés par l'écran (`reference/localiser.ts`), dont la série lit
    // ce fichier : changer l'un sans l'autre la fait rougir.
    public const string RefusDesactive = "La localisation des sites est désactivée : cochez-la dans Entreprise.";
    public const string RefusAdresseVide = "Saisissez l’adresse du site avant de le localiser.";
    public const string RefusInvisible = "L’adresse, le code postal ou la ville contient un caractère invisible : retapez-les.";
    public const string RefusTropLongue = "L’adresse et la ville dépassent ensemble 200 caractères : abrégez-les.";
    public const string RefusDebut = "L’adresse doit commencer par un numéro ou une lettre, et compter au moins trois caractères.";
    public const string RefusIntrouvable = "Adresse introuvable : vérifiez-la, ou saisissez les coordonnées à la main.";
    public const string RefusInjoignable = "La Géoplateforme de l’IGN ne répond pas : réessayez plus tard, ou saisissez les coordonnées à la main.";

    /// <summary>
    /// Liste paginée, filtrable par <c>clientId</c> — la descente de niveau,
    /// « les sites de ce client ».
    ///
    /// ⚠️ Le filtre entre <b>avant</b> le comptage. Un total calculé sur la table
    /// entière annoncerait « 40 sites » au-dessus d'une liste qui en montre trois,
    /// et la pagination proposerait des pages vides.
    ///
    /// Un identifiant inconnu — ou appartenant à une autre entreprise, que les
    /// filtres globaux écartent — rend une page vide, pas un 404 : une liste filtrée
    /// sur rien reste une liste, et c'est une réponse, pas une erreur.
    /// </summary>
    [Authorize(Roles = RolesKlean.InterneEtClientSyndic)]
    [HttpGet]
    public async Task<ActionResult<PageDto<SiteDto>>> Lister(
        [FromQuery] PaginationDto pagination,
        [FromQuery] Guid? clientId)
    {
        var requete = _db.Sites.AsNoTracking();

        if (clientId is not null)
        {
            requete = requete.Where(s => s.ClientId == clientId);
        }

        var total = await requete.CountAsync();

        var sites = await requete
            .OrderBy(s => s.Nom)
            .Skip(pagination.ANegliger)
            .Take(pagination.Taille)
            .Select(s => new SiteDto
            {
                Id = s.Id,
                EntrepriseId = s.EntrepriseId,
                ClientId = s.ClientId,
                Nom = s.Nom,
                Adresse = s.Adresse,
                CodePostal = s.CodePostal,
                Ville = s.Ville,
                Latitude = s.Latitude,
                Longitude = s.Longitude,
                Consignes = s.Consignes,
                DateCreation = s.DateCreation
            })
            .ToListAsync();

        return Ok(new PageDto<SiteDto>
        {
            Elements = sites,
            Total = total,
            Page = pagination.Page,
            Taille = pagination.Taille
        });
    }

    [Authorize(Roles = RolesKlean.InterneEtClientSyndic)]
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SiteDto>> Obtenir(Guid id)
    {
        var site = await _db.Sites
            .AsNoTracking()
            .Where(s => s.Id == id)
            .Select(s => new SiteDto
            {
                Id = s.Id,
                EntrepriseId = s.EntrepriseId,
                ClientId = s.ClientId,
                Nom = s.Nom,
                Adresse = s.Adresse,
                CodePostal = s.CodePostal,
                Ville = s.Ville,
                Latitude = s.Latitude,
                Longitude = s.Longitude,
                Consignes = s.Consignes,
                DateCreation = s.DateCreation
            })
            .FirstOrDefaultAsync();

        return site is null ? NotFound() : Ok(site);
    }

    /// <summary>
    /// Localise un site par la Géoplateforme de l'IGN (0.3.0). Administrateur seul, comme l'écriture des
    /// sites ; l'adresse voyage dans le <b>corps</b> — un <c>GET ?adresse=</c> la ferait entrer dans le
    /// journal des requêtes. Rien n'est enregistré : l'écran remplit ses champs, le patron relit puis
    /// enregistre.
    /// </summary>
    [Authorize(Roles = RolesKlean.Administrateur)]
    [HttpPost("localiser")]
    public async Task<ActionResult<LocalisationDto>> Localiser(LocaliserSiteDto dto, CancellationToken annulation)
    {
        var compte = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";

        if (!_debit.Autoriser($"localisation:compte:{compte}", PlafondsDebit.LocalisationsParCompte,
                PlafondsDebit.FenetreLocalisations, out var attente))
        {
            return this.TropDeTentatives(attente, PlafondsDebit.FenetreLocalisations);
        }

        if (!_debit.Autoriser("localisation:global", PlafondsDebit.LocalisationsGlobales,
                PlafondsDebit.FenetreLocalisationsGlobales, out var attenteGlobale))
        {
            return this.TropDeTentatives(attenteGlobale, PlafondsDebit.FenetreLocalisationsGlobales);
        }

        // L'entreprise du jeton, et elle seule : sous la RLS, et cadrée sur son Id. Introuvable vaut désactivé.
        var entrepriseId = _tenant.EntrepriseId;
        var actif = entrepriseId is not null && await _db.Entreprises.AsNoTracking()
            .Where(e => e.Id == entrepriseId)
            .Select(e => e.GeocodageActif)
            .FirstOrDefaultAsync(annulation);

        if (!actif) return Conflict(RefusDesactive);

        var adresse = (dto.Adresse ?? "").Trim();
        var codePostal = (dto.CodePostal ?? "").Trim();
        var ville = (dto.Ville ?? "").Trim();

        if (adresse.Length == 0) return BadRequest(RefusAdresseVide);
        if (adresse.Length > 200) return BadRequest("L’adresse dépasse 200 caractères.");
        if (codePostal.Length > 10) return BadRequest("Le code postal dépasse 10 caractères.");
        if (ville.Length > 100) return BadRequest("La ville dépasse 100 caractères.");
        // ⚠️ Par runes, et Format avec Control : un caractère de bidirectionnalité ou d'étiquette ne se
        // voit pas, et partirait vers l'IGN. Le même filtre que `Nettoyer` du service, au retour.
        if (new[] { adresse, codePostal, ville }.Any(ContientInvisible)) return BadRequest(RefusInvisible);

        // Les bornes mesurées du service : de 3 à 200 caractères, une lettre ou un chiffre en tête.
        var recherche = ville.Length == 0 ? adresse : $"{adresse} {ville}";
        if (recherche.Length > 200) return BadRequest(RefusTropLongue);
        if (recherche.Length < 3 || !char.IsLetterOrDigit(recherche[0])) return BadRequest(RefusDebut);

        return await _geocodage.LocaliserAsync(recherche, codePostal, annulation) switch
        {
            ResultatGeocodage.Trouve trouve => Ok(new LocalisationDto
            {
                Latitude = trouve.Latitude,
                Longitude = trouve.Longitude,
                AdresseTrouvee = trouve.AdresseTrouvee,
                AVerifier = trouve.AVerifier
            }),
            ResultatGeocodage.Introuvable => BadRequest(RefusIntrouvable),
            _ => StatusCode(StatusCodes.Status503ServiceUnavailable, RefusInjoignable)
        };
    }

    private static bool ContientInvisible(string texte)
    {
        foreach (var rune in texte.EnumerateRunes())
        {
            var categorie = Rune.GetUnicodeCategory(rune);
            if (categorie is UnicodeCategory.Control or UnicodeCategory.Format) return true;
        }

        return false;
    }

    [Authorize(Roles = RolesKlean.Administrateur)]
    [HttpPost]
    public async Task<ActionResult<SiteDto>> Creer(SiteCreateDto dto)
    {
        if (!await _db.Clients.AnyAsync(c => c.Id == dto.ClientId))
        {
            return BadRequest($"Client {dto.ClientId} inconnu.");
        }

        var site = new Site
        {
            ClientId = dto.ClientId,
            Nom = dto.Nom,
            Adresse = dto.Adresse,
            CodePostal = dto.CodePostal,
            Ville = dto.Ville,
            Latitude = dto.Latitude,
            Longitude = dto.Longitude,
            Consignes = dto.Consignes
        };

        _db.Sites.Add(site);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(Obtenir), new { id = site.Id }, Vers(site));
    }

    [Authorize(Roles = RolesKlean.Administrateur)]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Modifier(Guid id, SiteCreateDto dto)
    {
        var site = await _db.Sites.FindAsync(id);
        if (site is null) return NotFound();

        if (site.ClientId != dto.ClientId && !await _db.Clients.AnyAsync(c => c.Id == dto.ClientId))
        {
            return BadRequest($"Client {dto.ClientId} inconnu.");
        }

        site.ClientId = dto.ClientId;
        site.Nom = dto.Nom;
        site.Adresse = dto.Adresse;
        site.CodePostal = dto.CodePostal;
        site.Ville = dto.Ville;
        site.Latitude = dto.Latitude;
        site.Longitude = dto.Longitude;
        site.Consignes = dto.Consignes;

        await _db.SaveChangesAsync();
        return NoContent();
    }

    [Authorize(Roles = RolesKlean.Administrateur)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Supprimer(Guid id)
    {
        var site = await _db.Sites.FindAsync(id);
        if (site is null) return NotFound();

        _db.Sites.Remove(site);

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Restrict depuis la migration SuppressionRestreinteSurLaChaineExploitation,
            // et depuis l'étape 22 sur le stock et ses mouvements. Le refus nomme ce qui
            // bloque vraiment : un site sans contrat mais avec du stock n'avait aucune
            // sortie dans l'ancien message.
            var contrats = await _db.Contrats.CountAsync(c => c.SiteId == id);

            if (contrats > 0)
            {
                return Conflict(
                    $"Ce site porte {contrats} contrat(s) et ne peut pas être supprimé : sa "
                    + "suppression effacerait aussi leurs prestations et toutes les "
                    + "interventions déjà planifiées. Supprimer d'abord ses contrats.");
            }

            // ⚠️ Pas de « vider son stock d'abord » : les mouvements continueraient de citer
            // le site, et la sortie promise ne débloquerait rien. Un site sans contrat ne
            // génère plus aucune vacation : le garder ne coûte rien.
            var mouvements = await _db.MouvementsStock.CountAsync(m =>
                m.OrigineSiteId == id || m.DestinationSiteId == id);

            return Conflict(
                $"Ce site est cité par {mouvements} mouvement(s) de stock et ne peut pas être "
                + "supprimé : l'historique des quantités en dépend. Sans contrat, il ne génère "
                + "plus aucune vacation et peut rester tel quel.");
        }

        return NoContent();
    }

    private static SiteDto Vers(Site s) => new()
    {
        Id = s.Id,
        EntrepriseId = s.EntrepriseId,
        ClientId = s.ClientId,
        Nom = s.Nom,
        Adresse = s.Adresse,
        CodePostal = s.CodePostal,
        Ville = s.Ville,
        Latitude = s.Latitude,
        Longitude = s.Longitude,
        Consignes = s.Consignes,
        DateCreation = s.DateCreation
    };
}
