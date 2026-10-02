using System.Security.Claims;
using Klean.Api.Data;
using Klean.Api.Middleware;
using Klean.Api.Models;
using Klean.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

// La chaîne vit dans les user secrets en développement, dans une variable
// d'environnement en production. Jamais dans appsettings.json.
var chaine = builder.Configuration.GetConnectionString("Klean")
    ?? throw new InvalidOperationException(
        "Chaîne de connexion « Klean » absente. Voir README.md, section Installation.");

// ---------------------------------------------------------------------------
// Surface HTTP (SPEC.md étape 16)
//
// Quatre réglages qui n'ont pas de valeur par défaut raisonnable universelle, et
// qui sont donc lus dans la configuration. Aucun n'est un secret : ils vivent
// dans appsettings.example.json, pas dans les user secrets.
// ---------------------------------------------------------------------------

// Origines autorisées à appeler l'API depuis un navigateur.
//
// ⚠️ Vide par défaut, et c'est le bon défaut : aucune origine tierce n'est
// autorisée tant que quelqu'un ne l'a pas écrit. Un déploiement qui sert le
// frontend et l'API sous la même origine — le plus simple, et celui que
// l'étape 18 documentera — n'a rien à déclarer ici.
//
// ⚠️ Les cookies de session sont SameSite=Strict. Une origine déclarée ici ne
// suffit donc pas à faire fonctionner un frontend hébergé sur un *autre domaine
// enregistrable* : le navigateur n'enverrait pas les cookies, quelle que soit la
// politique CORS. Le déploiement multi-origine n'est viable qu'entre
// sous-domaines d'un même domaine.
var originesAutorisees = builder.Configuration.GetSection("Cors:Origines").Get<string[]>() ?? [];

const string PolitiqueCors = "klean";

// Le plus gros corps légitime aujourd'hui est un DTO JSON de quelques kilo-octets.
// Le défaut de Kestrel — 30 Mo — est calibré pour des téléversements que KLEAN
// n'a pas encore. Une future photo de certification devra porter sa propre
// limite par [RequestSizeLimit], plus haute que celle-ci.
//
// La valeur sert deux fois, et les deux sont nécessaires : au middleware
// LimiteCorpsRequete, qui refuse sur Content-Length quel que soit le serveur, et
// à Kestrel, qui seul peut arrêter un corps en fragments pendant sa lecture.
var tailleMaxCorps = builder.Configuration.GetValue<long?>("Http:TailleMaxCorpsOctets")
                     ?? 1024 * 1024;

var delaiRequete = TimeSpan.FromSeconds(
    builder.Configuration.GetValue<int?>("Http:DelaiRequeteSecondes") ?? 30);

// Port de destination de la redirection HTTPS. Il doit être explicite : sans lui,
// UseHttpsRedirection tente de le déduire des adresses d'écoute, échoue en
// silence quand il n'y en a pas, et laisse passer la requête en clair — un
// avertissement dans le journal, rien de plus.
var portHttps = builder.Configuration.GetValue<int?>("Http:PortHttps") ?? 443;

builder.Services.AddCors(options => options.AddPolicy(PolitiqueCors, politique =>
{
    if (originesAutorisees.Length == 0) return;

    politique
        .WithOrigins(originesAutorisees)
        .AllowAnyHeader()
        .AllowAnyMethod()
        // Indispensable : l'authentification passe par cookie. Et incompatible
        // avec une origine jokers, ce qu'ASP.NET refuse de lui-même — la seule
        // façon d'ouvrir l'API à tous serait de retirer cette ligne, donc de
        // casser l'authentification du même geste.
        .AllowCredentials();
}));

builder.Services.AddRequestTimeouts(options =>
    options.DefaultPolicy = new Microsoft.AspNetCore.Http.Timeouts.RequestTimeoutPolicy
    {
        Timeout = delaiRequete,
        TimeoutStatusCode = StatusCodes.Status504GatewayTimeout
    });

builder.Services.AddHttpsRedirection(options => options.HttpsPort = portHttps);

// Un an, au lieu des trente jours par défaut.
//
// ⚠️ IncludeSubDomains reste *désactivé*, délibérément. Une instance de KLEAN vit
// sous un sous-domaine d'une entreprise qui possède aussi d'autres sous-domaines,
// dont rien ne garantit qu'ils soient en HTTPS. Et une politique HSTS ne se
// retire pas : un navigateur qui l'a mémorisée refusera le clair jusqu'à
// l'échéance, quoi qu'on republie ensuite. Le coût d'un excès de zèle ici est
// payé par le voisin, pendant un an.
builder.Services.AddHsts(options => options.MaxAge = TimeSpan.FromDays(365));

builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = tailleMaxCorps);

// Le tenant est lu dans la revendication du jeton, via le contexte HTTP.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, TenantContext>();

builder.Services.AddDbContext<KleanDbContext>(options => options.UseNpgsql(chaine));

