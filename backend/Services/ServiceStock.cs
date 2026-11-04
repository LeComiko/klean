using Klean.Api.Data;
using Klean.Api.DTOs;
using Klean.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Klean.Api.Services;

/// <summary>Le mouvement enregistré, ou le refus en français — jamais les deux.</summary>
public readonly record struct ResultatMouvement(MouvementStockDto? Mouvement, string? Refus)
{
    public static ResultatMouvement Ok(MouvementStockDto mouvement) => new(mouvement, null);
    public static ResultatMouvement Refuse(string message) => new(null, message);
}

/// <summary>
/// Les mouvements de stock, et la seule écriture d'une quantité (SPEC.md, étape 22).
///
/// Un mouvement et ses quantités s'écrivent ensemble ou pas du tout : une quantité changée
/// sans son mouvement serait exactement la quantité sans histoire que l'étape existe pour
/// supprimer.
/// </summary>
public class ServiceStock
{
    /// <summary>
    /// Premier entier du verrou consultatif. Forme à deux entiers, comme l'import du
    /// catalogue (21) : aucune clé commune avec la forme à un <c>bigint</c> de la génération.
    /// </summary>
    internal const int EspaceVerrou = 22;

    private readonly KleanDbContext _db;
    private readonly ITenantContext _tenant;

    public ServiceStock(KleanDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<ResultatMouvement> EnregistrerAsync(
        MouvementStockCreateDto demande, Guid auteurId, CancellationToken annulation)
    {
        // Les noms exacts, et rien d'autre : Enum.TryParse accepterait « 0 », c'est-à-dire
        // une entrée que personne n'a choisie.
        if (!Enum.GetNames<NatureMouvement>().Contains(demande.Nature))
        {
            return ResultatMouvement.Refuse(
                $"Nature de mouvement « {demande.Nature} » invalide. Valeurs acceptées : "
                + $"{string.Join(", ", Enum.GetNames<NatureMouvement>())}.");
        }

        var nature = Enum.Parse<NatureMouvement>(demande.Nature);
        var origine = new Lieu(demande.OrigineEmplacementId, demande.OrigineSiteId);
        var destination = new Lieu(demande.DestinationEmplacementId, demande.DestinationSiteId);

        if (RefusDeForme(nature, origine, destination, demande.Quantite) is { } forme)
        {
            return ResultatMouvement.Refuse(forme);
        }

        var produit = await _db.Produits.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == demande.ProduitId, annulation);

        if (produit is null) return ResultatMouvement.Refuse($"Produit {demande.ProduitId} inconnu.");

        // Un produit désactivé est un produit qu'on a retiré : ce qui en reste peut sortir,
        // circuler et se compter, mais on n'en fait plus entrer.
        if (nature == NatureMouvement.Entree && !produit.Actif)
        {
            return ResultatMouvement.Refuse(
                $"Le produit « {produit.Nom} » est désactivé : le réactiver avant d'en faire entrer.");
        }

        if (await RefusDuLieuAsync(origine, recoit: false, annulation) is { } refusOrigine)
        {
            return ResultatMouvement.Refuse(refusOrigine);
        }

        // Un inventaire compte ce qui est là : il ne fait rien entrer, et un véhicule en
        // cours de retrait doit pouvoir se compter avant d'être vidé.
        var recoit = nature is NatureMouvement.Entree or NatureMouvement.Transfert;

        if (await RefusDuLieuAsync(destination, recoit, annulation) is { } refusDestination)
        {
            return ResultatMouvement.Refuse(refusDestination);
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(annulation);

        // ⚠️ Deux sorties simultanées du même stock liraient chacune la quantité d'avant,
        // et la seconde écraserait la première : le stock afficherait 4 litres quand il en
        // reste 0. Le verrou les sérialise. La contrainte « quantité ≥ 0 » reste en dessous,
        // mais elle ne voit pas une écriture perdue, seulement un nombre négatif.
        await _db.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock({EspaceVerrou}, {CleDeVerrou(_tenant.EntrepriseId)})", annulation);

        var mouvement = new MouvementStock
        {
            ProduitId = produit.Id,
            Nature = nature,
            OrigineEmplacementId = origine.EmplacementId,
            OrigineSiteId = origine.SiteId,
            DestinationEmplacementId = destination.EmplacementId,
            DestinationSiteId = destination.SiteId,
            Quantite = demande.Quantite,
            DateMouvement = DateTime.UtcNow,
            AuteurId = auteurId
        };

        if (origine.Present)
        {
            var depart = await StockAsync(produit.Id, origine, creer: false, annulation);
            var disponible = depart?.Quantite ?? 0m;

            if (depart is null || disponible < demande.Quantite)
            {
                return ResultatMouvement.Refuse(
                    // L'unité est écrite au singulier — « litre », « rouleau » — et ne
                    // s'accorde pas : elle reste hors de la phrase.
                    $"Stock insuffisant au départ : {disponible:0.##} disponible(s), "
                    + $"{demande.Quantite:0.##} demandé(s) (unité : {produit.Unite}). "
                    + "Un inventaire corrige une quantité fausse.");
            }

            depart.Quantite -= demande.Quantite;
        }

        if (destination.Present)
        {
            var arrivee = (await StockAsync(produit.Id, destination, creer: true, annulation))!;

            if (nature == NatureMouvement.Inventaire)
            {
                mouvement.Ecart = demande.Quantite - arrivee.Quantite;
                arrivee.Quantite = demande.Quantite;
            }
            else
            {
                arrivee.Quantite += demande.Quantite;
            }
        }

        _db.MouvementsStock.Add(mouvement);
        await _db.SaveChangesAsync(annulation);
        await transaction.CommitAsync(annulation);

        return ResultatMouvement.Ok(Vers(mouvement));
    }

    /// <summary>
    /// Ce qui se juge sans lire la base : la quantité, et les lieux qu'exige la nature.
    /// Voir le tableau de <see cref="NatureMouvement"/>.
    /// </summary>
    private static string? RefusDeForme(NatureMouvement nature, Lieu origine, Lieu destination, decimal quantite)
    {
        if (origine.Double || destination.Double)
        {
            return "Un côté du mouvement désigne un emplacement ou un site, pas les deux.";
        }

        if (nature != NatureMouvement.Inventaire && quantite <= 0)
        {
            return "La quantité doit être positive. Un stock à zéro se constate par un inventaire.";
        }

        var (origineRequise, destinationRequise) = nature switch
        {
            NatureMouvement.Entree => (false, true),
            NatureMouvement.Sortie => (true, false),
            NatureMouvement.Transfert => (true, true),
            _ => (false, true)
        };

        if (origine.Present != origineRequise)
        {
            return origineRequise
                ? $"Un mouvement « {nature} » part d'un lieu : l'origine est requise."
                : $"Un mouvement « {nature} » n'a pas d'origine.";
        }

        if (destination.Present != destinationRequise)
        {
            return destinationRequise
                ? $"Un mouvement « {nature} » vise un lieu : la destination est requise."
                : $"Un mouvement « {nature} » n'a pas de destination.";
        }

        if (nature == NatureMouvement.Transfert && origine == destination)
        {
            return "Un transfert relie deux lieux différents.";
        }

        return null;
    }

    /// <summary>
    /// Un lieu inconnu — ou d'une autre entreprise, les filtres globaux s'appliquant — est
    /// refusé ; un emplacement désactivé ne <paramref name="recoit"/> plus rien.
    /// </summary>
    private async Task<string?> RefusDuLieuAsync(Lieu lieu, bool recoit, CancellationToken annulation)
    {
        if (lieu.EmplacementId is { } emplacementId)
        {
            var emplacement = await _db.Emplacements.AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == emplacementId, annulation);

            if (emplacement is null) return $"Emplacement {emplacementId} inconnu.";

            if (recoit && !emplacement.Actif)
            {
                return $"L'emplacement « {emplacement.Nom} » est désactivé : il ne reçoit plus rien, "
                    + "mais ce qui s'y trouve peut encore en sortir.";
            }
        }

        if (lieu.SiteId is { } siteId && !await _db.Sites.AnyAsync(s => s.Id == siteId, annulation))
        {
            return $"Site {siteId} inconnu.";
        }

        return null;
    }

