using System.Net;
using System.Net.Mail;

namespace Klean.Api.Services;

/// <summary>
/// Envoi SMTP, <b>optionnel</b>.
///
/// <c>CLAUDE.md</c> §2 interdit toute dépendance à un service tiers obligatoire :
/// KLEAN doit tourner sans compte chez personne. L'absence de configuration SMTP
/// ne fait donc pas échouer le démarrage — elle désactive le seul parcours qui en
/// dépend, la réinitialisation de mot de passe, et le dit.
///
/// En revanche, une configuration <b>incomplète</b> échoue au démarrage. C'est la
/// distinction qui compte : « pas configuré » est un choix d'exploitation,
/// « configuré à moitié » est une erreur, et la découvrir au moment où un
/// responsable a perdu son mot de passe est le pire moment possible.
/// </summary>
public class ServiceCourrielSmtp : IServiceCourriel
{
    private readonly string? _hote;
    private readonly int _port;
    private readonly string? _expediteur;
    private readonly string? _utilisateur;
    private readonly string? _motDePasse;
    private readonly bool _tls;
    private readonly ILogger<ServiceCourrielSmtp> _journal;

    public ServiceCourrielSmtp(IConfiguration configuration, ILogger<ServiceCourrielSmtp> journal)
    {
        _journal = journal;

        _hote = Vide(configuration["Courriel:Hote"]);
        _expediteur = Vide(configuration["Courriel:Expediteur"]);
        _utilisateur = Vide(configuration["Courriel:Utilisateur"]);
        _motDePasse = Vide(configuration["Courriel:MotDePasse"]);
        _tls = configuration.GetValue("Courriel:UtiliserTls", true);

        var port = configuration["Courriel:Port"];
        _port = string.IsNullOrWhiteSpace(port) ? 587 : int.TryParse(port, out var p) ? p : -1;

        if (_hote is null)
        {
            // Pas d'exception : l'absence de SMTP est un mode de fonctionnement
            // admis. Mais elle doit être visible au démarrage, pas découverte
            // plus tard par un utilisateur bloqué.
            _journal.LogWarning(
                "SMTP non configuré : « Courriel:Hote » est absent. Les endpoints "
                + "POST /api/auth/mot-de-passe-oublie et POST /api/auth/reinitialiser "
                + "répondront 503. Pour les activer, renseigner Courriel:Hote, "
                + "Courriel:Port et Courriel:Expediteur. Voir appsettings.example.json.");

            return;
        }

        // À partir d'ici, l'exploitant a voulu activer le courriel. Une
        // configuration bancale devient donc une erreur, tout de suite.
        if (_expediteur is null)
        {
            throw new InvalidOperationException(
                "« Courriel:Hote » est renseigné mais « Courriel:Expediteur » est absent. "
                + "Un courriel sans expéditeur est refusé par tous les serveurs : autant "
                + "le savoir maintenant qu'au premier mot de passe oublié.");
        }

        if (_port is < 1 or > 65535)
        {
            throw new InvalidOperationException(
                $"« Courriel:Port » invalide : « {port} ». Attendu un entier entre 1 et 65535, "
                + "typiquement 587 pour STARTTLS ou 465 pour TLS implicite.");
        }

        if (_utilisateur is not null && _motDePasse is null)
        {
            throw new InvalidOperationException(
                "« Courriel:Utilisateur » est renseigné mais « Courriel:MotDePasse » est absent. "
                + "Une authentification SMTP à moitié configurée échoue à l'envoi, pas au "
                + "démarrage — c'est précisément ce qu'on veut éviter.");
        }

        _journal.LogInformation(
            "SMTP configuré sur {Hote}:{Port}, expéditeur {Expediteur}, TLS {Tls}.",
            _hote, _port, _expediteur, _tls);
    }

    public bool Configure => _hote is not null;

    public async Task EnvoyerAsync(string destinataire, string sujet, string corps)
    {
        if (!Configure)
        {
            throw new InvalidOperationException(
                "Envoi impossible : SMTP non configuré. L'appelant doit vérifier "
                + "Configure avant d'appeler EnvoyerAsync.");
        }

        using var client = new SmtpClient(_hote, _port) { EnableSsl = _tls };

        if (_utilisateur is not null)
        {
            client.Credentials = new NetworkCredential(_utilisateur, _motDePasse);
        }

        using var message = new MailMessage(_expediteur!, destinataire, sujet, corps);

        await client.SendMailAsync(message);

        _journal.LogInformation("Courriel « {Sujet} » envoyé à {Destinataire}.", sujet, destinataire);
    }

    private static string? Vide(string? valeur) => string.IsNullOrWhiteSpace(valeur) ? null : valeur;
}
