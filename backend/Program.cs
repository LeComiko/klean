using Klean.Api.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// La chaîne vit dans les user secrets en développement, dans une variable
// d'environnement en production. Jamais dans appsettings.json.
var chaine = builder.Configuration.GetConnectionString("Klean")
    ?? throw new InvalidOperationException(
        "Chaîne de connexion « Klean » absente. Voir README.md, section Installation.");

builder.Services.AddDbContext<KleanDbContext>(options => options.UseNpgsql(chaine));

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();

// Point de contrôle minimal : l'API démarre et répond. Exclu de la
// description OpenAPI pour ne pas ajouter de schéma anonyme aux DTOs.
app.MapGet("/api/sante", () => new { statut = "ok", version = "0.1.0" })
   .ExcludeFromDescription();

app.Run();
