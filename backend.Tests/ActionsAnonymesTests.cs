using System.Reflection;
using Klean.Api.Controllers;
using Klean.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Klean.Api.Tests;

/// <summary>
/// La surface anonyme de l'API, figée. Une route qu'on atteint sans session est une route que
/// tout Internet atteint : elle se décide, elle ne s'ajoute pas par oubli d'un attribut.
///
/// ⚠️ Aucune politique de repli n'est configurée (<c>Program.cs</c>) : une action sans
/// <c>[Authorize]</c> ni sur elle ni sur son contrôleur est <b>anonyme</b>, sans erreur ni
/// avertissement. <c>AuthController</c> et <c>SecondFacteurController</c> ne portent aucun
/// attribut de contrôleur, chaque action porte le sien : une action ajoutée là sans attribut
/// est publique. Et <c>[AllowAnonymous]</c>, où qu'il soit, l'emporte sur tout
/// <c>[Authorize]</c>.
///
/// Toute nouvelle action anonyme fait rougir le premier test : l'ajouter à la liste, avec sa
/// raison, est la décision. Le second couvre ce que la réflexion sur les contrôleurs ne voit
/// pas — les routes déclarées par <c>Map…</c> dans <c>Program.cs</c>.
/// </summary>
[Collection(CollectionApi.Nom)]
public class ActionsAnonymesTests
{
    private readonly FixtureApi _fixture;

    public ActionsAnonymesTests(FixtureApi fixture) => _fixture = fixture;

    /// <summary>Chaque action anonyme, et ce qui la garde à la place d'une session.</summary>
    private static readonly Dictionary<string, string> Attendues = new()
    {
        // La connexion, par définition sans session, et ses étapes : chacune est portée par le
        // cookie de défi de cinq minutes, chiffré et lié à l'empreinte de sécurité (§6).
        ["AuthController.Connexion"] = "on se connecte sans session ; limitation de débit et verrouillage",
        ["AuthController.PremierMotDePasse"] = "étape de la connexion, portée par le défi",
        ["SecondFacteurController.Code"] = "étape de la connexion, portée par le défi",
        ["SecondFacteurController.Preparer"] = "l'équipement dans la connexion, sur le défi à défaut de session",
        ["SecondFacteurController.Activer"] = "l'équipement dans la connexion, sur le défi à défaut de session",

        // La session elle-même.
        ["AuthController.Rafraichir"] = "le jeton d'accès vient d'expirer ; le jeton de rafraîchissement fait foi",
        ["AuthController.Deconnexion"] = "tolérante par nécessité : une session expirée efface quand même ses cookies",

        // Le mot de passe oublié : qui l'a oublié n'a pas de session.
        ["AuthController.MotDePasseOublie"] = "202 quoi qu'il arrive, limitation de débit",
        ["AuthController.Reinitialiser"] = "le jeton à usage unique du courriel fait foi",

        // L'assistant du premier lancement (étape 34, lot A2) : aucun compte n'existe encore.
        ["InstallationController.Lire"] = "ne rend que { installee }",
        ["InstallationController.Installer"] = "gardée par le code d'installation, et close dès qu'un compte existe",
    };

    /// <summary>Les routes de <c>Program.cs</c>, hors contrôleurs, ouvertes sans session.</summary>
    private static readonly Dictionary<string, string> RoutesAnonymesAttendues = new()
    {
        ["GET /api/sante"] = "la sonde de Caddy et de klean ; ne rend que l'état de la base et la version",
    };

    [Fact]
    public void Les_actions_anonymes_sont_exactement_celles_attendues()
    {
        var anonymes = typeof(ClientsController).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(controleur => controleur
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.DeclaringType == controleur && !m.IsSpecialName)
                .Where(m => !m.IsDefined(typeof(NonActionAttribute), inherit: true))
                .Where(m => EstAnonyme(controleur, m))
                .Select(m => $"{controleur.Name}.{m.Name}"))
            .Order()
            .ToArray();

        Assert.Equal(Attendues.Keys.Order().ToArray(), anonymes);
    }

    /// <summary>
    /// La réflexion ne voit que les contrôleurs : une route posée par <c>MapGet</c> lui
    /// échapperait. Lue dans les points de terminaison réels, avec leurs métadonnées
    /// d'autorisation, comme le fait l'intergiciel d'autorisation.
    /// </summary>
    [Fact]
    public void Les_routes_hors_controleur_ouvertes_sans_session_sont_celles_attendues()
    {
        var sources = _fixture.Fabrique.Services.GetRequiredService<EndpointDataSource>();

        var anonymes = sources.Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<ControllerActionDescriptor>() is null)
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is not null
                || e.Metadata.GetOrderedMetadata<IAuthorizeData>().Count == 0)
            .Select(e => $"{string.Join(",", e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["*"])} /{e.RoutePattern.RawText?.TrimStart('/')}")
            .Order()
            .ToArray();

        Assert.Equal(RoutesAnonymesAttendues.Keys.Order().ToArray(), anonymes);
    }

    /// <summary>
    /// <c>[AllowAnonymous]</c> sur l'action ou le contrôleur ; sinon, aucun <c>[Authorize]</c>
    /// à aucun niveau. Les attributs des classes de base comptent, comme pour MVC.
    /// </summary>
    private static bool EstAnonyme(Type controleur, MethodInfo action)
    {
        var attributs = action.GetCustomAttributes(inherit: true)
            .Concat(controleur.GetCustomAttributes(inherit: true))
            .ToArray();

        if (attributs.OfType<IAllowAnonymous>().Any()) return true;

        return !attributs.OfType<IAuthorizeData>().Any();
    }
}