builder.Services.AddIdentity<Utilisateur, IdentityRole<Guid>>(options =>
    {
        // Dix caractères, majuscule, minuscule, chiffre. Pas de caractère
        // spécial exigé : la longueur porte la robustesse (CLAUDE.md §6).
        options.Password.RequiredLength = 10;
        options.Password.RequireUppercase = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireDigit = true;
        options.Password.RequireNonAlphanumeric = false;

        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);

        // RequireUniqueEmail appartient à IdentityOptions.User, pas à .SignIn,
        // et vaut false par défaut (CLAUDE.md §10).
        options.User.RequireUniqueEmail = true;
    })
    .AddEntityFrameworkStores<KleanDbContext>()
    // Les descriptions d'erreur d'Identity traversent l'API telles quelles :
    // sans ce descripteur, une interface entièrement française renvoie
    // « Passwords must have at least one digit ».
    .AddErrorDescriber<DescripteurErreursFrancais>()
    .AddDefaultTokenProviders();

// Jeton de réinitialisation : une heure au lieu du jour par défaut. Assez pour
// qu'un courriel arrive et soit lu, assez court pour qu'un lien oublié dans une
// boîte partagée ne reste pas exploitable une journée entière.
builder.Services.Configure<DataProtectionTokenProviderOptions>(options =>
    options.TokenLifespan = TimeSpan.FromHours(1));

// La clé est lue une fois : JwtService est sans état, il sert aussi bien à
// fabriquer les jetons qu'à configurer leur validation.
var jwt = new JwtService(builder.Configuration);
builder.Services.AddSingleton(jwt);
builder.Services.AddScoped<AmorcageService>();
builder.Services.AddScoped<ServiceSession>();

// Singletons : le compteur de débit doit survivre aux requêtes, et la
// configuration SMTP est lue une fois pour toutes.
builder.Services.AddSingleton<LimiteurDebit>();
builder.Services.AddSingleton<IServiceCourriel, ServiceCourrielSmtp>();
builder.Services.AddScoped<AffectationService>();
builder.Services.AddScoped<RemplacementService>();
builder.Services.AddScoped<PlanningService>();
// Résolu depuis RequestServices par JournaliserAccesAttribute : un attribut ne
// reçoit pas d'injection par constructeur.
builder.Services.AddScoped<ServiceJournalAcces>();

// AddIdentity a déjà posé des schémas cookie par défaut, qui redirigent vers
// /Account/Login au lieu de retourner 401. Les trois propriétés doivent être
// fixées explicitement, sinon une requête sans jeton répond 302 (CLAUDE.md §10).
builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        // Vrai partout sauf en développement local. La clé de signature est
        // symétrique, donc aucune métadonnée n'est réellement récupérée sur le
        // réseau aujourd'hui — mais laisser ce drapeau à false, c'est garder
        // ouverte la porte qui servira le jour où l'émetteur deviendra distant.
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();

        options.Events = new JwtBearerEvents
        {
            // Le jeton n'arrive plus par l'en-tête Authorization mais par un
            // cookie HttpOnly, que le script de la page ne peut pas lire. C'est
            // le remplacement du localStorage : une XSS n'emporte plus la session.
            //
            // L'en-tête n'est volontairement plus accepté. Aucun jeton n'est remis
            // au client, donc personne ne pourrait s'en servir — et un second
            // chemin d'authentification est un second chemin à auditer.
            OnMessageReceived = contexte =>
            {
                var jeton = contexte.Request.Cookies[ServiceSession.CookieAcces];

                // ⚠️ Piège : laisser Token vide ne désactive pas l'en-tête
                // Authorization, le gestionnaire y retombe juste après. Poser ce
                // délégué ne suffit donc pas à fermer ce chemin — il faut
                // court-circuiter explicitement (CLAUDE.md §10).
                if (string.IsNullOrEmpty(jeton))
                {
                    contexte.NoResult();
                    return Task.CompletedTask;
                }

                contexte.Token = jeton;
                return Task.CompletedTask;
            },

            // Révocation. Un JWT signé vaut jusqu'à son expiration quoi qu'il
            // arrive côté serveur : sans cette vérification, désactiver un compte
            // le laissait pleinement actif jusqu'à l'échéance du jeton.
            //
            // Le coût est une requête sur AspNetUsers par requête authentifiée.
            // C'est le prix d'une révocation immédiate, et il est assumé : la
            // table est petite, la lecture se fait par clé primaire.
            OnTokenValidated = async contexte =>
            {
                var identifiant = contexte.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);

                if (!Guid.TryParse(identifiant, out _))
                {
                    contexte.Fail("Jeton sans identifiant exploitable.");
                    return;
                }

                var utilisateurs = contexte.HttpContext.RequestServices
                    .GetRequiredService<UserManager<Utilisateur>>();

                var utilisateur = await utilisateurs.FindByIdAsync(identifiant!);

                if (utilisateur is null)
                {
                    contexte.Fail("Compte introuvable.");
                    return;
                }

                if (!utilisateur.Actif)
                {
                    contexte.Fail("Compte désactivé.");
                    return;
                }

                // Identity régénère le SecurityStamp à tout changement sensible :
                // mot de passe, rôles, réinitialisation. Une empreinte périmée
                // signifie que le jeton décrit un état révolu.
                var empreinte = contexte.Principal?
                    .FindFirstValue(JwtService.RevendicationEmpreinteSecurite);

                if (empreinte != utilisateur.SecurityStamp)
                {
                    contexte.Fail("Session invalidée : les droits ou le mot de passe ont changé.");
                }
            }
        };

        options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Emetteur,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = jwt.CleDeSignature(),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            RoleClaimType = ClaimTypes.Role,
            NameClaimType = ClaimTypes.NameIdentifier
        };
    });

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    // AddSecurityDefinition seul suffit à faire apparaître le bouton Authorize.
    // AddSecurityRequirement a changé de signature en OpenAPI 2.x et la
    // documentation en ligne est périmée : ne pas s'acharner (CLAUDE.md §10).
    // L'authentification passe par cookie : dans un navigateur, Swagger les
    // porte tout seul après un POST sur /api/auth/connexion. Ce qui ne s'envoie
    // pas tout seul, c'est l'en-tête anti-CSRF exigé sur les écritures — c'est
    // donc lui, et non un jeton, qu'il faut pouvoir coller ici.
    options.AddSecurityDefinition(ServiceSession.EnteteCsrf, new OpenApiSecurityScheme
    {
        Name = ServiceSession.EnteteCsrf,
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Description =
            "Se connecter d'abord par POST /api/auth/connexion, puis recopier ici "
            + $"la valeur du cookie « {ServiceSession.CookieCsrf} ». "
            + "Nécessaire sur POST, PUT et DELETE uniquement."
    });
});

