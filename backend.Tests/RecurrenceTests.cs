using System.Net;
using System.Net.Http.Json;
using Klean.Api.Data;
using Klean.Api.DTOs;
using Klean.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Klean.Api.Tests;

/// <summary>
/// Saisie de la récurrence d'une prestation — jours, heure, titulaire — et du
/// drapeau des jours fériés sur le contrat. C'est ce que la génération des
/// interventions lira ; ce qu'elle en fait est éprouvé ailleurs.
///
/// Les corps sont des objets anonymes : plusieurs tests y écrivent des valeurs que
/// les DTO typés ne laisseraient pas écrire.
/// </summary>
[Collection(CollectionApi.Nom)]
public class RecurrenceTests : IAsyncLifetime
{
    private readonly FixtureApi _fixture;
    private HttpClient _client = null!;

    public RecurrenceTests(FixtureApi fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await _fixture.ReinitialiserAsync();
        _client = await _fixture.ClientConnecteAsync(FixtureApi.AdminAlpha);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Recurrence_saisie_puis_relue_et_remplacee()
    {
        var contrat = await Contrat();
        var titulaire = await Agent(_client, "Titulaire");

        // Dans le désordre à l'aller, du lundi au dimanche au retour.
        var creee = await Creer<PrestationDto>("/api/prestations", new
        {
            ContratId = contrat.Id,
            Libelle = "Sols",
            DureeEstimeeMinutes = 90,
            Jours = new[] { "Vendredi", "Lundi", "Mercredi" },
            HeureDebut = "07:00:00",
            AgentTitulaireId = titulaire.Id
        });

        var relue = await _client.GetFromJsonAsync<PrestationDto>($"/api/prestations/{creee.Id}");
        Assert.Equal(["Lundi", "Mercredi", "Vendredi"], relue!.Jours);
        Assert.Equal(new TimeSpan(7, 0, 0), relue.HeureDebut);
        Assert.Equal(titulaire.Id, relue.AgentTitulaireId);

        var modification = await _client.PutAsJsonAsync($"/api/prestations/{creee.Id}", new
        {
            ContratId = contrat.Id,
            Libelle = "Sols",
            DureeEstimeeMinutes = 90,
            Jours = new[] { "Jeudi" },
            HeureDebut = "10:30:00",
            AgentTitulaireId = (Guid?)null
        });
        Assert.Equal(HttpStatusCode.NoContent, modification.StatusCode);

        var apres = await _client.GetFromJsonAsync<PrestationDto>($"/api/prestations/{creee.Id}");
        Assert.Equal(["Jeudi"], apres!.Jours);
        Assert.Equal(new TimeSpan(10, 30, 0), apres.HeureDebut);
        Assert.Null(apres.AgentTitulaireId);
    }

    /// <summary>
    /// Une prestation créée comme avant la génération — sans aucun des trois champs —
    /// le reste : rien n'est coché, rien ne sera généré, elle se planifie à la main.
    /// </summary>
    [Fact]
    public async Task Sans_recurrence_la_prestation_reste_manuelle()
    {
        var contrat = await Contrat();

        var prestation = await Creer<PrestationDto>("/api/prestations", new
        {
            ContratId = contrat.Id,
            Libelle = "Remise en état",
            DureeEstimeeMinutes = 240
        });

        Assert.Empty(prestation.Jours);
        Assert.Null(prestation.HeureDebut);
        Assert.Null(prestation.AgentTitulaireId);
    }

    /// <summary>
    /// <c>Enum.TryParse</c> seul accepterait les trois dernières : un nombre, la valeur
    /// « aucun jour » et deux jours dans une même chaîne. Aucune n'est ce que l'écran
    /// envoie, et chacune produirait une récurrence que personne n'a cochée.
    /// </summary>
    [Theory]
    [InlineData("Lundy")]
    [InlineData("lundi")]
    [InlineData("3")]
    [InlineData("Aucun")]
    [InlineData("Lundi, Mardi")]
    public async Task Jour_invalide_est_refuse_en_le_citant(string jour)
    {
        var contrat = await Contrat();

        var reponse = await _client.PostAsJsonAsync("/api/prestations", new
        {
            ContratId = contrat.Id,
            Libelle = "Sols",
            DureeEstimeeMinutes = 90,
            Jours = new[] { jour },
            HeureDebut = "07:00:00"
        });

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        var message = await reponse.Content.ReadAsStringAsync();
        Assert.Contains($"« {jour} »", message);
        Assert.Contains("Lundi, Mardi, Mercredi, Jeudi, Vendredi, Samedi, Dimanche", message);
    }

    [Fact]
    public async Task Des_jours_sans_heure_sont_refuses()
    {
        var contrat = await Contrat();

        var reponse = await _client.PostAsJsonAsync("/api/prestations", new
        {
            ContratId = contrat.Id,
            Libelle = "Sols",
            DureeEstimeeMinutes = 90,
            Jours = new[] { "Lundi" }
        });

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Contains("heure de début", await reponse.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("1.00:00:00")]
    [InlineData("-01:00:00")]
    public async Task Une_heure_hors_de_la_journee_est_refusee(string heure)
    {
        var contrat = await Contrat();

        var reponse = await _client.PostAsJsonAsync("/api/prestations", new
        {
            ContratId = contrat.Id,
            Libelle = "Sols",
            DureeEstimeeMinutes = 90,
            Jours = new[] { "Lundi" },
            HeureDebut = heure
        });

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Contains("00:00 et 23:59", await reponse.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// Le titulaire est une clé étrangère vers une table filtrée par tenant : un agent
    /// d'une autre entreprise doit être aussi inconnu qu'un identifiant inventé. Sans
    /// cela, la génération affecterait chez Alpha un salarié de Beta.
    /// </summary>
    [Fact]
    public async Task Un_titulaire_d_une_autre_entreprise_est_inconnu()
    {
        var contrat = await Contrat();
        var clientBeta = await _fixture.ClientConnecteAsync(FixtureApi.AdminBeta);
        var agentBeta = await Agent(clientBeta, "Beta");

        var reponse = await _client.PostAsJsonAsync("/api/prestations", new
        {
            ContratId = contrat.Id,
            Libelle = "Sols",
            DureeEstimeeMinutes = 90,
            Jours = new[] { "Lundi" },
            HeureDebut = "07:00:00",
            AgentTitulaireId = agentBeta.Id
        });

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Contains($"Agent {agentBeta.Id} inconnu", await reponse.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// <c>SetNull</c>, et non <c>Restrict</c> comme <c>Intervention.AgentId</c> : le
    /// titulaire est une préférence pour l'avenir, pas un fait. L'agent supprimé, la
    /// prestation reste, sans titulaire.
    /// </summary>
    [Fact]
    public async Task Supprimer_le_titulaire_ne_retire_que_le_rattachement()
    {
        var contrat = await Contrat();
        var titulaire = await Agent(_client, "Partant");

        var prestation = await Creer<PrestationDto>("/api/prestations", new
        {
            ContratId = contrat.Id,
            Libelle = "Sols",
            DureeEstimeeMinutes = 90,
            Jours = new[] { "Lundi" },
            HeureDebut = "07:00:00",
            AgentTitulaireId = titulaire.Id
        });

        var suppression = await _client.DeleteAsync($"/api/agents/{titulaire.Id}");
        Assert.Equal(HttpStatusCode.NoContent, suppression.StatusCode);

        var relue = await _client.GetFromJsonAsync<PrestationDto>($"/api/prestations/{prestation.Id}");
        Assert.Null(relue!.AgentTitulaireId);
        Assert.Equal(["Lundi"], relue.Jours);
    }

    [Fact]
    public async Task Le_contrat_porte_les_jours_feries_et_les_exclut_par_defaut()
    {
        var parDefaut = await Contrat();
        Assert.False(parDefaut.InclutJoursFeries);

        var site = await Site();
        var continu = await Creer<ContratDto>("/api/contrats", new
        {
            SiteId = site.Id,
            DateDebut = new DateTime(2026, 1, 1),
            Frequence = "Tous les jours",
            InclutJoursFeries = true
        });

        var relu = await _client.GetFromJsonAsync<ContratDto>($"/api/contrats/{continu.Id}");
        Assert.True(relu!.InclutJoursFeries);
    }

    /// <summary>
    /// Ce que la migration fait aux prestations antérieures à la colonne : elles
    /// reçoivent « Aucun », donc restent planifiées à la main.
    ///
    /// ⚠️ L'assertion qui compte porte sur la valeur <b>stockée</b>, pas sur la lecture
    /// par l'API. Sans défaut en base, la ligne recevrait la chaîne vide — et l'API la
    /// relirait quand même comme « aucun jour », la conversion d'énumération d'EF
    /// rendant la valeur par défaut au lieu de lever. Un premier état de ce test ne
    /// vérifiait que la lecture, et un sabotage du défaut est resté vert.
    /// </summary>
    [Fact]
    public async Task Une_prestation_inseree_sans_jours_est_relue_sans_jours()
    {
        var contrat = await Contrat();
        var id = Guid.NewGuid();
        var entreprise = _fixture.EntrepriseAlpha;
        string? stockee = null;

        await _fixture.AvecServicesAsync(async services =>
        {
            var db = services.GetRequiredService<KleanDbContext>();

            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Prestations" ("Id", "EntrepriseId", "ContratId", "Libelle", "DureeEstimeeMinutes")
                VALUES ({id}, {entreprise}, {contrat.Id}, 'Prestation antérieure', 60)
                """);

            stockee = await db.Database
                .SqlQuery<string>($"""SELECT "Jours" AS "Value" FROM "Prestations" WHERE "Id" = {id}""")
                .SingleAsync();
        });

        Assert.Equal("Aucun", stockee);

        var reponse = await _client.GetAsync($"/api/prestations/{id}");
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);

        var prestation = await reponse.Content.ReadFromJsonAsync<PrestationDto>();
        Assert.Empty(prestation!.Jours);
        Assert.Null(prestation.HeureDebut);
    }

    // ---- fabriques de contexte ----

    private async Task<T> Creer<T>(string route, object corps, HttpClient? client = null)
    {
        var reponse = await (client ?? _client).PostAsJsonAsync(route, corps);

        Assert.Equal(HttpStatusCode.Created, reponse.StatusCode);

        return await reponse.Content.ReadFromJsonAsync<T>()
            ?? throw new InvalidOperationException($"Réponse illisible sur {route}.");
    }

    private async Task<SiteDto> Site()
    {
        var client = await Creer<ClientDto>("/api/clients", new ClientCreateDto
        {
            Nom = "Client " + Guid.NewGuid().ToString("N")[..8],
            Type = "Copropriete"
        });

        return await Creer<SiteDto>("/api/sites", new SiteCreateDto
        {
            ClientId = client.Id,
            Nom = "Site de test",
            Adresse = "1 rue du Test",
            Latitude = 48.85,
            Longitude = 2.33
        });
    }

    private async Task<ContratDto> Contrat()
    {
        var site = await Site();

        return await Creer<ContratDto>("/api/contrats", new ContratCreateDto
        {
            SiteId = site.Id,
            DateDebut = new DateTime(2026, 1, 1),
            Frequence = "Lundi, mercredi, vendredi"
        });
    }

    private Task<AgentDto> Agent(HttpClient client, string nom) =>
        Creer<AgentDto>("/api/agents", new AgentCreateDto
        {
            Nom = nom,
            Prenom = "Test",
            DateEmbauche = new DateTime(2024, 1, 1),
            Latitude = 48.86,
            Longitude = 2.35
        }, client);
}
