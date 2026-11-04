using System.Net;
using System.Net.Http.Json;
using Klean.Api.Data;
using Klean.Api.DTOs;
using Klean.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Klean.Api.Tests;

[Collection(CollectionApi.Nom)]
public class CrudTests : IAsyncLifetime
{
    private readonly FixtureApi _fixture;
    private HttpClient _client = null!;

    public CrudTests(FixtureApi fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await _fixture.ReinitialiserAsync();
        _client = await _fixture.ClientConnecteAsync(FixtureApi.AdminAlpha);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Chaine_complete_creee_puis_relue()
    {
        var client = await Creer<ClientDto>("/api/clients", new ClientCreateDto
        {
            Nom = "Syndic Bellevue",
            Type = "Syndic",
            Ville = "Paris"
        });
        Assert.Equal("Syndic", client.Type);

        var site = await Creer<SiteDto>("/api/sites", new SiteCreateDto
        {
            ClientId = client.Id,
            Nom = "Résidence Bellevue",
            Adresse = "12 rue de Vaugirard",
            CodePostal = "75015",
            Ville = "Paris",
            Latitude = 48.8421,
            Longitude = 2.3219,
            Consignes = "Badge à l'accueil"
        });
        Assert.Equal(48.8421, site.Latitude, 4);
        Assert.Equal("Badge à l'accueil", site.Consignes);

        var contrat = await Creer<ContratDto>("/api/contrats", new ContratCreateDto
        {
            SiteId = site.Id,
            DateDebut = new DateTime(2026, 1, 1),
            Frequence = "Lundi, mercredi, vendredi"
        });
        Assert.Null(contrat.DateFin);

        var prestation = await Creer<PrestationDto>("/api/prestations", new PrestationCreateDto
        {
            ContratId = contrat.Id,
            Libelle = "Nettoyage parties communes",
            DureeEstimeeMinutes = 120
        });
        Assert.Equal(120, prestation.DureeEstimeeMinutes);

        var agent = await Creer<AgentDto>("/api/agents", new AgentCreateDto
        {
            Nom = "Diallo",
            Prenom = "Aminata",
            DateEmbauche = new DateTime(2024, 3, 15),
            Latitude = 48.8566,
            Longitude = 2.3522
        });

        var intervention = await Creer<InterventionDto>("/api/interventions", new InterventionCreateDto
        {
            PrestationId = prestation.Id,
            AgentId = agent.Id,
            DatePrevue = new DateTime(2026, 9, 7),
            HeurePrevue = new TimeSpan(8, 30, 0),
            Statut = "Planifiee"
        });

        // Relecture : ce qui a été écrit doit revenir identique.
        var relue = await _client.GetFromJsonAsync<InterventionDto>(
            $"/api/interventions/{intervention.Id}");

        Assert.NotNull(relue);
        Assert.Equal(new TimeSpan(8, 30, 0), relue.HeurePrevue);
        Assert.Equal("Planifiee", relue.Statut);
        Assert.Equal(agent.Id, relue.AgentId);
    }

    [Fact]
    public async Task Date_recue_sans_fuseau_est_stockee_en_utc()
    {
        // Npgsql refuse un DateTime dont le Kind est Unspecified, ce que produit
        // la liaison JSON d'une date nue. La garde est le SpecifyKind des
        // contrôleurs (CLAUDE.md §10) : sans elle, cette création échoue.
        var agent = await Creer<AgentDto>("/api/agents", new AgentCreateDto
        {
            Nom = "Nguyen",
            Prenom = "Linh",
            DateEmbauche = new DateTime(2024, 3, 15)
        });

        var relu = await _client.GetFromJsonAsync<AgentDto>($"/api/agents/{agent.Id}");

        Assert.NotNull(relu);
        Assert.Equal(new DateTime(2024, 3, 15), relu.DateEmbauche.Date);
        Assert.Equal(DateTimeKind.Utc, relu.DateEmbauche.Kind);
    }

    [Fact]
    public async Task Vacation_sans_agent_est_acceptee()
    {
        var prestation = await ChainePrestation();

        var intervention = await Creer<InterventionDto>("/api/interventions", new InterventionCreateDto
        {
            PrestationId = prestation.Id,
            AgentId = null,
            DatePrevue = new DateTime(2026, 9, 8),
            HeurePrevue = new TimeSpan(14, 0, 0),
            Statut = "Planifiee"
        });

        Assert.Null(intervention.AgentId);
    }

    [Theory]
    [InlineData("/api/clients", "Bailleur", "Type de client")]
    [InlineData("/api/emplacements", "Camion", "Type d'emplacement")]
    public async Task Enum_invalide_renvoie_400_avec_les_valeurs_acceptees(
        string route, string valeurInvalide, string debutDuMessage)
    {
        object corps = route == "/api/clients"
            ? new ClientCreateDto { Nom = "X", Type = valeurInvalide }
            : new EmplacementCreateDto { Nom = "Camion 1", Type = valeurInvalide };

        var reponse = await _client.PostAsJsonAsync(route, corps);

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        var message = await reponse.Content.ReadAsStringAsync();
        Assert.Contains(debutDuMessage, message);
        Assert.Contains(valeurInvalide, message);
    }

    [Fact]
    public async Task Cle_etrangere_inconnue_renvoie_400()
    {
        var reponse = await _client.PostAsJsonAsync("/api/sites", new SiteCreateDto
        {
            ClientId = Guid.NewGuid(),
            Nom = "X",
            Adresse = "Y",
            Latitude = 48.8,
            Longitude = 2.3
        });

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Contains("inconnu", await reponse.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Contrat_avec_date_de_fin_anterieure_refuse()
    {
        var site = await ChaineSite();

        var reponse = await _client.PostAsJsonAsync("/api/contrats", new ContratCreateDto
        {
            SiteId = site.Id,
            DateDebut = new DateTime(2026, 6, 1),
            DateFin = new DateTime(2026, 1, 1),
            Frequence = "Lundi"
        });

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
    }

    [Theory]
    [InlineData("entreprises")]
    [InlineData("clients")]
    [InlineData("sites")]
    [InlineData("agents")]
    [InlineData("contrats")]
    [InlineData("prestations")]
    [InlineData("interventions")]
    [InlineData("absences")]
    [InlineData("remplacements")]
    [InlineData("produits")]
    [InlineData("stocks")]
    [InlineData("emplacements")]
    [InlineData("mouvements-stock")]
    public async Task Identifiant_inexistant_renvoie_404(string ressource)
    {
        var reponse = await _client.GetAsync($"/api/{ressource}/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, reponse.StatusCode);
    }

    [Fact]
    public async Task Suppression_agent_porteur_d_interventions_renvoie_409()
    {
        var prestation = await ChainePrestation();

        var agent = await Creer<AgentDto>("/api/agents", new AgentCreateDto
        {
            Nom = "Bloque",
            Prenom = "Agent",
            DateEmbauche = new DateTime(2024, 1, 1)
        });

        await Creer<InterventionDto>("/api/interventions", new InterventionCreateDto
        {
            PrestationId = prestation.Id,
            AgentId = agent.Id,
            DatePrevue = new DateTime(2026, 9, 7),
            HeurePrevue = new TimeSpan(8, 0, 0),
            Statut = "Planifiee"
        });

        var reponse = await _client.DeleteAsync($"/api/agents/{agent.Id}");

        // Restrict volontaire sur Intervention.AgentId : l'historique du planning
        // ne disparaît pas avec l'agent, et le message doit indiquer la sortie.
        Assert.Equal(HttpStatusCode.Conflict, reponse.StatusCode);
        var message = await reponse.Content.ReadAsStringAsync();
        Assert.Contains("1 intervention", message);
        Assert.Contains("Actif = false", message);
    }

    /// <summary>
    /// La suppression d'un parent est <b>refusée</b>, elle n'emporte plus ses
    /// enfants.
    ///
    /// ⚠️ Ce test affirmait l'inverse jusqu'à la migration
    /// <c>SuppressionRestreinteSurLaChaineExploitation</c>, et il passait : la
    /// cascade était une décision, pas un oubli. Ce qui a changé n'est pas la
    /// lecture du risque, c'est le contexte — l'étape 16 bis a fait de ce
    /// <c>DELETE</c> un bouton dans une liste, là où il n'existait que dans
    /// Swagger. Mesuré avant correction : un client supprimé emportait le client,
    /// son site, son contrat, sa prestation et son intervention, en un appel, avec
    /// un 204.
    ///
    /// Le nom de l'ancien test est conservé dans l'historique git ; celui-ci garde
    /// la règle inverse, et le message doit rester exploitable — un refus qui ne
    /// nomme ni ce qui bloque ni la sortie est un cul-de-sac, et la friction n'a de
    /// valeur que si elle est franchissable.
    /// </summary>
    [Fact]
    public async Task Suppression_d_un_client_qui_porte_un_site_est_refusee()
    {
        var site = await ChaineSite();

        var suppression = await _client.DeleteAsync($"/api/clients/{site.ClientId}");

        Assert.Equal(HttpStatusCode.Conflict, suppression.StatusCode);

        var message = await suppression.Content.ReadAsStringAsync();
        Assert.Contains("1 site", message);
        Assert.Contains("Supprimer d'abord ses sites", message);

        // Et le site est toujours là : c'est l'assertion qui compte.
        var siteRelu = await _client.GetAsync($"/api/sites/{site.Id}");
        Assert.Equal(HttpStatusCode.OK, siteRelu.StatusCode);
    }

    /// <summary>
    /// Témoin : un parent sans enfant se supprime toujours.
    ///
    /// Sans lui, une restriction posée sur la mauvaise colonne — ou un
    /// <c>Conflict</c> rendu sans condition — ferait passer le test précédent en
    /// interdisant toute suppression de l'application.
    /// </summary>
    [Fact]
    public async Task Suppression_d_un_client_sans_site_est_acceptee()
    {
        var client = await Creer<ClientDto>("/api/clients", new ClientCreateDto
        {
            Nom = "Client sans site",
            Type = "Autre"
        });

        var suppression = await _client.DeleteAsync($"/api/clients/{client.Id}");

        Assert.Equal(HttpStatusCode.NoContent, suppression.StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await _client.GetAsync($"/api/clients/{client.Id}")).StatusCode);
    }

    /// <summary>
    /// Toute la chaîne se vide de bas en haut, et <b>uniquement</b> dans cet ordre.
    ///
    /// C'est le parcours réel qu'impose la restriction, et le seul moyen de vérifier
    /// que les cinq maillons sont bien posés : chaque refus prouve un maillon, chaque
    /// réussite prouve qu'il n'est pas posé trop haut. Une restriction manquante au
    /// milieu rendrait un 204 là où ce test attend un 409.
    /// </summary>
    [Fact]
    public async Task La_chaine_ne_se_vide_que_de_bas_en_haut()
    {
        var prestation = await ChainePrestation();

        var contrat = await _client.GetFromJsonAsync<ContratDto>(
            $"/api/contrats/{prestation.ContratId}");
        Assert.NotNull(contrat);

        var site = await _client.GetFromJsonAsync<SiteDto>($"/api/sites/{contrat.SiteId}");
        Assert.NotNull(site);

        var intervention = await Creer<InterventionDto>("/api/interventions",
            new InterventionCreateDto
            {
                PrestationId = prestation.Id,
                DatePrevue = new DateTime(2026, 10, 6),
                HeurePrevue = new TimeSpan(8, 0, 0),
                Statut = "Planifiee"
            });

        // De haut en bas : chaque maillon refuse, et nomme son enfant.
        await RefuseEnNommant($"/api/clients/{site.ClientId}", "1 site");
        await RefuseEnNommant($"/api/sites/{site.Id}", "1 contrat");
        await RefuseEnNommant($"/api/contrats/{contrat.Id}", "1 prestation");
        await RefuseEnNommant($"/api/prestations/{prestation.Id}", "1 intervention");

        // De bas en haut : tout passe, dans l'ordre inverse exact.
        Assert.Equal(HttpStatusCode.NoContent,
            (await _client.DeleteAsync($"/api/interventions/{intervention.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await _client.DeleteAsync($"/api/prestations/{prestation.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await _client.DeleteAsync($"/api/contrats/{contrat.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await _client.DeleteAsync($"/api/sites/{site.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await _client.DeleteAsync($"/api/clients/{site.ClientId}")).StatusCode);
    }

    /// <summary>
    /// Une absence bloque aussi la suppression de son agent, depuis la même
    /// migration, et le message compte les deux — un agent bloqué par ses seules
    /// absences lisait « porte 0 intervention(s) », ce qui ressemble à un défaut du
    /// logiciel plutôt qu'à une règle.
    /// </summary>
    [Fact]
    public async Task Une_absence_seule_bloque_la_suppression_de_son_agent()
    {
        var agent = await Creer<AgentDto>("/api/agents", new AgentCreateDto
        {
            Nom = "Sans",
            Prenom = "Vacation",
            DateEmbauche = new DateTime(2026, 1, 5)
        });

        await Creer<AbsenceDto>("/api/absences", new AbsenceCreateDto
        {
            AgentId = agent.Id,
            Type = "Conge",
            DateDebut = new DateTime(2026, 11, 2),
            DateFin = new DateTime(2026, 11, 6)
        });

        var suppression = await _client.DeleteAsync($"/api/agents/{agent.Id}");

        Assert.Equal(HttpStatusCode.Conflict, suppression.StatusCode);

        var message = await suppression.Content.ReadAsStringAsync();
        Assert.Contains("0 intervention(s)", message);
        Assert.Contains("1 absence(s)", message);
        Assert.Contains("Actif = false", message);
    }

    /// <summary>
    /// Un refus doit nommer <b>deux</b> choses : ce qui bloque, et la sortie.
    ///
    /// ⚠️ La seconde assertion vient d'un sabotage passé inaperçu. Ce contrôle ne
    /// vérifiait que le compte d'enfants, et vider le message de sa marche à suivre
    /// laissait la série verte — alors que c'est toute la raison d'être de ces
    /// messages. Un <c>Restrict</c> dont le refus ne dit pas par où sortir ne
    /// protège pas les données, il bloque l'utilisateur : la friction n'a de valeur
    /// que si elle est franchissable.
    /// </summary>
    private async Task RefuseEnNommant(string route, string attendu)
    {
        var reponse = await _client.DeleteAsync(route);

        Assert.Equal(HttpStatusCode.Conflict, reponse.StatusCode);

        var message = await reponse.Content.ReadAsStringAsync();

        Assert.Contains(attendu, message);
        Assert.Contains("Supprimer d'abord", message);
    }

    [Fact]
    public async Task Siret_deja_pris_par_une_autre_entreprise_renvoie_409()
    {
        // Le SIRET est unique à l'échelle de l'instance, pas du tenant : deux
        // entreprises clientes ne peuvent pas déclarer le même. La contrainte est
        // en base, hors portée des filtres, donc Alpha se heurte à celui de Beta
        // sans pouvoir le lire.
        var sienne = await _client.ListerAsync<EntrepriseDto>("/api/entreprises");
        Assert.NotNull(sienne);
        var alpha = Assert.Single(sienne);

        var reponse = await _client.PutAsJsonAsync($"/api/entreprises/{alpha.Id}",
            new EntrepriseCreateDto
            {
                RaisonSociale = alpha.RaisonSociale,
                Siret = "20000000000002"
            });

        Assert.Equal(HttpStatusCode.Conflict, reponse.StatusCode);
        Assert.Contains("déjà enregistré", await reponse.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Latitude_hors_bornes_refusee()
    {
        var reponse = await _client.PostAsJsonAsync("/api/sites", new SiteCreateDto
        {
            ClientId = Guid.NewGuid(),
            Nom = "X",
            Adresse = "Y",
            Latitude = 120,
            Longitude = 2.3
        });

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
    }

    /// <summary>
    /// <c>DateCreation</c> est la seule date d'un site ou d'un client sans contrat,
    /// donc celle dont part leur conservation (RGPD.md §9.7). Une date que le corps
    /// de la requête pourrait poser rendrait une ligne purgeable à volonté, ou
    /// impurgeable : le serveur la pose, et le <c>PUT</c> n'y touche pas.
    ///
    /// Les corps sont des objets anonymes, pas les <c>CreateDto</c>, pour pouvoir
    /// y forger la propriété que ces derniers n'ont pas.
    /// </summary>
    [Fact]
    public async Task DateCreation_posee_par_le_serveur_et_inchangee_par_PUT()
    {
        var forgee = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var avant = DateTime.UtcNow;

        var client = await Creer<ClientDto>("/api/clients", new
        {
            Nom = "Client daté",
            Type = "Copropriete",
            DateCreation = forgee
        });
        var site = await Creer<SiteDto>("/api/sites", new
        {
            ClientId = client.Id,
            Nom = "Site daté",
            Adresse = "1 rue du Test",
            Latitude = 48.85,
            Longitude = 2.33,
            DateCreation = forgee
        });

        Assert.InRange(client.DateCreation, avant.AddSeconds(-5), DateTime.UtcNow.AddSeconds(5));
        Assert.InRange(site.DateCreation, avant.AddSeconds(-5), DateTime.UtcNow.AddSeconds(5));

        // Relue en base, et non prise dans la réponse du POST : PostgreSQL tronque
        // à la microseconde ce que .NET porte au dixième.
        var clientRelu = await _client.GetFromJsonAsync<ClientDto>($"/api/clients/{client.Id}");
        var siteRelu = await _client.GetFromJsonAsync<SiteDto>($"/api/sites/{site.Id}");

        var modificationClient = await _client.PutAsJsonAsync($"/api/clients/{client.Id}", new
        {
            Nom = "Client daté, renommé",
            Type = "Copropriete",
            DateCreation = forgee
        });
        var modificationSite = await _client.PutAsJsonAsync($"/api/sites/{site.Id}", new
        {
            ClientId = client.Id,
            Nom = "Site daté, renommé",
            Adresse = "1 rue du Test",
            Latitude = 48.85,
            Longitude = 2.33,
            DateCreation = forgee
        });
        Assert.Equal(HttpStatusCode.NoContent, modificationClient.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, modificationSite.StatusCode);

        var clientApres = await _client.GetFromJsonAsync<ClientDto>($"/api/clients/{client.Id}");
        var siteApres = await _client.GetFromJsonAsync<SiteDto>($"/api/sites/{site.Id}");

        Assert.Equal("Client daté, renommé", clientApres!.Nom);
        Assert.Equal(clientRelu!.DateCreation, clientApres.DateCreation);
        Assert.Equal(siteRelu!.DateCreation, siteApres!.DateCreation);
    }

    /// <summary>
    /// Ce que la migration fait aux lignes antérieures à la colonne. Sans défaut en
    /// base, une colonne <c>timestamp</c> non nullable les date du 1er janvier de
    /// l'an 1, et le script de conservation les voit toutes hors délai : la purge la
    /// plus large possible, sur les lignes les moins bien connues.
    ///
    /// Une insertion SQL qui omet la colonne est exactement la situation d'une ligne
    /// existante au moment de l'<c>ALTER TABLE</c>, d'où le passage direct par la
    /// base — c'est ce que l'API ne peut pas produire.
    /// </summary>
    [Fact]
    public async Task Ligne_inseree_sans_DateCreation_est_datee_par_la_base()
    {
        var clientId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var entreprise = _fixture.EntrepriseAlpha;

        await _fixture.AvecServicesAsync(async services =>
        {
            var db = services.GetRequiredService<KleanDbContext>();

            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Clients" ("Id", "EntrepriseId", "Nom", "Type")
                VALUES ({clientId}, {entreprise}, 'Client antérieur', 'Autre')
                """);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Sites" ("Id", "EntrepriseId", "ClientId", "Nom", "Adresse", "Latitude", "Longitude")
                VALUES ({siteId}, {entreprise}, {clientId}, 'Site antérieur', '1 rue du Test', 48.85, 2.33)
                """);
        });

        var client = await _client.GetFromJsonAsync<ClientDto>($"/api/clients/{clientId}");
        var site = await _client.GetFromJsonAsync<SiteDto>($"/api/sites/{siteId}");

        var maintenant = DateTime.UtcNow;
        Assert.InRange(client!.DateCreation, maintenant.AddMinutes(-1), maintenant.AddMinutes(1));
        Assert.InRange(site!.DateCreation, maintenant.AddMinutes(-1), maintenant.AddMinutes(1));
    }

    // ---- fabriques de contexte ----

    private async Task<T> Creer<T>(string route, object corps)
    {
        var reponse = await _client.PostAsJsonAsync(route, corps);

        Assert.Equal(HttpStatusCode.Created, reponse.StatusCode);

        return await reponse.Content.ReadFromJsonAsync<T>()
            ?? throw new InvalidOperationException($"Réponse illisible sur {route}.");
    }

    private async Task<SiteDto> ChaineSite()
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

    private async Task<PrestationDto> ChainePrestation()
    {
        var site = await ChaineSite();

        var contrat = await Creer<ContratDto>("/api/contrats", new ContratCreateDto
        {
            SiteId = site.Id,
            DateDebut = new DateTime(2026, 1, 1),
            Frequence = "Mardi, jeudi"
        });

        return await Creer<PrestationDto>("/api/prestations", new PrestationCreateDto
        {
            ContratId = contrat.Id,
            Libelle = "Vitrerie",
            DureeEstimeeMinutes = 90
        });
    }
}
