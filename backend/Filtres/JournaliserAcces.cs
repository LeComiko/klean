using System.Collections;
using System.Security.Claims;
using Klean.Api.DTOs;
using Klean.Api.Models;
using Klean.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Klean.Api.Filtres;

/// <summary>
/// Consigne au journal d'accès toute divulgation de données personnelles d'agent ou
/// d'absence (SPEC.md étape 17).
///
/// <b>Pourquoi un filtre et non un appel dans chaque action.</b> Huit actions
/// divulguent aujourd'hui ces données, réparties sur cinq contrôleurs, et deux d'entre
/// elles ne s'en doutent pas — la liste des candidats expose la distance au domicile,
/// le planning expose la cause « Agent absent ». Écrire l'appel à la main huit fois
/// donnerait huit occasions d'oublier le neuvième endroit. L'attribut reste visible au
/// point d'appel, et un test par réflexion vérifie qu'aucune action rendant ces DTOs ne
/// s'en passe : c'est cette invariante qui tient, pas ce commentaire.
///
/// <b>Pourquoi après l'action et non avant.</b> Le nombre de lignes divulguées n'est
/// connu qu'une fois le résultat produit, et une requête refusée ne divulgue rien.
///
/// ⚠️ <b>Aucun try/catch, et c'est la décision.</b> Si l'écriture du journal échoue,
/// l'exception remonte, la réponse n'est pas rendue et l'appelant reçoit 500 : un accès
/// qui ne peut pas être tracé ne doit pas avoir lieu, sinon il suffirait de faire
/// échouer le journal pour lire sans laisser de trace. Le coût est faible en pratique
/// parce que le journal vit dans la <b>même base</b> que les données : si l'on a pu
/// lire les agents, on peut insérer une ligne. Un échec signale une dérive de schéma ou
/// un disque plein, pas une panne passagère sans rapport.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class JournaliserAccesAttribute : Attribute, IAsyncActionFilter
{
    public JournaliserAccesAttribute(RessourceAccedee ressource, NatureAcces nature)
    {
        Ressource = ressource;
        Nature = nature;
    }

    public RessourceAccedee Ressource { get; }

    public NatureAcces Nature { get; }

    public async Task OnActionExecutionAsync(
        ActionExecutingContext contexte,
        ActionExecutionDelegate suivant)
    {
        var execute = await suivant();

        var divulgue = Divulgation(execute.Result);

        // Une requête refusée — 403, 404, 400 — ne divulgue rien, et n'a donc rien à
        // consigner ici. Journaliser les tentatives serait un autre journal, utile mais
        // différent : il noierait celui-ci sous les sondages d'un compte mal cadré.
        if (divulgue is null) return;

        var auteur = contexte.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);

        // ⚠️ Un accès qu'on ne peut pas attribuer ne doit pas réussir. Toutes les
        // actions concernées portent [Authorize], donc ce cas est un défaut, pas une
        // situation d'exploitation — et le laisser passer en écrivant Guid.Empty
        // produirait un journal qui répond « quelqu'un » à la question « qui ».
        if (!Guid.TryParse(auteur, out var utilisateurId))
        {
            throw new InvalidOperationException(
                "Accès à des données personnelles sans identifiant d'utilisateur "
                + "exploitable : la revendication NameIdentifier est absente ou illisible. "
                + "Le journal d'accès ne peut pas attribuer cette lecture.");
        }

        var journal = contexte.HttpContext.RequestServices
            .GetRequiredService<ServiceJournalAcces>();

        await journal.EnregistrerAsync(utilisateurId, Ressource, Nature, Cible(contexte), divulgue.Value);
    }

    /// <summary>
    /// La ligne visée quand la requête en désigne une, lue dans la route.
    ///
    /// Sur <c>/api/interventions/{id}/candidats</c> ce n'est pas un agent mais la
    /// vacation dont les candidats ont été listés, et c'est l'information utile : « a
    /// consulté les remplaçants possibles de cette vacation ».
    /// </summary>
    private static Guid? Cible(ActionExecutingContext contexte)
    {
        return contexte.RouteData.Values.TryGetValue("id", out var brut)
            && Guid.TryParse(brut?.ToString(), out var identifiant)
                ? identifiant
                : null;
    }

    /// <summary>
    /// Le nombre de lignes divulguées, ou <c>null</c> si rien ne l'a été.
    ///
    /// ⚠️ Sur une enveloppe de pagination, ce sont les <c>Elements</c> qui sont comptés
    /// et <b>jamais</b> <c>Total</c> : le journal dit ce qui est sorti, pas ce qui
    /// existe. Confondre les deux ferait écrire « a lu 150 agents » sur une page de 25.
    ///
    /// ⚠️ <c>public</c> pour être éprouvable, et c'est la même raison qui a fait sortir
    /// les conversions de valeurs du composant côté écran (§11) : c'est la seule partie
    /// de ce filtre qui écrit une donnée <b>fausse</b> plutôt que de ne rien écrire. Un
    /// compte erroné ne lève aucune erreur, ne casse aucun écran, et rend le journal
    /// trompeur — ce qui est pire qu'un journal absent, puisqu'il ressemble à une
    /// réponse. Plusieurs de ses branches gardent des cas qu'aucune action n'atteint
    /// aujourd'hui ; passer par l'API ne les éprouverait pas.
    /// </summary>
    public static int? Divulgation(IActionResult? resultat)
    {
        if (resultat is not ObjectResult objet) return null;

        var statut = objet.StatusCode ?? StatusCodes.Status200OK;
        if (statut is < 200 or > 299) return null;
        if (objet.Value is null) return null;

        return Compter(objet.Value);
    }

    public static int Compter(object valeur)
    {
        // Le planning n'est pas une liste : c'est un objet dont les lignes sont les
        // agents. Son total de vacations ne dit rien du nombre de personnes nommées.
        if (valeur is PlanningSemaineDto planning) return planning.Agents.Count;

        // PageDto<T>, par réflexion plutôt que par un switch sur chaque T : une
        // enveloppe d'un type non prévu retomberait sinon sur « 1 » et sous-déclarerait
        // la divulgation, ce qu'aucune erreur ne signalerait.
        var elements = valeur.GetType().GetProperty("Elements")?.GetValue(valeur);
        if (elements is IEnumerable enveloppe) return Denombrer(enveloppe);

        // ⚠️ Avant tout test de séquence : `string` implémente `IEnumerable<char>`, donc
        // un corps textuel serait compté **caractère par caractère**. Aucune action
        // journalisée ne rend de texte en 2xx aujourd'hui, mais un message de refus en
        // rend un — et le jour où l'un d'eux passerait ce filtre, le journal écrirait
        // « a lu 134 lignes » pour une phrase.
        if (valeur is string) return 1;

        if (valeur is IEnumerable liste) return Denombrer(liste);

        return 1;
    }

    private static int Denombrer(IEnumerable sequence)
    {
        var compte = 0;
        foreach (var _ in sequence) compte++;

        return compte;
    }
}
