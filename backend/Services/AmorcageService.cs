using Klean.Api.Data;
using Klean.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Klean.Api.Services;

/// <summary>
/// Crée les rôles au démarrage, puis décide de ce que fait une base sans aucun compte
/// (SPEC.md, étape 34, lot A2) :
/// <list type="bullet">
///   <item>les trois variables d'amorçage renseignées : l'entreprise et l'administrateur sont
///   créés, par <see cref="ServiceInstallation"/>, en une transaction ;</item>
///   <item>sinon, un code d'installation configuré : l'API démarre et attend l'assistant ;</item>
///   <item>sinon, le démarrage est refusé, en nommant les deux sorties.</item>
/// </list>
/// Idempotent : sans effet sur une base qui a déjà un compte, sinon noter l'installation.
/// </summary>
public class AmorcageService
{
    private readonly KleanDbContext _db;
    private readonly RoleManager<IdentityRole<Guid>> _roles;
    private readonly IConfiguration _configuration;
    private readonly ITenantContext _tenant;
    private readonly ServiceInstallation _installation;
    private readonly CodeInstallation _code;
    private readonly EtatInstallation _etat;

    public AmorcageService(
        KleanDbContext db,
        RoleManager<IdentityRole<Guid>> roles,
        IConfiguration configuration,
        ITenantContext tenant,
        ServiceInstallation installation,
        CodeInstallation code,
        EtatInstallation etat)
    {
        _db = db;
        _roles = roles;
        _configuration = configuration;
        _tenant = tenant;
        _installation = installation;
        _code = code;
        _etat = etat;
    }

    public async Task ExecuterAsync()
    {
        // En premier, avant toute requête : l'amorçage doit créer la première
        // entreprise alors qu'aucun tenant n'existe encore, et relire les rôles
        // et les comptes sans filtre.
        _tenant.DesactiverFiltrage();

        foreach (var role in RolesKlean.Tous)
        {
            if (!await _roles.RoleExistsAsync(role))
            {
                var creation = await _roles.CreateAsync(new IdentityRole<Guid>(role));
                if (!creation.Succeeded)
                {
                    throw new InvalidOperationException(
                        $"Création du rôle « {role} » impossible : {ServiceInstallation.Messages(creation)}");
                }
            }
        }

        if (await _db.Users.AnyAsync())
        {
            _etat.Installee = true;
            return;
        }

        // Au propriétaire, toutes les entreprises se voient : une entreprise sans compte est le
        // reste d'un amorçage interrompu d'avant le lot A2, qui n'était pas transactionnel.
        var orpheline = await _db.Entreprises.AnyAsync();

        var email = _configuration["Amorcage:EmailAdmin"];
        var motDePasse = _configuration["Amorcage:MotDePasseAdmin"];
        var raisonSociale = _configuration["Amorcage:RaisonSocialeEntreprise"];
        var renseignees = new[] { email, motDePasse, raisonSociale }.Count(v => !string.IsNullOrWhiteSpace(v));

        if (renseignees == 3)
        {
            if (orpheline) throw new InvalidOperationException(EtatInstallation.MessageOrpheline);

            var resultat = await _installation.CreerAsync(new NouvelleInstallation(
                raisonSociale!, _configuration["Amorcage:SiretEntreprise"] ?? "00000000000000",
                Territoire.Metropole, email!, "Administrateur", string.Empty, motDePasse!,
                // Écrit en clair dans .env : il ne sert qu'une fois (SPEC.md, étape 24).
                MotDePasseProvisoire: true), CancellationToken.None);

            if (resultat.Issue != IssueInstallation.Creee)
            {
                throw new InvalidOperationException($"Amorçage impossible ({resultat.Issue}) : {resultat.Message}");
            }

            _etat.Installee = true;
            return;
        }

        if (_code.Configure)
        {
            // L'API démarre et attend l'assistant. Des variables partielles sont ignorées : c'est
            // le .env d'un serveur installé en 0.1.x, mot de passe déjà retiré.
            _etat.EntrepriseOrpheline = orpheline;
            return;
        }

        if (renseignees > 0)
        {
            throw new InvalidOperationException(
                "Amorçage incomplet : renseigner Amorcage:EmailAdmin, Amorcage:MotDePasseAdmin et "
                + "Amorcage:RaisonSocialeEntreprise, ou aucune des trois et le code d'installation. "
                + "Voir DEPLOIEMENT.md.");
        }

        throw new InvalidOperationException(
            "Aucun compte, et rien pour en créer un. Deux sorties : le code d'installation "
            + "(Installation:Code — KLEAN_CODE_INSTALLATION, posé par le script ; sudo klean code en "
            + "tire un), pour créer l'entreprise dans le navigateur ; ou les trois variables "
            + "d'amorçage (Amorcage:EmailAdmin, Amorcage:MotDePasseAdmin, "
            + "Amorcage:RaisonSocialeEntreprise). Voir DEPLOIEMENT.md.");
    }
}
