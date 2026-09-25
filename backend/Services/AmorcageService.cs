using Klean.Api.Data;
using Klean.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Klean.Api.Services;

/// <summary>
/// Crée les rôles au démarrage, puis une entreprise et un compte administrateur
/// si aucun utilisateur n'existe. Idempotent : sans effet sur une base déjà amorcée.
/// </summary>
public class AmorcageService
{
    private readonly KleanDbContext _db;
    private readonly UserManager<Utilisateur> _utilisateurs;
    private readonly RoleManager<IdentityRole<Guid>> _roles;
    private readonly IConfiguration _configuration;
    private readonly ITenantContext _tenant;

    public AmorcageService(
        KleanDbContext db,
        UserManager<Utilisateur> utilisateurs,
        RoleManager<IdentityRole<Guid>> roles,
        IConfiguration configuration,
        ITenantContext tenant)
    {
        _db = db;
        _utilisateurs = utilisateurs;
        _roles = roles;
        _configuration = configuration;
        _tenant = tenant;
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
                        $"Création du rôle « {role} » impossible : {Messages(creation)}");
                }
            }
        }

        if (await _db.Users.AnyAsync())
        {
            return;
        }

        var email = _configuration["Amorcage:EmailAdmin"];
        var motDePasse = _configuration["Amorcage:MotDePasseAdmin"];
        var raisonSociale = _configuration["Amorcage:RaisonSocialeEntreprise"];

        if (string.IsNullOrWhiteSpace(email)
            || string.IsNullOrWhiteSpace(motDePasse)
            || string.IsNullOrWhiteSpace(raisonSociale))
        {
            throw new InvalidOperationException(
                "Amorçage incomplet : renseigner Amorcage:EmailAdmin, "
                + "Amorcage:MotDePasseAdmin et Amorcage:RaisonSocialeEntreprise. "
                + "Voir README.md, section Installation.");
        }

        var entreprise = new Entreprise
        {
            RaisonSociale = raisonSociale,
            Siret = _configuration["Amorcage:SiretEntreprise"] ?? "00000000000000",
            DateCreation = DateTime.UtcNow
        };

        _db.Entreprises.Add(entreprise);
        await _db.SaveChangesAsync();

        var admin = new Utilisateur
        {
            // Identity refuse la création si UserName est vide : il doit porter
            // la même valeur que Email (CLAUDE.md §6).
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            EntrepriseId = entreprise.Id,
            Nom = "Administrateur",
            Prenom = string.Empty,
            DateCreation = DateTime.UtcNow,
            Actif = true
        };

        var resultat = await _utilisateurs.CreateAsync(admin, motDePasse);
        if (!resultat.Succeeded)
        {
            throw new InvalidOperationException(
                $"Création du compte administrateur impossible : {Messages(resultat)}");
        }

        var affectation = await _utilisateurs.AddToRoleAsync(admin, RolesKlean.Administrateur);
        if (!affectation.Succeeded)
        {
            throw new InvalidOperationException(
                $"Affectation du rôle Administrateur impossible : {Messages(affectation)}");
        }
    }

    private static string Messages(IdentityResult resultat) =>
        string.Join(" ; ", resultat.Errors.Select(e => e.Description));
}
