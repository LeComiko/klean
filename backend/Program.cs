using Klean.Api.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// La chaîne vit dans les user secrets en développement, dans une variable
// d'environnement en production. Jamais dans appsettings.json.
var chaine = builder.Configuration.GetConnectionString("Klean")
    ?? throw new InvalidOperationException(
        "Chaîne de connexion « Klean » absente. Voir README.md, section Installation.");

builder.Services.AddDbContext<KleanDbContext>(options => options.UseNpgsql(chaine));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Point de contrôle minimal de l'étape 1 : l'API démarre et répond.
// Les contrôleurs métier arrivent à l'étape 3.
app.MapGet("/api/sante", () => new { statut = "ok", version = "0.1.0" });

app.Run();
