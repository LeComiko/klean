using Microsoft.AspNetCore.Identity;

namespace Klean.Api.Services;

/// <summary>
/// Messages d'Identity en français.
///
/// Les descriptions d'erreur d'Identity traversent l'API telles quelles :
/// <c>AuthController</c> les recopie dans ses <c>BadRequest</c>. Sans cette
/// classe, un utilisateur francophone reçoit « Passwords must have at least one
/// digit » depuis une interface entièrement en français — et l'écran n'a aucun
/// moyen de traduire un texte libre venu du serveur.
///
/// Seuls les messages réellement atteignables par l'API sont surchargés. Traduire
/// les autres donnerait l'illusion d'une couverture complète tout en laissant les
/// vrais chemins non couverts silencieusement anglais.
/// </summary>
public class DescripteurErreursFrancais : IdentityErrorDescriber
{
    public override IdentityError DefaultError() => new()
    {
        Code = nameof(DefaultError),
        Description = "L'opération a échoué."
    };

    public override IdentityError DuplicateEmail(string email) => new()
    {
        Code = nameof(DuplicateEmail),
        Description = $"L'adresse « {email} » est déjà utilisée par un autre compte."
    };

    public override IdentityError DuplicateUserName(string userName) => new()
    {
        Code = nameof(DuplicateUserName),
        Description = $"L'adresse « {userName} » est déjà utilisée par un autre compte."
    };

    public override IdentityError InvalidEmail(string? email) => new()
    {
        Code = nameof(InvalidEmail),
        Description = $"L'adresse « {email} » n'est pas une adresse de courriel valide."
    };

    public override IdentityError InvalidUserName(string? userName) => new()
    {
        Code = nameof(InvalidUserName),
        Description = $"Le nom d'utilisateur « {userName} » contient des caractères refusés."
    };

    public override IdentityError InvalidToken() => new()
    {
        Code = nameof(InvalidToken),
        Description = "Ce lien est invalide ou a expiré. En demander un nouveau."
    };

    public override IdentityError PasswordMismatch() => new()
    {
        Code = nameof(PasswordMismatch),
        Description = "Mot de passe incorrect."
    };

    public override IdentityError PasswordTooShort(int length) => new()
    {
        Code = nameof(PasswordTooShort),
        Description = $"Le mot de passe doit faire au moins {length} caractères."
    };

    public override IdentityError PasswordRequiresDigit() => new()
    {
        Code = nameof(PasswordRequiresDigit),
        Description = "Le mot de passe doit contenir au moins un chiffre."
    };

    public override IdentityError PasswordRequiresLower() => new()
    {
        Code = nameof(PasswordRequiresLower),
        Description = "Le mot de passe doit contenir au moins une minuscule."
    };

    public override IdentityError PasswordRequiresUpper() => new()
    {
        Code = nameof(PasswordRequiresUpper),
        Description = "Le mot de passe doit contenir au moins une majuscule."
    };

    public override IdentityError PasswordRequiresNonAlphanumeric() => new()
    {
        Code = nameof(PasswordRequiresNonAlphanumeric),
        Description = "Le mot de passe doit contenir au moins un caractère spécial."
    };

    public override IdentityError PasswordRequiresUniqueChars(int uniqueChars) => new()
    {
        Code = nameof(PasswordRequiresUniqueChars),
        Description = $"Le mot de passe doit contenir au moins {uniqueChars} caractères différents."
    };

    public override IdentityError UserAlreadyHasPassword() => new()
    {
        Code = nameof(UserAlreadyHasPassword),
        Description = "Ce compte a déjà un mot de passe."
    };

    public override IdentityError UserAlreadyInRole(string role) => new()
    {
        Code = nameof(UserAlreadyInRole),
        Description = $"Ce compte a déjà le rôle « {role} »."
    };

    public override IdentityError UserNotInRole(string role) => new()
    {
        Code = nameof(UserNotInRole),
        Description = $"Ce compte n'a pas le rôle « {role} »."
    };

    public override IdentityError InvalidRoleName(string? role) => new()
    {
        Code = nameof(InvalidRoleName),
        Description = $"Le nom de rôle « {role} » est invalide."
    };

    public override IdentityError DuplicateRoleName(string role) => new()
    {
        Code = nameof(DuplicateRoleName),
        Description = $"Le rôle « {role} » existe déjà."
    };

    public override IdentityError ConcurrencyFailure() => new()
    {
        Code = nameof(ConcurrencyFailure),
        Description = "Le compte a été modifié entre-temps. Recommencer l'opération."
    };
}
