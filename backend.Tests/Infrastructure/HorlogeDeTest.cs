namespace Klean.Api.Tests.Infrastructure;

/// <summary>
/// Horloge que la série fixe à volonté. Sans elle, un test de génération dépendrait du
/// jour où il tourne — le défaut que le paramètre obligatoire <c>lundi</c> du planning
/// existe pour éviter.
///
/// Partagée par toute la série, comme les courriels : la fixture la libère entre chaque
/// test, et elle suit alors l'horloge réelle.
/// </summary>
public class HorlogeDeTest : TimeProvider
{
    private DateTimeOffset? _fixee;

    public void Fixer(DateOnly jour, int heure = 9) =>
        _fixee = new DateTimeOffset(jour.ToDateTime(new TimeOnly(heure, 0)), TimeSpan.Zero);

    public void Liberer() => _fixee = null;

    public override DateTimeOffset GetUtcNow() => _fixee ?? System.GetUtcNow();
}
