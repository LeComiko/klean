using Klean.Api.Data;
using Klean.Api.DTOs;
using Klean.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Klean.Api.Services;

/// <summary>
/// Le besoin prévu en produits, et les deux alertes du stock (SPEC.md, étape 22, lot B).
///
/// Une lecture, et rien d'autre : la consommation ne se déduit d'aucun stock. Elle
/// attend la certification des passages ; avant elle, une déduction inventerait une
/// consommation sur des passages dont personne ne sait s'ils ont eu lieu.
/// </summary>
public class ServicePrevisionStock
{
    /// <summary>Un mois de vacations : de quoi voir venir une commande, sur un planning encore fiable.</summary>
    public const int SemainesParDefaut = 4;

    /// <summary>La limite de la génération : au-delà, il n'y a pas de vacations à compter.</summary>
    public const int SemainesMaximum = ServiceGeneration.SemainesMaximum;

    /// <summary>
    /// Ce qui reste à faire. Une vacation terminée a déjà consommé, une vacation annulée ne
    /// consommera pas.
    /// </summary>
    private static readonly StatutIntervention[] StatutsAVenir =
        [StatutIntervention.Planifiee, StatutIntervention.EnCours];

    private readonly KleanDbContext _db;
    private readonly TimeProvider _horloge;

    public ServicePrevisionStock(KleanDbContext db, TimeProvider horloge)
    {
        _db = db;
        _horloge = horloge;
    }

    /// <summary>Le jour UTC, comme la génération, et avec la même limite au petit matin.</summary>
    private DateOnly Aujourdhui => DateOnly.FromDateTime(_horloge.GetUtcNow().UtcDateTime);

    public async Task<PrevisionStockDto> CalculerAsync(int semaines, CancellationToken annulation)
    {
        var du = Aujourdhui;
        var finExclue = du.AddDays(semaines * 7);

        var debut = DateTime.SpecifyKind(du.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var fin = DateTime.SpecifyKind(finExclue.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);

        // Le nombre de passages par type et par site : c'est tout ce que la consommation lit.
        var passages = await _db.Interventions.AsNoTracking()
            .Where(i => i.DatePrevue >= debut && i.DatePrevue < fin && StatutsAVenir.Contains(i.Statut))
            .GroupBy(i => new { i.Prestation!.TypePrestationId, i.Prestation.Contrat!.SiteId })
            .Select(g => new { g.Key.TypePrestationId, g.Key.SiteId, Nombre = g.Count() })
            .ToListAsync(annulation);

        var consommations = await _db.ConsommationsType.AsNoTracking()
            .Select(c => new { c.TypePrestationId, c.ProduitId, c.QuantiteParPassage })
            .ToListAsync(annulation);

        // ⚠️ Un produit désactivé est un produit retiré : il ne se réapprovisionne plus, et
        // l'annoncer en couverture courte pousserait à racheter ce qu'on a décidé d'arrêter.
        var produits = await _db.Produits.AsNoTracking()
            .Where(p => p.Actif)
            .Select(p => new { p.Id, p.Nom, p.SeuilAlerte })
            .ToListAsync(annulation);

        var stocks = await _db.Stocks.AsNoTracking()
            .Select(s => new { s.ProduitId, s.EmplacementId, s.SiteId, s.Quantite })
            .ToListAsync(annulation);

        // Besoin par produit et par site.
        var besoins = new Dictionary<(Guid Produit, Guid Site), decimal>();

        foreach (var passage in passages.Where(p => p.TypePrestationId is not null))
        {
            foreach (var consommation in consommations.Where(c => c.TypePrestationId == passage.TypePrestationId))
            {
                var cle = (consommation.ProduitId, passage.SiteId);
                besoins[cle] = besoins.GetValueOrDefault(cle) + passage.Nombre * consommation.QuantiteParPassage;
            }
        }

        var lignes = produits.Select(produit =>
        {
            var siens = stocks.Where(s => s.ProduitId == produit.Id).ToList();
            var reserve = siens.Where(s => s.EmplacementId is not null).Sum(s => s.Quantite);

            var sites = besoins
                .Where(b => b.Key.Produit == produit.Id)
                .Select(b =>
                {
                    var surPlace = siens.Where(s => s.SiteId == b.Key.Site).Sum(s => s.Quantite);

                    return new PrevisionSiteDto
                    {
                        SiteId = b.Key.Site,
                        Besoin = b.Value,
                        Stock = surPlace,
                        AApporter = Math.Max(0, b.Value - surPlace)
                    };
                })
                .OrderByDescending(s => s.AApporter).ThenBy(s => s.SiteId)
                .ToList();

            // Chaque site puise d'abord dans ce qu'il a ; la réserve couvre le reste. Le
            // stock d'un site n'entre pas dans la réserve : il n'est pas là où sont les autres.
            var manque = Math.Max(0, sites.Sum(s => s.AApporter) - reserve);
            var total = siens.Sum(s => s.Quantite);

            return (produit.Nom, Ligne: new PrevisionProduitDto
            {
                ProduitId = produit.Id,
                SeuilAlerte = produit.SeuilAlerte,
                StockTotal = total,
                StockReserve = reserve,
                Besoin = sites.Sum(s => s.Besoin),
                Manque = manque,
                SousLeSeuil = total < produit.SeuilAlerte,
                CouvertureCourte = manque > 0,
                Sites = sites
            });
        });

        return new PrevisionStockDto
        {
            Du = debut,
            Au = fin.AddDays(-1),
            Semaines = semaines,
            VacationsSansType = passages.Where(p => p.TypePrestationId is null).Sum(p => p.Nombre),
            Produits = lignes
                .OrderByDescending(l => l.Ligne.CouvertureCourte)
                .ThenByDescending(l => l.Ligne.SousLeSeuil)
                .ThenBy(l => l.Nom, StringComparer.CurrentCulture)
                .Select(l => l.Ligne)
                .ToList()
        };
    }
}
