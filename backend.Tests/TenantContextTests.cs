using System.Security.Claims;
using Klean.Api.Services;
using Microsoft.AspNetCore.Http;

namespace Klean.Api.Tests;

public class TenantContextTests
{
    private static void Anonyme(Action<TenantContext, HttpContextAccessor> test)
    {
        var accesseur = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        try { test(new TenantContext(accesseur), accesseur); }
        finally { accesseur.HttpContext = null; }
    }

    [Fact]
    public void Une_requete_anonyme_n_a_pas_d_entreprise() => Anonyme((tenant, _) => Assert.Null(tenant.EntrepriseId));

    [Fact]
    public void L_entreprise_tiree_devient_celle_de_la_requete() => Anonyme((tenant, _) =>
    {
        var id = tenant.PoserEntrepriseInstallation();
        Assert.NotEqual(Guid.Empty, id);
        Assert.Equal(id, tenant.EntrepriseId);
        Assert.True(tenant.FiltrageActif);
    });

    [Fact]
    public void Elle_ne_se_pose_qu_une_fois() => Anonyme((tenant, _) =>
    {
        tenant.PoserEntrepriseInstallation();
        Assert.Throws<InvalidOperationException>(() => tenant.PoserEntrepriseInstallation());
    });

    [Fact]
    public void Elle_est_refusee_a_une_requete_authentifiee() => Anonyme((tenant, accesseur) =>
    {
        var revendiquee = Guid.NewGuid();
        accesseur.HttpContext!.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(JwtService.RevendicationEntrepriseId, revendiquee.ToString())], "test"));
        Assert.Throws<InvalidOperationException>(() => tenant.PoserEntrepriseInstallation());
        Assert.Equal(revendiquee, tenant.EntrepriseId);
    });

    [Fact]
    public void Une_identite_non_authentifiee_n_a_pas_d_entreprise_meme_revendiquee() => Anonyme((tenant, accesseur) =>
    {
        accesseur.HttpContext!.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(JwtService.RevendicationEntrepriseId, Guid.NewGuid().ToString())]));
        Assert.Null(tenant.EntrepriseId);

        var id = tenant.PoserEntrepriseInstallation();
        Assert.Equal(id, tenant.EntrepriseId);
    });

    [Fact]
    public void Elle_est_refusee_hors_requete()
    {
        var tenant = new TenantContext(new HttpContextAccessor());
        var erreur = Assert.Throws<InvalidOperationException>(() => tenant.PoserEntrepriseInstallation());
        Assert.Contains("requête d'installation", erreur.Message);
    }

    [Fact]
    public void DesactiverFiltrage_est_refuse_dans_une_requete_http() => Anonyme((tenant, _) =>
    {
        var erreur = Assert.Throws<InvalidOperationException>(tenant.DesactiverFiltrage);
        Assert.True(tenant.FiltrageActif);
        Assert.Contains("requête", erreur.Message);
    });

    [Fact]
    public void DesactiverFiltrage_reste_permis_hors_requete()
    {
        var tenant = new TenantContext(new HttpContextAccessor());
        tenant.DesactiverFiltrage();
        Assert.False(tenant.FiltrageActif);
    }

    private static readonly System.Text.RegularExpressions.Regex Appel = new(@"\bPoserEntrepriseInstallation\b");
    private static readonly System.Text.RegularExpressions.Regex Commentaire = new(@"^\s*//");

    /// <summary>
    /// Seul l'assistant d'installation pose une entreprise sur une requête anonyme. Lu dans les
    /// sources : un appel ajouté ailleurs demain ne lèverait rien.
    /// </summary>
    [Fact]
    public void Seul_InstallationController_pose_l_entreprise_d_installation()
    {
        var racine = new DirectoryInfo(AppContext.BaseDirectory);
        while (racine is not null && !Directory.Exists(Path.Combine(racine.FullName, "backend", "Controllers"))) racine = racine.Parent;
        Assert.NotNull(racine);

        var appelants = Directory.EnumerateFiles(Path.Combine(racine!.FullName, "backend"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && Appel.IsMatch(string.Join('\n', File.ReadAllLines(f).Where(l => !Commentaire.IsMatch(l)))))
            .Select(f => Path.GetFileName(f))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["ITenantContext.cs", "InstallationController.cs", "TenantContext.cs"], appelants);
    }
}
