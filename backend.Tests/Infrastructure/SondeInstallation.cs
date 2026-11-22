using System.Collections.Concurrent;
using System.Data.Common;
using Klean.Api.Data;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Klean.Api.Tests.Infrastructure;

/// <summary>
/// Relève, sous chaque commande d'EF, le rôle et l'entreprise de la connexion qui va
/// l'exécuter (SPEC.md, étape 34, lot A2) : ce que voit la base, et non ce que croit le code.
/// Sert à prouver que l'assistant d'installation écrit sous <c>klean_app</c>, dans son
/// entreprise, et que la requête suivante n'en garde rien.
///
/// Inactive par défaut : aucun coût pour le reste de la série. La relève part sur la même
/// connexion et dans la même transaction que la commande observée — sans quoi elle lirait le
/// réglage d'une autre connexion du pool.
/// </summary>
public sealed class SondeInstallation : DbCommandInterceptor
{
    private const string Lecture =
        $"SELECT current_user::text, coalesce(current_setting('{BasculeRoleApplicatif.Reglage}', true), '')";

    public sealed record Releve(string Commande, string Role, string Entreprise);

    private volatile bool _active;

    public bool Active
    {
        get => _active;
        set => _active = value;
    }

    public ConcurrentQueue<Releve> Releves { get; } = new();

    public void Vider() => Releves.Clear();

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand commande, CommandEventData donnees, InterceptionResult<DbDataReader> resultat)
    {
        Relever(commande);
        return resultat;
    }

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand commande, CommandEventData donnees, InterceptionResult<DbDataReader> resultat,
        CancellationToken annulation = default)
    {
        await ReleverAsync(commande, annulation);
        return resultat;
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand commande, CommandEventData donnees, InterceptionResult<int> resultat)
    {
        Relever(commande);
        return resultat;
    }

    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand commande, CommandEventData donnees, InterceptionResult<int> resultat,
        CancellationToken annulation = default)
    {
        await ReleverAsync(commande, annulation);
        return resultat;
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand commande, CommandEventData donnees, InterceptionResult<object> resultat)
    {
        Relever(commande);
        return resultat;
    }

    public override async ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand commande, CommandEventData donnees, InterceptionResult<object> resultat,
        CancellationToken annulation = default)
    {
        await ReleverAsync(commande, annulation);
        return resultat;
    }

    private void Relever(DbCommand commande)
    {
        if (!Active) return;

        using var autre = Preparer(commande);
        using var lecteur = autre.ExecuteReader();
        lecteur.Read();
        Releves.Enqueue(new Releve(commande.CommandText, lecteur.GetString(0), lecteur.GetString(1)));
    }

    private async Task ReleverAsync(DbCommand commande, CancellationToken annulation)
    {
        if (!Active) return;

        await using var autre = Preparer(commande);
        await using var lecteur = await autre.ExecuteReaderAsync(annulation);
        await lecteur.ReadAsync(annulation);
        Releves.Enqueue(new Releve(commande.CommandText, lecteur.GetString(0), lecteur.GetString(1)));
    }

    private static DbCommand Preparer(DbCommand commande)
    {
        var autre = commande.Connection!.CreateCommand();
        autre.Transaction = commande.Transaction;
        autre.CommandText = Lecture;
        return autre;
    }
}
