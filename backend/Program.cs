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
    .AddDefaultTokenProviders();

// La clé est lue une fois : JwtService est sans état, il sert aussi bien à
// fabriquer les jetons qu'à configurer leur validation.
var jwt = new JwtService(builder.Configuration);
builder.Services.AddSingleton(jwt);
builder.Services.AddScoped<AmorcageService>();
builder.Services.AddScoped<ServiceSession>();
builder.Services.AddScoped<AffectationService>();
builder.Services.AddScoped<RemplacementService>();
builder.Services.AddScoped<PlanningService>();

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
        options.RequireHttpsMetadata = false;

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
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Avant l'authentification : la requête est rejetée sans qu'on ait besoin de
// savoir qui elle prétend être.
app.UseMiddleware<VerificationCsrf>();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Point de contrôle minimal : l'API démarre et répond. Exclu de la
// description OpenAPI pour ne pas ajouter de schéma anonyme aux DTOs.
app.MapGet("/api/sante", () => new { statut = "ok", version = "0.1.0" })
   .ExcludeFromDescription();

app.Run();
