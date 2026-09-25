using Klean.Api.DTOs;
using Klean.Api.Data;
using Klean.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Klean.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/clients")]
public class ClientsController : ControllerBase
{
    private readonly KleanDbContext _db;

    public ClientsController(KleanDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ClientDto>>> Lister()
    {
        var clients = await _db.Clients
            .AsNoTracking()
            .OrderBy(c => c.Nom)
            .Select(c => new ClientDto
            {
                Id = c.Id,
                EntrepriseId = c.EntrepriseId,
                Nom = c.Nom,
                Type = c.Type.ToString(),
                Adresse = c.Adresse,
                CodePostal = c.CodePostal,
                Ville = c.Ville,
                Email = c.Email,
                Telephone = c.Telephone
            })
            .ToListAsync();

        return Ok(clients);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ClientDto>> Obtenir(Guid id)
    {
        var client = await _db.Clients
            .AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new ClientDto
            {
                Id = c.Id,
                EntrepriseId = c.EntrepriseId,
                Nom = c.Nom,
                Type = c.Type.ToString(),
                Adresse = c.Adresse,
                CodePostal = c.CodePostal,
                Ville = c.Ville,
                Email = c.Email,
                Telephone = c.Telephone
            })
            .FirstOrDefaultAsync();

        return client is null ? NotFound() : Ok(client);
    }

    [HttpPost]
    public async Task<ActionResult<ClientDto>> Creer(ClientCreateDto dto)
    {
        if (!Enum.TryParse<TypeClient>(dto.Type, out var type))
        {
            return BadRequest($"Type de client « {dto.Type} » invalide. Valeurs acceptées : {string.Join(", ", Enum.GetNames<TypeClient>())}.");
        }

        var client = new Client
        {
            Nom = dto.Nom,
            Type = type,
            Adresse = dto.Adresse,
            CodePostal = dto.CodePostal,
            Ville = dto.Ville,
            Email = dto.Email,
            Telephone = dto.Telephone
        };

        _db.Clients.Add(client);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(Obtenir), new { id = client.Id }, Vers(client));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Modifier(Guid id, ClientCreateDto dto)
    {
        if (!Enum.TryParse<TypeClient>(dto.Type, out var type))
        {
            return BadRequest($"Type de client « {dto.Type} » invalide. Valeurs acceptées : {string.Join(", ", Enum.GetNames<TypeClient>())}.");
        }

        var client = await _db.Clients.FindAsync(id);
        if (client is null) return NotFound();

        client.Nom = dto.Nom;
        client.Type = type;
        client.Adresse = dto.Adresse;
        client.CodePostal = dto.CodePostal;
        client.Ville = dto.Ville;
        client.Email = dto.Email;
        client.Telephone = dto.Telephone;

        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Supprimer(Guid id)
    {
        var client = await _db.Clients.FindAsync(id);
        if (client is null) return NotFound();

        _db.Clients.Remove(client);

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return Conflict("Suppression refusée par la base : des données rattachées l'empêchent.");
        }

        return NoContent();
    }

    private static ClientDto Vers(Client c) => new()
    {
        Id = c.Id,
        EntrepriseId = c.EntrepriseId,
        Nom = c.Nom,
        Type = c.Type.ToString(),
        Adresse = c.Adresse,
        CodePostal = c.CodePostal,
        Ville = c.Ville,
        Email = c.Email,
        Telephone = c.Telephone
    };
}
