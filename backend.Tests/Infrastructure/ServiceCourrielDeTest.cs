using System.Text.RegularExpressions;
using Klean.Api.Services;

namespace Klean.Api.Tests.Infrastructure;

public record CourrielEnvoye(string Destinataire, string Sujet, string Corps);

/// <summary>
/// Double de <see cref="IServiceCourriel"/> : retient les messages au lieu de les
/// envoyer.
///
/// C'est la raison pour laquelle <c>IServiceCourriel</c> est une interface alors que
/// <c>CLAUDE.md</c> §11 proscrit les interfaces de service. L'alternative — une
/// classe concrète qui « n'envoie pas vraiment » selon la configuration — ferait
/// dépendre le comportement de production d'un réglage, sur un chemin
/// d'authentification.
/// </summary>
public class ServiceCourrielDeTest : IServiceCourriel
{
    private readonly List<CourrielEnvoye> _messages = [];

    /// <summary>Modifiable : un test doit pouvoir simuler une instance sans SMTP.</summary>
    public bool Configure { get; set; } = true;

    public Task EnvoyerAsync(string destinataire, string sujet, string corps)
    {
        if (!Configure)
        {
            throw new InvalidOperationException(
                "EnvoyerAsync appelé alors que Configure est faux : l'appelant doit "
                + "vérifier Configure d'abord.");
        }

        lock (_messages) _messages.Add(new CourrielEnvoye(destinataire, sujet, corps));

        return Task.CompletedTask;
    }

    public IReadOnlyList<CourrielEnvoye> Messages
    {
        get { lock (_messages) return [.. _messages]; }
    }

    public CourrielEnvoye Dernier =>
        Messages.LastOrDefault()
        ?? throw new InvalidOperationException("Aucun courriel n'a été envoyé.");

    public void Reinitialiser()
    {
        lock (_messages) _messages.Clear();
        Configure = true;
    }

    /// <summary>
    /// Extrait le jeton du lien contenu dans le corps, en le décodant. Rejoue ce
    /// que fait un navigateur quand l'utilisateur clique.
    /// </summary>
    public static string JetonDuLien(string corps)
    {
        var trouve = Regex.Match(corps, @"jeton=([^\s&]+)");

        if (!trouve.Success)
        {
            throw new InvalidOperationException($"Aucun jeton dans le corps :\n{corps}");
        }

        return Uri.UnescapeDataString(trouve.Groups[1].Value);
    }

    public static string AdresseDuLien(string corps)
    {
        var trouve = Regex.Match(corps, @"email=([^\s&]+)");

        if (!trouve.Success)
        {
            throw new InvalidOperationException($"Aucune adresse dans le corps :\n{corps}");
        }

        return Uri.UnescapeDataString(trouve.Groups[1].Value);
    }
}
