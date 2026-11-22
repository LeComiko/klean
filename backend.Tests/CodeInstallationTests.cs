using System.Security.Cryptography;
using Klean.Api.Services;
using Microsoft.Extensions.Configuration;

namespace Klean.Api.Tests;

public class CodeInstallationTests
{
    // Tiré à chaque exécution : aucune valeur littérale d'un code ne circule dans le dépôt.
    private static readonly string Code = CodeInstallation.Tirer();

    private static CodeInstallation Lire(string? valeur) =>
        CodeInstallation.Lire(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Installation:Code"] = valeur })
            .Build());

    [Fact] public void Absent_le_code_n_est_pas_configure() => Assert.False(Lire(null).Configure);
    [Fact] public void Vide_le_code_n_est_pas_configure() => Assert.False(Lire("  ").Configure);

    [Theory]
    [InlineData(19)]
    [InlineData(21)]
    public void Un_code_qui_n_a_pas_vingt_symboles_fait_refuser_le_demarrage(int longueur)
    {
        var erreur = Assert.Throws<InvalidOperationException>(() => Lire(new string('A', longueur)));
        Assert.Contains("KLEAN_CODE_INSTALLATION", erreur.Message);
    }

    [Theory]
    [InlineData('I')] [InlineData('L')] [InlineData('O')] [InlineData('0')] [InlineData('1')] [InlineData('$')] [InlineData('é')]
    public void Un_symbole_hors_de_l_alphabet_fait_refuser_le_demarrage(char intrus) =>
        Assert.Throws<InvalidOperationException>(() => Lire(intrus + Code[1..]));

    [Fact]
    public void La_saisie_est_normalisee_casse_espaces_tirets()
    {
        var code = Lire(Code);
        var groupes = string.Join("-", Code.Chunk(5).Select(g => new string(g)));
        Assert.True(code.Correspond(Code));
        Assert.True(code.Correspond(groupes.ToLowerInvariant()));
        Assert.True(code.Correspond($" {groupes.Replace('-', ' ')}\t"));
        Assert.True(code.Correspond(groupes.Replace('-', '–')));   // demi-cadratin, collé d'un traitement de texte
        Assert.True(code.Correspond(groupes.Replace('-', '‑'))); // tiret insécable
    }

    [Fact]
    public void Un_autre_code_ne_correspond_pas()
    {
        var code = Lire(Code);
        Assert.False(code.Correspond(null));
        Assert.False(code.Correspond(""));
        Assert.False(code.Correspond(Code[..19]));
        Assert.False(code.Correspond(CodeInstallation.Tirer()));
        Assert.False(code.Correspond(new string('A', 10_000)));
    }

    [Fact] public void Sans_code_configure_rien_ne_correspond() => Assert.False(Lire(null).Correspond(Code));

    [Fact]
    public void Un_code_tire_suit_l_alphabet() =>
        Assert.Matches("^[ABCDEFGHJKMNPQRSTUVWXYZ23456789]{20}$", CodeInstallation.Tirer());
}
