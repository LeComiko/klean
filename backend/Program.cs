using System.Security.Claims;
using Klean.Api.Data;
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
builder.Services.AddScoped<AffectationService>();

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
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Scheme = "Bearer",
        Description = "Coller « Bearer <jeton> », jeton obtenu sur /api/auth/connexion."
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

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Point de contrôle minimal : l'API démarre et répond. Exclu de la
// description OpenAPI pour ne pas ajouter de schéma anonyme aux DTOs.
app.MapGet("/api/sante", () => new { statut = "ok", version = "0.1.0" })
   .ExcludeFromDescription();

app.Run();
