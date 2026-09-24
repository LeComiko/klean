var builder = WebApplication.CreateBuilder(args);

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
