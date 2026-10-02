using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using Klean.Api.Controllers;
using Klean.Api.DTOs;
using Klean.Api.Filtres;
using Klean.Api.Models;
using Klean.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;

namespace Klean.Api.Tests;

/// <summary>
/// Le journal d'accès aux données personnelles (SPEC.md étape 17, lot A).
///
/// Deux familles de vérifications, et la seconde est celle qui survivra à ce fichier.
///
/// <b>Le comportement</b> : une lecture laisse une ligne, une ligne dit qui et combien,
/// un refus ne laisse rien, et le journal est cadré par entreprise comme le reste.
///
/// <b>L'invariante structurelle</b> : aucune action de contrôleur ne peut divulguer un
/// DTO portant des attributs identifiants sans porter l'attribut de journalisation. Ce
/// n'est pas une relecture de code, c'est de la réflexion sur les types — donc elle
/// attrape le neuvième endpoint que personne n'aura pensé à déclarer.
/// </summary>
[Collection(CollectionApi.Nom)]
public class JournalAccesTests : IAsyncLifetime
{
    private readonly FixtureApi _fixture;
    private HttpClient _client = null!;

    public JournalAccesTests(FixtureApi fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await _fixture.ReinitialiserAsync();
        _client = await _fixture.ClientConnecteAsync(FixtureApi.AdminAlpha);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ---------------------------------------------------------------------------
    // L'invariante qui tient
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Les propriétés qui font d'un DTO un porteur de données identifiantes.
    ///
    /// ⚠️ Structurel et non nominatif : la règle ne nomme pas les DTOs concernés, elle
    /// nomme les <b>attributs</b> qui rendent une personne identifiable ou qui portent
    /// une donnée sensible. Un DTO créé demain et exposant un <c>Nom</c> tombe donc dans
    /// le filet sans que personne ait à y penser — c'est tout l'objet.
    /// </summary>
    private static readonly string[] ProprietesIdentifiantes =
    [
        "Nom", "Prenom", "Adresse", "Email", "Telephone", "Latitude", "Longitude", "Motif"
    ];

    /// <summary>
    /// Les DTOs exemptés, nommément et avec leur raison.
    ///
    /// ⚠️ La liste est une <b>exemption</b>, pas une inclusion : le défaut est de devoir
    /// journaliser, et s'en dispenser demande de l'écrire ici. L'inverse — une liste des
    /// DTOs à journaliser — laisserait un nouveau DTO hors du journal par simple
    /// omission, ce qui est la façon normale dont ces mécanismes se vident.
    ///
    /// <c>ClientDto</c> et <c>SiteDto</c> portent nom, adresse, courriel et téléphone,
    /// mais d'une <b>personne morale</b> : une entreprise cliente, un syndic, une
    /// copropriété. Le périmètre de l'étape 17 est « les données d'agents et
    /// d'absences ». ⚠️ Cela reste discutable — le contact d'une petite copropriété est
    /// souvent une personne physique — et la question appartient au lot de la
    /// cartographie des données, pas à celui-ci. Elle est consignée, pas tranchée.
    /// </summary>
    private static readonly string[] DtosExemptes =
    [
        "ClientDto",
        "SiteDto",

        // Le `Nom` d'un produit est « Détergent sols ». C'est le prix de la règle :
        // `Nom` est un signal grossier, et il attrape tout ce qui porte un nom. Le
        // raffiner — exiger `Prenom`, ou `Nom` avec `Adresse` — ne marcherait pas :
        // `AbsenceDto` ne porte que `Motif`, et `ClientDto` porterait `Nom` **et**
        // `Adresse`. Mieux vaut une règle grossière à défaut fermé, dont chaque
        // exception est écrite, qu'une règle fine qui laisse passer en silence.
        "ProduitDto",

        // ⚠️ `ProfilDto` porte nom, prénom et courriel — mais **ceux de l'appelant**.
        // `GET /api/auth/moi` ne divulgue rien que l'appelant ne sache déjà de
        // lui-même, et un journal de « qui a lu son propre nom » serait du bruit qui
        // noierait les lectures qui comptent. Le RGPD s'inquiète de l'accès aux données
        // d'autrui, pas de l'accès à soi.
        //
        // ⚠️ La limite à connaître : `POST /api/auth/comptes` rend aussi un `ProfilDto`,
        // celui d'un compte qu'un administrateur vient de créer pour quelqu'un d'autre.
        // Ce n'est pas une lecture — l'administrateur a fourni ces données — mais le
        // jour où un endpoint **listera** les comptes, il sortira du nom et du courriel
        // d'autrui et devra être journalisé. Aucun n'existe (CLAUDE.md §7), et cette
        // exemption ne le couvrirait pas puisqu'elle porte sur le DTO, non sur l'action.
        "ProfilDto"
    ];

    [Fact]
    public void Aucune_action_ne_divulgue_de_donnees_identifiantes_sans_etre_journalisee()
    {
        var manquantes = new List<string>();

        foreach (var controleur in typeof(AgentsController).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract))
        {
            foreach (var action in controleur.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.DeclaringType == controleur && !m.IsSpecialName))
            {
                // ⚠️ Les lectures seulement, et c'est une distinction de fond, pas une
                // commodité : une écriture **renvoie ce que l'appelant vient de
                // fournir**. `POST /api/agents` rend l'`AgentDto` qu'on lui a donné —
                // le journaliser consignerait « a lu les données qu'il a lui-même
                // écrites », ce qui est faux et bruyant. C'est aussi ce qu'affirme
                // `Une_ecriture_ne_laisse_rien_au_journal` côté comportement : les deux
                // tests diraient le contraire l'un de l'autre sans cette ligne.
                //
                // Ce que cette restriction laisse passer, et qu'il faut savoir : un
                // futur `POST` qui rendrait des données d'autrui — une recherche par
                // POST, un export — échapperait au filet. Aucun n'existe.
                if (!EstLecture(action)) continue;

                var porteurs = TypesDivulgues(action.ReturnType)
                    .Where(EstIdentifiant)
                    .Select(t => t.Name)
                    .Distinct()
                    .ToList();

                if (porteurs.Count == 0) continue;

                if (action.GetCustomAttribute<JournaliserAccesAttribute>() is null)
                {
                    manquantes.Add(
                        $"{controleur.Name}.{action.Name} divulgue {string.Join(", ", porteurs)} "
                        + "sans [JournaliserAcces]");
                }
            }
        }