    private async Task<Stock?> StockAsync(Guid produitId, Lieu lieu, bool creer, CancellationToken annulation)
    {
        var stock = await _db.Stocks.FirstOrDefaultAsync(
            s => s.ProduitId == produitId && s.EmplacementId == lieu.EmplacementId && s.SiteId == lieu.SiteId,
            annulation);

        if (stock is not null || !creer) return stock;

        stock = new Stock { ProduitId = produitId, EmplacementId = lieu.EmplacementId, SiteId = lieu.SiteId };
        _db.Stocks.Add(stock);

        return stock;
    }

    /// <summary>L'entreprise ramenée à 32 bits ; une collision ne ferait que sérialiser deux entreprises.</summary>
    internal static int CleDeVerrou(Guid? entrepriseId) =>
        BitConverter.ToInt32((entrepriseId ?? Guid.Empty).ToByteArray(), 0);

    internal static MouvementStockDto Vers(MouvementStock m) => new()
    {
        Id = m.Id,
        EntrepriseId = m.EntrepriseId,
        ProduitId = m.ProduitId,
        Nature = m.Nature.ToString(),
        OrigineEmplacementId = m.OrigineEmplacementId,
        OrigineSiteId = m.OrigineSiteId,
        DestinationEmplacementId = m.DestinationEmplacementId,
        DestinationSiteId = m.DestinationSiteId,
        Quantite = m.Quantite,
        Ecart = m.Ecart,
        DateMouvement = m.DateMouvement,
        AuteurId = m.AuteurId
    };

    /// <summary>Un côté d'un mouvement : un emplacement, un site, ou rien.</summary>
    private readonly record struct Lieu(Guid? EmplacementId, Guid? SiteId)
    {
        public bool Present => EmplacementId is not null || SiteId is not null;
        public bool Double => EmplacementId is not null && SiteId is not null;
    }
}
