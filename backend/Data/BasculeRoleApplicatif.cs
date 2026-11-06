using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace Klean.Api.Data;

/// <summary>
/// Met chaque connexion de l'API sous la sécurité au niveau des lignes (SPEC.md, étape 26).
///
/// L'API se connecte en superutilisateur dans les trois environnements, et un superutilisateur
/// ignore toute politique de RLS. À chaque ouverture, cet intercepteur pose donc l'entreprise
/// du jeton dans le réglage <see cref="Reglage"/>, puis bascule sur <see cref="Role"/> : un rôle
/// sans connexion, sans droits de structure, soumis aux politiques de la migration
/// <c>SecuriteAuNiveauDesLignes</c>. La base refuse alors elle-même ce qu'un filtre global
/// oublié laisserait passer.
///
/// ⚠️ <b>Le réglage est posé à chaque ouverture, vide sans entreprise, jamais omis.</b> Une
/// connexion revient au pool après chaque requête : l'omettre laisserait à la suivante
/// l'entreprise de la précédente, si la remise à zéro du pool venait à manquer.
///
/// ⚠️ <b>Le contournement veut dire « rester propriétaire »</b>, et rien d'autre : sous
/// <c>DesactiverFiltrage</c> — l'amorçage, la fixture de test —, ou pour un contexte construit
/// hors injection — les migrations —, la connexion revient explicitement au rôle de connexion.
/// Aucun réglage ne dit à une politique de tout laisser passer : il n'existe donc aucune valeur
/// qu'un code fautif pourrait poser pour sortir de la RLS en restant <c>klean_app</c>.
///
/// Ce n'est pas un moindre privilège contre une injection SQL, qui pourrait jouer
/// <c>RESET ROLE</c> : c'est un filet contre un filtre oublié, la menace que l'étape vise.
/// </summary>
public sealed class BasculeRoleApplicatif : DbConnectionInterceptor
{
    /// <summary>Le rôle sous lequel l'API lit et écrit. Créé par la migration.</summary>
    public const string Role = "klean_app";

    /// <summary>Le réglage de session que lisent les politiques. Préfixe à point, qu'aucune extension n'emploie.</summary>
    public const string Reglage = "klean.entreprise";

    public override void ConnectionOpened(DbConnection connexion, ConnectionEndEventData donnees)
    {
        using var commande = Commande(connexion, donnees.Context);
        commande.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connexion, ConnectionEndEventData donnees, CancellationToken annulation = default)
    {
        await using var commande = Commande(connexion, donnees.Context);
        await commande.ExecuteNonQueryAsync(annulation);
    }

    /// <summary>
    /// Une seule commande, donc un seul aller-retour : le réglage et le rôle partent ensemble.
    /// </summary>
    private static DbCommand Commande(DbConnection connexion, Microsoft.EntityFrameworkCore.DbContext? contexte)
    {
        var commande = connexion.CreateCommand();

        if (contexte is KleanDbContext klean && klean.SousLaSecuriteDesLignes)
        {
            commande.CommandText = $"SELECT set_config('{Reglage}', @entreprise, false); SET ROLE {Role};";
            commande.Parameters.Add(new NpgsqlParameter("entreprise", klean.EntrepriseDuJeton?.ToString() ?? string.Empty));
        }
        else
        {
            commande.CommandText = $"SELECT set_config('{Reglage}', '', false); RESET ROLE;";
        }

        return commande;
    }
}