        Assert.Empty(manquantes);
    }

    [Fact]
    public void Les_dix_actions_journalisees_sont_celles_attendues()
    {
        // Le miroir du test précédent : il garde l'absence d'oubli, celui-ci garde
        // l'absence d'excès. Journaliser une action qui ne divulgue rien gonflerait le
        // journal de lignes sans objet, et un journal bruyant ne se lit plus.
        var journalisees = typeof(AgentsController).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.DeclaringType == t))
            .Where(m => m.GetCustomAttribute<JournaliserAccesAttribute>() is not null)
            .Select(m => $"{m.DeclaringType!.Name}.{m.Name}")
            .OrderBy(n => n)
            .ToList();

        Assert.Equal(
            [
                "AbsencesController.Lister",
                "AbsencesController.Obtenir",
                "AgentsController.Anonymiser",
                "AgentsController.Dossier",
                "AgentsController.Lister",
                "AgentsController.Obtenir",
                "InterventionsController.Candidats",
                "JournalAccesController.Lister",
                "PlanningController.Semaine",
                "RemplacementsController.Candidats"
            ],
            journalisees);
    }

    /// <summary>Les DTOs atteignables depuis un type de retour d'action.</summary>
    private static IEnumerable<Type> TypesDivulgues(Type type)
    {
        var vus = new HashSet<Type>();
        var aExplorer = new Stack<Type>([type]);

        while (aExplorer.Count > 0)
        {
            var courant = aExplorer.Pop();

            if (!vus.Add(courant)) continue;

            // ActionResult<PageDto<AgentDto>>, Task<…>, IEnumerable<CandidatDto> : on
            // descend dans les arguments génériques jusqu'aux DTOs.
            foreach (var argument in courant.GetGenericArguments()) aExplorer.Push(argument);

            if (courant.Namespace != "Klean.Api.DTOs") continue;

            yield return courant;

            // Les DTOs imbriqués comptent : PlanningSemaineDto ne porte aucun nom
            // lui-même, ce sont ses PlanningAgentDto qui en portent.
            foreach (var propriete in courant.GetProperties())
            {
                aExplorer.Push(propriete.PropertyType);
            }
        }
    }

    private static bool EstLecture(MethodInfo action)
    {
        return action.GetCustomAttributes<HttpMethodAttribute>()
            .SelectMany(a => a.HttpMethods)
            .Any(verbe => verbe == "GET");
    }

    private static bool EstIdentifiant(Type dto)
    {
        if (DtosExemptes.Contains(dto.Name)) return false;

        return dto.GetProperties().Any(p => ProprietesIdentifiantes.Contains(p.Name));
    }

    [Fact]
    public void Le_journal_lui_meme_ne_porte_aucune_donnee_identifiante()
    {
        // ⚠️ La garde contre l'enrichissement bien intentionné. Ajouter « le nom de
        // l'agent lu » rendrait le journal lisible — et en ferait une seconde base de
        // données personnelles, moins protégée que la première, à purger elle aussi.
        foreach (var type in new[] { typeof(AccesJournalise), typeof(JournalAccesDto) })
        {
            var fautives = type.GetProperties()
                .Select(p => p.Name)
                .Where(ProprietesIdentifiantes.Contains)
                .ToList();

            Assert.Empty(fautives);
        }
    }

    // ---------------------------------------------------------------------------
    // Le comportement
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Lister_les_agents_laisse_une_ligne_et_une_seule()
    {
        await CreerAgents(3);

        var avant = await Journal();
        await _client.PageAsync<AgentDto>("/api/agents");
        var apres = await Journal();

        var nouvelles = Nouvelles(avant, apres);

        Assert.Single(nouvelles);
        Assert.Equal(nameof(RessourceAccedee.Agent), nouvelles[0].Ressource);
        Assert.Equal(nameof(NatureAcces.Liste), nouvelles[0].Nature);
        Assert.Equal(3, nouvelles[0].Lignes);
        Assert.Null(nouvelles[0].CibleId);
    }

    [Fact]
    public async Task La_ligne_compte_ce_qui_est_sorti_et_non_ce_qui_existe()
    {
        // ⚠️ LE test de ce fichier côté comportement. Compter `Total` au lieu des
        // `Elements` ferait écrire « a lu 5 agents » sur une page qui en a rendu 2 : le
        // journal surdéclarerait, et une surdéclaration est un mensonge autant qu'une
        // sous-déclaration. Rien, dans l'application, ne le signalerait.
        await CreerAgents(5);

        var avant = await Journal();
        var page = await _client.PageAsync<AgentDto>("/api/agents?page=1&taille=2");
        var nouvelles = Nouvelles(avant, await Journal());

        Assert.Equal(5, page.Total);
        Assert.Equal(2, page.Elements.Count());
        Assert.Equal(2, Assert.Single(nouvelles).Lignes);
    }

    [Fact]
    public async Task Lire_un_agent_nommement_consigne_son_identifiant()
    {
        var agent = (await CreerAgents(1)).Single();

        var avant = await Journal();
        var relu = await _client.GetFromJsonAsync<AgentDto>($"/api/agents/{agent}");
        var ligne = Assert.Single(Nouvelles(avant, await Journal()));

        Assert.NotNull(relu);
        Assert.Equal(nameof(NatureAcces.Lecture), ligne.Nature);
        Assert.Equal(agent, ligne.CibleId);
        Assert.Equal(1, ligne.Lignes);
    }

    [Fact]
    public async Task Le_planning_est_journalise_bien_qu_il_ne_passe_par_aucune_route_d_agent()
    {
        // ⚠️ Le trou qu'un journal « sur /api/agents et /api/absences » aurait laissé, et
        // il aurait eu l'air complet. PlanningAgentDto porte nom et prénom de **tous**
        // les agents de l'entreprise, et PlanningNonPourvueDto porte la cause « Agent
        // absent » — donc le planning divulgue aussi une absence.
        await CreerAgents(2);

        var avant = await Journal();
        var reponse = await _client.GetAsync("/api/planning?lundi=2026-09-07");
        reponse.EnsureSuccessStatusCode();

        var ligne = Assert.Single(Nouvelles(avant, await Journal()));

        Assert.Equal(nameof(RessourceAccedee.Planning), ligne.Ressource);
    }

    [Fact]
    public async Task Les_candidats_sont_journalises_sur_les_deux_endpoints_qui_les_exposent()
    {
        // CandidatDto expose nom, prénom et **distance au domicile**. Il sort par deux
        // routes, et n'en couvrir qu'une laisserait l'autre muette.
        var intervention = await ChaineAvecIntervention();

        var avant = await Journal();
        (await _client.GetAsync($"/api/interventions/{intervention}/candidats"))
            .EnsureSuccessStatusCode();
        var ligne = Assert.Single(Nouvelles(avant, await Journal()));

        Assert.Equal(nameof(RessourceAccedee.Candidats), ligne.Ressource);
        // La cible est la vacation, pas un agent : « a consulté les remplaçants
        // possibles de cette vacation » est l'information utile.
        Assert.Equal(intervention, ligne.CibleId);

        var remplacement = await _client.PostAsJsonAsync(
            $"/api/interventions/{intervention}/remplacement", new { });
        remplacement.EnsureSuccessStatusCode();
        var ouvert = await remplacement.Content.ReadFromJsonAsync<RemplacementDto>();

        avant = await Journal();
        (await _client.GetAsync($"/api/remplacements/{ouvert!.Id}/candidats"))
            .EnsureSuccessStatusCode();

        Assert.Equal(
            nameof(RessourceAccedee.Candidats),
            Assert.Single(Nouvelles(avant, await Journal())).Ressource);
    }

    [Fact]
    public async Task Les_absences_sont_journalisees_en_liste_et_en_lecture()
    {
        var agent = (await CreerAgents(1)).Single();
        var creation = await _client.PostAsJsonAsync("/api/absences", new AbsenceCreateDto
        {
            AgentId = agent,
            Type = "Conge",
            DateDebut = new DateTime(2026, 9, 7),
            DateFin = new DateTime(2026, 9, 9)
        });
        creation.EnsureSuccessStatusCode();
        var absence = await creation.Content.ReadFromJsonAsync<AbsenceDto>();

        var avant = await Journal();
        await _client.PageAsync<AbsenceDto>("/api/absences");
        await _client.GetFromJsonAsync<AbsenceDto>($"/api/absences/{absence!.Id}");

        var nouvelles = Nouvelles(avant, await Journal());

        Assert.Equal(2, nouvelles.Count);
        Assert.All(nouvelles, l => Assert.Equal(nameof(RessourceAccedee.Absence), l.Ressource));
        Assert.Contains(nouvelles, l => l.Nature == nameof(NatureAcces.Liste) && l.CibleId is null);
        Assert.Contains(nouvelles, l => l.Nature == nameof(NatureAcces.Lecture)
            && l.CibleId == absence.Id);
    }

    [Fact]
    public async Task Une_ecriture_ne_laisse_rien_au_journal()
    {
        // La création d'un agent ne divulgue rien : elle écrit ce que l'appelant a
        // lui-même fourni. Journaliser les écritures serait un autre mécanisme — un
        // journal de modifications — utile mais hors du périmètre de l'étape.
        var avant = await Journal();
        await CreerAgents(1);

        Assert.Empty(Nouvelles(avant, await Journal()));
    }

    [Fact]
    public async Task Un_refus_ne_laisse_rien_au_journal()
    {
        // Rien n'est sorti, donc rien n'est consigné. Journaliser les tentatives serait
        // un journal de sécurité, différent et légitime — mais mêlé à celui-ci, le
        // sondage d'un compte mal cadré noierait les divulgations réelles.
        await CreerAgents(1);

        // Un compte ClientSyndic doit porter un ClientId : c'est le deuxième axe
        // d'isolation, et le serveur refuse sa création sans lui.
        var client = await Creer<ClientDto>("/api/clients",
            new ClientCreateDto { Nom = "Syndic du journal", Type = "Syndic" });
        var syndic = await _fixture.ClientDeRoleAsync(
            _client, RolesKlean.ClientSyndic, "syndic.journal@test.local", clientId: client.Id);

        var avant = await Journal();
        var refus = await syndic.GetAsync("/api/agents");

        Assert.Equal(HttpStatusCode.Forbidden, refus.StatusCode);
        Assert.Empty(Nouvelles(avant, await Journal()));
    }

    [Fact]
    public async Task Une_ligne_introuvable_ne_laisse_rien_au_journal()
    {
        var avant = await Journal();
        var reponse = await _client.GetAsync($"/api/agents/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, reponse.StatusCode);
        Assert.Empty(Nouvelles(avant, await Journal()));
    }

    [Fact]
    public async Task Lire_le_journal_est_lui_meme_journalise()
    {
        // L'accès le plus sensible du produit : il dit qui a regardé quoi. Une lecture,
        // une ligne — pas de récursion, le filtre s'exécute une fois par requête.
        var premiere = await Journal();
        var seconde = await Journal();

        // ⚠️ `ToutesNouvelles` et non `Nouvelles` : cette dernière écarte justement les
        // lignes du journal lui-même, pour que les autres tests ne comptent pas leurs
        // propres témoins. Les chercher avec elle ne donnait rien — défaut de ce test,
        // pas du mécanisme.
        var nouvelles = ToutesNouvelles(premiere, seconde);

        Assert.Single(nouvelles);
        Assert.Equal(nameof(RessourceAccedee.JournalAcces), nouvelles[0].Ressource);
        Assert.Equal(nameof(NatureAcces.Liste), nouvelles[0].Nature);
    }

    [Fact]
    public async Task Le_journal_est_reserve_a_l_administrateur()
    {
        var responsable = await _fixture.ClientDeRoleAsync(
            _client, RolesKlean.Responsable, "responsable.journal@test.local");
        var chef = await _fixture.ClientDeRoleAsync(
            _client, RolesKlean.ChefEquipe, "chef.journal@test.local");

        // Ouvert à l'un ou à l'autre, le journal devient un outil de surveillance des
        // collègues — ce qu'un journal RGPD n'est précisément pas censé être.
        Assert.Equal(HttpStatusCode.Forbidden,
            (await responsable.GetAsync("/api/journal-acces")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await chef.GetAsync("/api/journal-acces")).StatusCode);
    }

    [Fact]
    public void Le_journal_n_expose_aucune_ecriture()
    {
        // Structurel : un journal que l'API permet de modifier ou d'élaguer ne prouve
        // rien. La purge passe par un script sur la base (Scripts/purge-journal-acces.sql).
        var verbes = typeof(JournalAccesController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.DeclaringType == typeof(JournalAccesController))
            .SelectMany(m => m.GetCustomAttributes<HttpMethodAttribute>())
            .SelectMany(a => a.HttpMethods)
            .Distinct()
            .ToList();

        Assert.Equal(["GET"], verbes);
    }

    [Fact]
    public async Task Le_journal_est_cadre_par_entreprise()
    {
        // Le douzième filtre global. Sans lui, un administrateur lirait les habitudes de
        // consultation des autres entreprises de l'instance.
        await CreerAgents(1);
        await _client.PageAsync<AgentDto>("/api/agents");

        var beta = await _fixture.ClientConnecteAsync(FixtureApi.AdminBeta);
        (await beta.GetAsync("/api/planning?lundi=2026-09-07")).EnsureSuccessStatusCode();

        var mien = await Journal();
        var sien = await beta.PageAsync<JournalAccesDto>("/api/journal-acces?page=1&taille=200");

        Assert.All(mien.Elements, l => Assert.NotEqual(nameof(RessourceAccedee.Planning), l.Ressource));
        Assert.Contains(sien.Elements, l => l.Ressource == nameof(RessourceAccedee.Planning));
        Assert.Empty(mien.Elements.Select(l => l.Id).Intersect(sien.Elements.Select(l => l.Id)));
    }

    [Fact]
    public async Task L_entreprise_de_la_ligne_vient_du_jeton_et_non_de_l_appelant()
    {
        await CreerAgents(1);
        await _client.PageAsync<AgentDto>("/api/agents");

        var page = await Journal();

        Assert.All(page.Elements, l => Assert.Equal(_fixture.EntrepriseAlpha, l.EntrepriseId));
    }

    [Fact]
    public async Task Les_trois_filtres_repondent_aux_trois_questions_d_un_controle()
    {
        await CreerAgents(2);
        await _client.PageAsync<AgentDto>("/api/agents");
        (await _client.GetAsync("/api/planning?lundi=2026-09-07")).EnsureSuccessStatusCode();

        var moi = (await _client.GetFromJsonAsync<ProfilDto>("/api/auth/moi"))!.Id;

        var parRessource = await _client.PageAsync<JournalAccesDto>(
            "/api/journal-acces?page=1&taille=200&ressource=Planning");
        Assert.NotEmpty(parRessource.Elements);
        Assert.All(parRessource.Elements,
            l => Assert.Equal(nameof(RessourceAccedee.Planning), l.Ressource));

        var parUtilisateur = await _client.PageAsync<JournalAccesDto>(
            $"/api/journal-acces?page=1&taille=200&utilisateurId={moi}");
        Assert.NotEmpty(parUtilisateur.Elements);
        Assert.All(parUtilisateur.Elements, l => Assert.Equal(moi, l.UtilisateurId));

        var horsPeriode = await _client.PageAsync<JournalAccesDto>(
            "/api/journal-acces?page=1&taille=200&jusqua=2020-01-01");
        Assert.Empty(horsPeriode.Elements);
        Assert.Equal(0, horsPeriode.Total);
    }

    [Fact]
    public async Task Une_ressource_inconnue_est_refusee_en_nommant_les_valeurs_acceptees()
    {
        var avant = await Journal();
        var reponse = await _client.GetAsync("/api/journal-acces?ressource=Salaire");

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);

        var detail = await reponse.Content.ReadAsStringAsync();
        Assert.Contains("Agent", detail);
        Assert.Contains("Planning", detail);

        // ⚠️ Et ce refus ne laisse **rien** au journal. C'est ici, et nulle part
        // ailleurs, que le contrôle de statut du filtre est éprouvé : un 403 est produit
        // par le filtre d'autorisation, qui s'exécute avant les filtres d'action, et
        // `NotFound()` rend un `NotFoundResult` qui n'est pas un `ObjectResult` — ni l'un
        // ni l'autre n'atteint ce contrôle. Un `BadRequest("…")`, lui, rend un
        // `BadRequestObjectResult` : un ObjectResult avec un corps non nul et un statut
        // 400. Sans le contrôle, il serait consigné comme la divulgation d'une ligne.
        //
        // Le sabotage qui retire ce contrôle est resté vert une première fois, parce que
        // les deux autres témoins de refus ne l'exerçaient pas.
        // La seule ligne apparue est celle du relevé `avant` lui-même. Avec le sabotage,
        // il y en a deux — et la seconde annonce fièrement « 134 lignes lues », parce
        // qu'une `string` est une séquence de caractères.
        var nouvelles = ToutesNouvelles(avant, await Journal());

        Assert.Single(nouvelles);
        Assert.Equal(nameof(RessourceAccedee.JournalAcces), nouvelles[0].Ressource);
    }

    [Fact]
    public async Task Un_acces_inattribuable_echoue_plutot_que_de_s_ecrire_anonymement()
    {
        // ⚠️ Éprouvé **directement sur le filtre**, et non à travers l'API, parce que
        // l'API ne peut pas produire ce cas : les huit actions portent [Authorize], et
        // `OnTokenValidated` rejette depuis l'étape 16 un jeton dont l'identifiant n'est
        // pas exploitable. La branche est donc une défense en profondeur dont la
        // condition est inatteignable par la surface publique — ce qui ne la rend pas
        // inutile, mais la rend inéprouvable autrement.
        //
        // Sans elle, un accès non attribuable s'écrirait avec `Guid.Empty` : le journal
        // répondrait « quelqu'un » à la question « qui », ce qui est pire qu'une absence
        // de ligne, puisque ça ressemble à une réponse.
        var attribut = new JournaliserAccesAttribute(RessourceAccedee.Agent, NatureAcces.Liste);

        var requete = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) };
        var action = new ActionContext(requete, new RouteData(), new ActionDescriptor());
        var contexte = new ActionExecutingContext(
            action, [], new Dictionary<string, object?>(), controller: null!);

        Task<ActionExecutedContext> suivant() => Task.FromResult(
            new ActionExecutedContext(action, [], controller: null!)
            {
                Result = new OkObjectResult(new[] { new AgentDto() })
            });

        var echec = await Assert.ThrowsAsync<InvalidOperationException>(
            () => attribut.OnActionExecutionAsync(contexte, suivant));

        Assert.Contains("ne peut pas attribuer", echec.Message);
    }

    // ---------------------------------------------------------------------------
    // Le comptage, éprouvé directement
    //
    // ⚠️ Plusieurs de ses branches gardent des formes qu'aucune action ne produit
    // aujourd'hui. Les éprouver par l'API est impossible, et les laisser sans test les
    // laisse libres de dériver : le sabotage qui retire la garde des chaînes est resté
    // vert deux fois avant que cette série existe.
    // ---------------------------------------------------------------------------

    [Fact]
    public void Une_enveloppe_de_pagination_compte_ses_elements_et_non_son_total()
    {
        var page = new PageDto<AgentDto>
        {
            Elements = [new AgentDto(), new AgentDto()],
            Total = 150,
            Page = 1,
            Taille = 2
        };

        Assert.Equal(2, JournaliserAccesAttribute.Compter(page));
    }

    [Fact]
    public void Le_planning_compte_ses_agents_nommes_et_non_ses_vacations()
    {
        // Son `TotalVacations` ne dit rien du nombre de **personnes** nommées, et c'est
        // le nombre de personnes qui intéresse un journal de données personnelles.
        var planning = new PlanningSemaineDto
        {
            Agents = [new PlanningAgentDto(), new PlanningAgentDto(), new PlanningAgentDto()],
            TotalVacations = 17
        };

        Assert.Equal(3, JournaliserAccesAttribute.Compter(planning));
    }

    [Fact]
    public void Une_sequence_nue_est_comptee_element_par_element()
    {
        Assert.Equal(4, JournaliserAccesAttribute.Compter(
            new[] { new CandidatDto(), new CandidatDto(), new CandidatDto(), new CandidatDto() }));
        Assert.Equal(0, JournaliserAccesAttribute.Compter(Array.Empty<CandidatDto>()));
    }

    [Fact]
    public void Une_chaine_compte_pour_une_ligne_et_non_pour_ses_caracteres()
    {
        // ⚠️ `string` implémente `IEnumerable<char>`. Sans garde explicite, un corps
        // textuel est compté caractère par caractère et le journal écrit « a lu 134
        // lignes » pour une phrase. Aucune action journalisée ne rend de texte en 2xx
        // aujourd'hui — mais les messages de refus en rendent, et c'est à un contrôle
        // de statut près.
        Assert.Equal(1, JournaliserAccesAttribute.Compter("Un message de trente caractères"));
    }

    [Fact]
    public void Un_objet_seul_compte_pour_une_ligne()
    {
        Assert.Equal(1, JournaliserAccesAttribute.Compter(new AgentDto()));
    }

    [Fact]
    public void Seule_une_reponse_a_succes_portant_un_corps_est_une_divulgation()
    {
        // Les quatre formes que prend l'absence de divulgation, dont deux n'atteignent
        // jamais ce code en pratique — un 403 vient du filtre d'autorisation, qui
        // s'exécute avant, et `NotFound()` ne porte pas de corps.
        Assert.Null(JournaliserAccesAttribute.Divulgation(null));
        Assert.Null(JournaliserAccesAttribute.Divulgation(new NotFoundResult()));
        Assert.Null(JournaliserAccesAttribute.Divulgation(new NoContentResult()));
        Assert.Null(JournaliserAccesAttribute.Divulgation(new BadRequestObjectResult("Refusé")));
        Assert.Null(JournaliserAccesAttribute.Divulgation(new ConflictObjectResult("Refusé")));
        Assert.Null(JournaliserAccesAttribute.Divulgation(new OkObjectResult(null)));

        Assert.Equal(1, JournaliserAccesAttribute.Divulgation(new OkObjectResult(new AgentDto())));
        Assert.Equal(2, JournaliserAccesAttribute.Divulgation(
            new CreatedResult("/api/agents", new[] { new AgentDto(), new AgentDto() })));
    }

    // ---- Fabrication ----

    private Task<PageDto<JournalAccesDto>> Journal() =>
        _client.PageAsync<JournalAccesDto>("/api/journal-acces?page=1&taille=200");

    /// <summary>
    /// Les lignes apparues entre deux relevés, <b>hors</b> celles que ces relevés ont
    /// eux-mêmes produites.
    ///
    /// ⚠️ La lecture du journal étant journalisée, chaque relevé ajoute une ligne :
    /// sans cette soustraction, tout compte est faussé d'un.
    /// </summary>
    private static List<JournalAccesDto> Nouvelles(
        PageDto<JournalAccesDto> avant,
        PageDto<JournalAccesDto> apres)
    {
        return ToutesNouvelles(avant, apres)
            .Where(l => l.Ressource != nameof(RessourceAccedee.JournalAcces))
            .ToList();
    }

    private static List<JournalAccesDto> ToutesNouvelles(
        PageDto<JournalAccesDto> avant,
        PageDto<JournalAccesDto> apres)
    {
        var connues = avant.Elements.Select(l => l.Id).ToHashSet();

        return apres.Elements.Where(l => !connues.Contains(l.Id)).ToList();
    }

    private async Task<List<Guid>> CreerAgents(int combien)
    {
        var ids = new List<Guid>();

        for (var i = 0; i < combien; i++)
        {
            var reponse = await _client.PostAsJsonAsync("/api/agents", new AgentCreateDto
            {
                Nom = $"Agent {i:D2}",
                Prenom = "Test",
                DateEmbauche = new DateTime(2024, 3, 15),
                Latitude = 48.8566,
                Longitude = 2.3522
            });

            reponse.EnsureSuccessStatusCode();
            ids.Add((await reponse.Content.ReadFromJsonAsync<AgentDto>())!.Id);
        }

        return ids;
    }

    private async Task<Guid> ChaineAvecIntervention()
    {
        await CreerAgents(2);

        var client = await Creer<ClientDto>("/api/clients",
            new ClientCreateDto { Nom = "Syndic Journal", Type = "Syndic" });
        var site = await Creer<SiteDto>("/api/sites", new SiteCreateDto
        {
            ClientId = client.Id,
            Nom = "Résidence Journal",
            Adresse = "1 rue du Test",
            Latitude = 48.8421,
            Longitude = 2.3219
        });
        var contrat = await Creer<ContratDto>("/api/contrats", new ContratCreateDto
        {
            SiteId = site.Id,
            DateDebut = new DateTime(2026, 1, 1),
            Frequence = "Lundi"
        });
        var prestation = await Creer<PrestationDto>("/api/prestations", new PrestationCreateDto
        {
            ContratId = contrat.Id,
            Libelle = "Nettoyage",
            DureeEstimeeMinutes = 120
        });
        var intervention = await Creer<InterventionDto>("/api/interventions", new InterventionCreateDto
        {
            PrestationId = prestation.Id,
            DatePrevue = new DateTime(2026, 9, 7),
            HeurePrevue = new TimeSpan(8, 30, 0),
            Statut = "Planifiee"
        });

        return intervention.Id;
    }

    private async Task<T> Creer<T>(string route, object corps)
    {
        var reponse = await _client.PostAsJsonAsync(route, corps);

        if (!reponse.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"POST {route} → {(int)reponse.StatusCode} : "
                + await reponse.Content.ReadAsStringAsync());
        }

        return (await reponse.Content.ReadFromJsonAsync<T>())!;
    }
}
