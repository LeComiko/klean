using System.Data;
using Klean.Api.Data;
using Klean.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Klean.Api.Services;

public sealed record NouvelleInstallation(
    string RaisonSociale, string Siret, Territoire Territoire,
    string Email, string Nom, string Prenom, string MotDePasse, bool MotDePasseProvisoire);

public enum IssueInstallation { Creee, DejaInstallee, SiretPris, Refusee }

public sealed record ResultatInstallation(IssueInstallation Issue, string? Message = null);

/// <summary>
/// Crée l'entreprise et son premier administrateur, en une transaction (SPEC.md, étape 34,
/// lot A2). Commune à l'amorçage par variables — au propriétaire — et à l'assistant —
/// sous la RLS. Jamais recopiée : deux créations finiraient par diverger.
///
/// L'identifiant de l'entreprise : sous la RLS, celui que la requête a posé
/// (<see cref="ITenantContext.PoserEntrepriseInstallation"/>) — la politique d'Entreprises n'en
/// accepte pas d'autre ; au propriétaire, un neuf.
/// </summary>
public sealed class ServiceInstallation
{
    /// <summary>Espace du verrou consultatif. 21 et 22 sont pris (catalogue, stock).</summary>
    internal const int EspaceVerrou = 34;

    private readonly KleanDbContext _db;
    private readonly UserManager<Utilisateur> _utilisateurs;
    private readonly ITenantContext _tenant;
    private readonly EtatInstallation _etat;

    public ServiceInstallation(
        KleanDbContext db,
        UserManager<Utilisateur> utilisateurs,
        ITenantContext tenant,
        EtatInstallation etat)
    {
        _db = db;
        _utilisateurs = utilisateurs;
        _tenant = tenant;
        _etat = etat;
    }

    public async Task<ResultatInstallation> CreerAsync(NouvelleInstallation demande, CancellationToken annulation)
    {
        Guid entrepriseId;
        if (_tenant.FiltrageActif)
        {
            entrepriseId = _tenant.EntrepriseId
                ?? throw new InvalidOperationException("Sous la RLS, l'entreprise d'installation doit être posée avant la création.");
        }
        else
        {
            entrepriseId = Guid.NewGuid();
        }

        // Filet : le réglage de la RLS se pose à l'ouverture de la connexion (BasculeRoleApplicatif).
        // Une connexion déjà ouverte — une transaction de l'appelant — porterait celui d'avant.
        if (_db.Database.GetDbConnection().State != System.Data.ConnectionState.Closed)
        {
            throw new InvalidOperationException("Connexion déjà ouverte : le réglage de la RLS serait celui d'avant.");
        }

        // Toute sortie sans validation oublie ce qu'elle a suivi : l'entreprise et le compte
        // ajoutés resteraient « Added » dans le contexte, et le prochain SaveChanges de la même
        // portée les réécrirait hors de la transaction défaite.
        var validee = false;
        try
        {
            // Read committed, explicitement : sous repeatable read, l'instantané serait pris à la
            // première lecture — le verrou —, donc avant qu'il soit obtenu, et la relecture des
            // comptes ne verrait pas ceux de l'installation qui l'a précédée.
            await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, annulation);

            // Deux installations simultanées : la seconde attend ici, puis relit les comptes. Aucune
            // contrainte ne remplace ce verrou — rien n'interdit deux entreprises.
            await _db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({EspaceVerrou}, 0)", annulation);

            // AspNetUsers est hors de la RLS (CLAUDE.md §7) : cette lecture voit tous les comptes.
            if (await _db.Users.AnyAsync(annulation))
            {
                return new ResultatInstallation(IssueInstallation.DejaInstallee);
            }

            _db.Entreprises.Add(new Entreprise
            {
                Id = entrepriseId,
                RaisonSociale = demande.RaisonSociale,
                Siret = demande.Siret,
                Territoire = demande.Territoire,
                DateCreation = DateTime.UtcNow
            });

            try
            {
                await _db.SaveChangesAsync(annulation);
            }
            catch (DbUpdateException erreur) when (erreur.InnerException is PostgresException
                                                    { SqlState: PostgresErrorCodes.UniqueViolation } violation
                                                && violation.ConstraintName == "IX_Entreprises_Siret")
            {
                // Invisible sous la RLS, l'orpheline se trahit par l'unicité de son SIRET. La transaction
                // est avortée ; sortir sans valider la défait.
                return new ResultatInstallation(IssueInstallation.SiretPris, EtatInstallation.MessageOrpheline);
            }

            var administrateur = new Utilisateur
            {
                // Identity refuse un UserName vide : la même valeur que Email (CLAUDE.md §6).
                UserName = demande.Email,
                Email = demande.Email,
                EmailConfirmed = true,
                EntrepriseId = entrepriseId,
                Nom = demande.Nom,
                Prenom = demande.Prenom,
                DateCreation = DateTime.UtcNow,
                Actif = true,
                MotDePasseProvisoire = demande.MotDePasseProvisoire
            };

            // Sur un refus, la transaction se défait en sortant : aucune entreprise sans compte.
            var creation = await _utilisateurs.CreateAsync(administrateur, demande.MotDePasse);
            if (!creation.Succeeded) return new ResultatInstallation(IssueInstallation.Refusee, Messages(creation));

            var role = await _utilisateurs.AddToRoleAsync(administrateur, RolesKlean.Administrateur);
            if (!role.Succeeded) return new ResultatInstallation(IssueInstallation.Refusee, Messages(role));

            await transaction.CommitAsync(annulation);
            validee = true;

            // La postcondition, posée ici seulement : l'amorçage et l'assistant n'ont pas à s'en souvenir.
            _etat.Installee = true;
            return new ResultatInstallation(IssueInstallation.Creee);
        }
        finally
        {
            if (!validee) _db.ChangeTracker.Clear();
        }
    }

    public static string Messages(IdentityResult resultat) =>
        string.Join(" ; ", resultat.Errors.Select(e => e.Description));
}
