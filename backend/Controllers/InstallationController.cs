using System.Net.Mail;
using System.Text.RegularExpressions;
using Klean.Api.Data;
using Klean.Api.DTOs;
using Klean.Api.Models;
using Klean.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Klean.Api.Controllers;

/// <summary>
/// L'assistant d'installation (SPEC.md, étape 34, lot A2) : deux routes anonymes, gardées par le
/// code d'installation.
///
/// ⚠️ <b>Sous la RLS, jamais au-dessus.</b> Aucun <c>DesactiverFiltrage</c> — il lèverait,
/// dans une requête HTTP. L'entreprise tirée par <c>PoserEntrepriseInstallation</c> devient
/// celle de la requête.
///
/// ⚠️ <b>Le POST n'ouvre aucune session</b> : la connexion qui suit fait équiper le second
/// facteur, par le parcours de l'étape 23.
///
/// <b>L'ordre des contrôles du POST</b> : le code est évalué d'abord — sans coût, il décide du
/// plafond par adresse — ; puis ce plafond ; puis session ou compte existant, 409 ; puis un code
/// faux passe le plafond global des codes faux, 429, sinon 400 — un bon code ne le traverse
/// jamais, et un inconnu qui le garde plein ne bloque que d'autres inconnus ; enfin les champs,
/// l'orpheline, la création.
/// </summary>
[ApiController]
[Route("api/installation")]
[AllowAnonymous]
public partial class InstallationController(
    KleanDbContext db,
    ITenantContext tenant,
    ServiceInstallation installation,
    CodeInstallation code,
    EtatInstallation etat,
    LimiteurDebit debit) : ControllerBase
{
    /// <summary>Début du refus d'un code faux : l'écran s'en sert pour revenir au premier écran.</summary>
    public const string DebutRefusCode = "Code d'installation incorrect";

    private const string DejaInstallee = "KLEAN est déjà installé : connectez-vous.";

    [HttpGet]
    public async Task<ActionResult<EtatInstallationDto>> Lire(CancellationToken annulation)
    {
        if (!debit.Autoriser($"installation-lecture:adresse:{Adresse}",
                PlafondsDebit.LecturesInstallationParAdresse, PlafondsDebit.FenetreLecturesInstallation))
        {
            return TropDeTentatives();
        }

        return new EtatInstallationDto(await InstalleeAsync(annulation));
    }

    [HttpPost]
    public async Task<IActionResult> Installer(InstallationDto dto, CancellationToken annulation)
    {
        var codeJuste = code.Correspond(dto.Code);

        if (!debit.Autoriser(
                $"installation{(codeJuste ? "-bon-code" : "")}:adresse:{Adresse}",
                codeJuste ? PlafondsDebit.InstallationsBonCodeParAdresse : PlafondsDebit.InstallationsParAdresse,
                PlafondsDebit.FenetreInstallations))
        {
            return TropDeTentatives();
        }

        if (User.Identity?.IsAuthenticated == true || await InstalleeAsync(annulation)) return Conflict(DejaInstallee);

        if (!codeJuste)
        {
            return debit.Autoriser("installation:codes-faux:global", PlafondsDebit.CodesFauxGlobaux, PlafondsDebit.FenetreInstallations)
                ? BadRequest($"{DebutRefusCode}. Il s'affiche à la fin de l'installation sur le serveur, et par : sudo klean code")
                : TropDeTentatives();
        }

        var saisie = Saisie.De(dto);
        if (saisie.Refus() is { } message) return BadRequest(message);

        if (etat.EntrepriseOrpheline) return Conflict(EtatInstallation.MessageOrpheline);

        // ⚠️ Avant toute connexion tenue ouverte : le réglage de la RLS se pose à l'ouverture, et
        // CreerAsync refuse une connexion déjà ouverte. La lecture des comptes plus haut a ouvert
        // puis refermé la sienne.
        tenant.PoserEntrepriseInstallation();

        // « Installée » est posée par CreerAsync, juste après sa validation : pas ici.
        var resultat = await installation.CreerAsync(new NouvelleInstallation(
            saisie.RaisonSociale, saisie.Siret, Enum.Parse<Territoire>(saisie.Territoire),
            saisie.Email, saisie.Nom, saisie.Prenom, saisie.MotDePasse,
            // Choisi par le patron lui-même : pas provisoire.
            MotDePasseProvisoire: false), annulation);

        return resultat.Issue switch
        {
            IssueInstallation.Creee => StatusCode(StatusCodes.Status201Created),
            IssueInstallation.DejaInstallee => Conflict(DejaInstallee),
            IssueInstallation.SiretPris => Conflict(resultat.Message),
            _ => BadRequest(resultat.Message)
        };
    }

    private string Adresse => LimiteurDebit.CleDAdresse(HttpContext.Connection.RemoteIpAddress);

    /// <summary>« Installée » ne revient jamais en arrière : une fois vue, la base n'est plus interrogée.</summary>
    private async Task<bool> InstalleeAsync(CancellationToken annulation)
    {
        // AspNetUsers est hors de la RLS (CLAUDE.md §7) : sous klean_app, cette lecture voit tous les comptes.
        if (!etat.Installee && await db.Users.AnyAsync(annulation)) etat.Installee = true;
        return etat.Installee;
    }

    private ObjectResult TropDeTentatives() =>
        StatusCode(StatusCodes.Status429TooManyRequests, "Trop de tentatives. Réessayer dans quelques minutes.");

    /// <summary>La saisie, normalisée une seule fois ; <c>null</c> vaut vide.</summary>
    private sealed record Saisie(string RaisonSociale, string Siret, string Territoire, string Email,
        string Nom, string Prenom, string MotDePasse)
    {
        public static Saisie De(InstallationDto dto) => new(
            (dto.RaisonSociale ?? "").Trim(),
            new string((dto.Siret ?? "").Where(c => !char.IsWhiteSpace(c)).ToArray()),
            dto.Territoire ?? "",
            (dto.Email ?? "").Trim(),
            (dto.Nom ?? "").Trim(),
            (dto.Prenom ?? "").Trim(),
            dto.MotDePasse ?? "");

        public string? Refus()
        {
            if (RaisonSociale.Length is 0 or > 200) return "La raison sociale est obligatoire, 200 caractères au plus.";
            if (!SiretValide().IsMatch(Siret)) return "Le SIRET compte 14 chiffres.";
            // Par les noms exacts : Enum.TryParse accepterait « 0 » (CLAUDE.md §5).
            if (!Enum.GetNames<Models.Territoire>().Contains(Territoire))
                return $"Territoire inconnu. Valeurs acceptées : {string.Join(", ", Enum.GetNames<Models.Territoire>())}.";
            if (Email.Length > 256 || !MailAddress.TryCreate(Email, out var adresse) || adresse.Address != Email)
                return "L'adresse électronique de l'administrateur n'est pas valide.";
            if (Nom.Length is 0 or > 100) return "Le nom est obligatoire, 100 caractères au plus.";
            if (Prenom.Length is 0 or > 100) return "Le prénom est obligatoire, 100 caractères au plus.";
            // Le mot de passe n'est pas vérifié ici : Identity est l'autorité, et parle français (CLAUDE.md §6).
            return null;
        }
    }

    [GeneratedRegex("^[0-9]{14}$")]
    private static partial Regex SiretValide();
}
