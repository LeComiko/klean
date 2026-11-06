using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Klean.Api.Data;

/// <summary>
/// Le contexte des outils de migration — <c>dotnet ef</c> et le bundle <c>migrer</c> — construit
/// sans passer par <c>Program.cs</c> (SPEC.md, étape 26).
///
/// ⚠️ <b>Elle existe pour que les migrations restent au propriétaire.</b> Le contexte de
/// l'application porte <see cref="BasculeRoleApplicatif"/>, qui met chaque connexion sous
/// <c>klean_app</c> : un rôle sans droits de structure, sous lequel aucune migration ne passe,
/// et qui n'existe même pas avant la migration qui le crée. Les outils préfèrent une fabrique
/// de conception à l'hôte de l'application quand il en existe une ; celle-ci construit le
/// contexte hors injection, donc sans intercepteur et sans tenant.
///
/// La chaîne vient de l'environnement — ce que <c>docker-compose.yml</c> passe au bundle —, ou
/// des secrets utilisateur en développement, comme pour l'application. Absente, le contexte est
/// construit sans elle : la construction du bundle n'ouvre aucune connexion, et le bundle reçoit
/// la sienne à l'exécution.
/// </summary>
public sealed class FabriqueDeConception : IDesignTimeDbContextFactory<KleanDbContext>
{
    public KleanDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets(typeof(KleanDbContext).Assembly, optional: true)
            .AddEnvironmentVariables()
            .Build();

        var chaine = configuration.GetConnectionString("Klean");
        var options = new DbContextOptionsBuilder<KleanDbContext>();

        if (string.IsNullOrEmpty(chaine)) options.UseNpgsql();
        else options.UseNpgsql(chaine);

        return new KleanDbContext(options.Options);
    }
}
