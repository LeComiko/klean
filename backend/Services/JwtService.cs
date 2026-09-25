using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Klean.Api.Models;
using Microsoft.IdentityModel.Tokens;

namespace Klean.Api.Services;

/// <summary>
/// Fabrique les jetons. HMAC-SHA256, 8 heures, pas de jeton de rafraîchissement
/// (décision consignée dans SPEC.md).
/// </summary>
public class JwtService
{
    public const string RevendicationEntrepriseId = "entrepriseId";
    public const string RevendicationAgentId = "agentId";
    public const string RevendicationClientId = "clientId";

    private readonly string _cle;
    private readonly string _emetteur;
    private readonly string _audience;

    public JwtService(IConfiguration configuration)
    {
        _cle = configuration["Jwt:Cle"]
            ?? throw new InvalidOperationException(
                "Clé JWT absente. Voir README.md, section Installation.");

        if (Encoding.UTF8.GetByteCount(_cle) < 32)
        {
            throw new InvalidOperationException(
                "La clé JWT doit faire au moins 32 octets pour HMAC-SHA256.");
        }

        _emetteur = configuration["Jwt:Emetteur"] ?? "klean";
        _audience = configuration["Jwt:Audience"] ?? "klean";
    }

    public static TimeSpan Duree => TimeSpan.FromHours(8);

    public SecurityKey CleDeSignature() => new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_cle));

    public string Emetteur => _emetteur;
    public string Audience => _audience;

    public string Generer(Utilisateur utilisateur, IEnumerable<string> roles)
    {
        var revendications = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, utilisateur.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(ClaimTypes.NameIdentifier, utilisateur.Id.ToString()),
            new(ClaimTypes.Email, utilisateur.Email ?? string.Empty),

            // La revendication qui porte le multi-tenant (CLAUDE.md §6).
            new(RevendicationEntrepriseId, utilisateur.EntrepriseId.ToString())
        };

        if (utilisateur.AgentId.HasValue)
        {
            revendications.Add(new Claim(RevendicationAgentId, utilisateur.AgentId.Value.ToString()));
        }

        if (utilisateur.ClientId.HasValue)
        {
            revendications.Add(new Claim(RevendicationClientId, utilisateur.ClientId.Value.ToString()));
        }

        revendications.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

        var jeton = new JwtSecurityToken(
            issuer: _emetteur,
            audience: _audience,
            claims: revendications,
            expires: DateTime.UtcNow.Add(Duree),
            signingCredentials: new SigningCredentials(CleDeSignature(), SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(jeton);
    }
}