var app = builder.Build();

// Rôles, puis entreprise et administrateur si aucun utilisateur n'existe.
using (var portee = app.Services.CreateScope())
{
    await portee.ServiceProvider.GetRequiredService<AmorcageService>().ExecuterAsync();

    // Résolution forcée : le constructeur valide la configuration SMTP et
    // journalise son état. Sans cet appel, une configuration incomplète
    // n'échouerait qu'au premier mot de passe oublié — soit au plus mauvais
    // moment possible (SPEC.md étape 12).
    portee.ServiceProvider.GetRequiredService<IServiceCourriel>();
}

// ---------------------------------------------------------------------------
// Pipeline. L'ordre est la fonctionnalité : chaque déplacement de ligne
// ci-dessous change ce qui est protégé, sans rien casser de visible.
// ---------------------------------------------------------------------------

// HSTS et redirection : hors développement seulement. En local le backend
// n'écoute qu'en clair derrière le proxy Vite, et rediriger romprait ce chemin
// sans rien protéger — le saut est en loopback sur la même machine.
//
// HSTS avant la redirection : l'en-tête doit partir sur les réponses HTTPS, ce
// que la redirection ne produit pas.
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseRequestTimeouts();

// Avant tout le reste de la requête : refuser un corps démesuré ne doit rien
// coûter, ni une lecture, ni une liaison de modèle, ni une allocation.
app.UseMiddleware<LimiteCorpsRequete>(tailleMaxCorps);

// Avant tout ce qui lit la session : une requête d'origine interdite n'a pas à
// atteindre la vérification anti-CSRF ni l'authentification.
app.UseCors(PolitiqueCors);

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// ⚠️ Après UseSwaggerUI, délibérément. Voir EnTetesSecurite : la politique
// interdit le script en ligne dont l'interface de Swagger a besoin, et celle-ci
// court-circuite le pipeline avant d'arriver ici.
app.UseMiddleware<EnTetesSecurite>();

// Avant l'authentification : la requête est rejetée sans qu'on ait besoin de
// savoir qui elle prétend être.
app.UseMiddleware<VerificationCsrf>();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Sonde de disponibilité.
//
// Elle interroge la base, et c'est tout l'intérêt : une API qui répond pendant
// que PostgreSQL est injoignable est un processus vivant et un service mort. Un
// orchestrateur qui ne regarde que « le port répond » laisse le trafic arriver
// sur une instance qui rendra 500 à chaque appel.
//
// Anonyme et hors description OpenAPI. La réponse ne dit ni le nom de la base,
// ni l'erreur : une sonde n'a besoin que d'un code de statut, et ce point
// d'entrée est le seul ouvert sans authentification.
app.MapGet("/api/sante", async (KleanDbContext db) =>
{
    var joignable = await db.Database.CanConnectAsync();

    return Results.Json(
        new
        {
            statut = joignable ? "ok" : "degrade",
            baseDeDonnees = joignable ? "joignable" : "injoignable",
            version = "0.1.0"
        },
        statusCode: joignable
            ? StatusCodes.Status200OK
            : StatusCodes.Status503ServiceUnavailable);
})
   .ExcludeFromDescription();

app.Run();
