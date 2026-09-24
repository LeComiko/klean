using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Klean.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Entreprises",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RaisonSociale = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Siret = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: false),
                    DateCreation = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Entreprises", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Agents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EntrepriseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Nom = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Prenom = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Telephone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Adresse = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Latitude = table.Column<double>(type: "double precision", nullable: true),
                    Longitude = table.Column<double>(type: "double precision", nullable: true),
                    DateEmbauche = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Actif = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Agents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Agents_Entreprises_EntrepriseId",
                        column: x => x.EntrepriseId,
                        principalTable: "Entreprises",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Clients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EntrepriseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Nom = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    Adresse = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CodePostal = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    Ville = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Telephone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Clients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Clients_Entreprises_EntrepriseId",
                        column: x => x.EntrepriseId,
                        principalTable: "Entreprises",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Produits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EntrepriseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Nom = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Reference = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Unite = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    SeuilAlerte = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Produits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Produits_Entreprises_EntrepriseId",
                        column: x => x.EntrepriseId,
                        principalTable: "Entreprises",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Absences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EntrepriseId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    DateDebut = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DateFin = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Motif = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Absences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Absences_Agents_AgentId",
                        column: x => x.AgentId,
                        principalTable: "Agents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Sites",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EntrepriseId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientId = table.Column<Guid>(type: "uuid", nullable: false),
                    Nom = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Adresse = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CodePostal = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    Ville = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Latitude = table.Column<double>(type: "double precision", nullable: false),
                    Longitude = table.Column<double>(type: "double precision", nullable: false),
                    Consignes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sites", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Sites_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Stocks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EntrepriseId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProduitId = table.Column<Guid>(type: "uuid", nullable: false),
                    TypeEmplacement = table.Column<string>(type: "text", nullable: false),
                    EmplacementId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantite = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Stocks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Stocks_Produits_ProduitId",
                        column: x => x.ProduitId,
                        principalTable: "Produits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Contrats",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EntrepriseId = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    DateDebut = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DateFin = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Frequence = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Actif = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Contrats", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Contrats_Sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "Sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Prestations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EntrepriseId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContratId = table.Column<Guid>(type: "uuid", nullable: false),
                    Libelle = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DureeEstimeeMinutes = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Prestations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Prestations_Contrats_ContratId",
                        column: x => x.ContratId,
                        principalTable: "Contrats",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Interventions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EntrepriseId = table.Column<Guid>(type: "uuid", nullable: false),
                    PrestationId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentId = table.Column<Guid>(type: "uuid", nullable: true),
                    DatePrevue = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    HeurePrevue = table.Column<TimeSpan>(type: "interval", nullable: false),
                    Statut = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Interventions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Interventions_Agents_AgentId",
                        column: x => x.AgentId,
                        principalTable: "Agents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Interventions_Prestations_PrestationId",
                        column: x => x.PrestationId,
                        principalTable: "Prestations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Remplacements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EntrepriseId = table.Column<Guid>(type: "uuid", nullable: false),
                    InterventionId = table.Column<Guid>(type: "uuid", nullable: false),
                    AbsenceId = table.Column<Guid>(type: "uuid", nullable: true),
                    AgentRemplacantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Statut = table.Column<string>(type: "text", nullable: false),
                    Score = table.Column<double>(type: "double precision", nullable: true),
                    DateCreation = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Remplacements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Remplacements_Absences_AbsenceId",
                        column: x => x.AbsenceId,
                        principalTable: "Absences",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Remplacements_Agents_AgentRemplacantId",
                        column: x => x.AgentRemplacantId,
                        principalTable: "Agents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Remplacements_Interventions_InterventionId",
                        column: x => x.InterventionId,
                        principalTable: "Interventions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Absences_AgentId",
                table: "Absences",
                column: "AgentId");

            migrationBuilder.CreateIndex(
                name: "IX_Absences_EntrepriseId",
                table: "Absences",
                column: "EntrepriseId");

            migrationBuilder.CreateIndex(
                name: "IX_Agents_EntrepriseId",
                table: "Agents",
                column: "EntrepriseId");

            migrationBuilder.CreateIndex(
                name: "IX_Clients_EntrepriseId",
                table: "Clients",
                column: "EntrepriseId");

            migrationBuilder.CreateIndex(
                name: "IX_Contrats_EntrepriseId",
                table: "Contrats",
                column: "EntrepriseId");

            migrationBuilder.CreateIndex(
                name: "IX_Contrats_SiteId",
                table: "Contrats",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_Entreprises_Siret",
                table: "Entreprises",
                column: "Siret",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Interventions_AgentId",
                table: "Interventions",
                column: "AgentId");

            migrationBuilder.CreateIndex(
                name: "IX_Interventions_EntrepriseId_DatePrevue",
                table: "Interventions",
                columns: new[] { "EntrepriseId", "DatePrevue" });

            migrationBuilder.CreateIndex(
                name: "IX_Interventions_PrestationId",
                table: "Interventions",
                column: "PrestationId");

            migrationBuilder.CreateIndex(
                name: "IX_Prestations_ContratId",
                table: "Prestations",
                column: "ContratId");

            migrationBuilder.CreateIndex(
                name: "IX_Prestations_EntrepriseId",
                table: "Prestations",
                column: "EntrepriseId");

            migrationBuilder.CreateIndex(
                name: "IX_Produits_EntrepriseId",
                table: "Produits",
                column: "EntrepriseId");

            migrationBuilder.CreateIndex(
                name: "IX_Remplacements_AbsenceId",
                table: "Remplacements",
                column: "AbsenceId");

            migrationBuilder.CreateIndex(
                name: "IX_Remplacements_AgentRemplacantId",
                table: "Remplacements",
                column: "AgentRemplacantId");

            migrationBuilder.CreateIndex(
                name: "IX_Remplacements_EntrepriseId",
                table: "Remplacements",
                column: "EntrepriseId");

            migrationBuilder.CreateIndex(
                name: "IX_Remplacements_InterventionId",
                table: "Remplacements",
                column: "InterventionId");

            migrationBuilder.CreateIndex(
                name: "IX_Sites_ClientId",
                table: "Sites",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_Sites_EntrepriseId",
                table: "Sites",
                column: "EntrepriseId");

            migrationBuilder.CreateIndex(
                name: "IX_Stocks_EntrepriseId",
                table: "Stocks",
                column: "EntrepriseId");

            migrationBuilder.CreateIndex(
                name: "IX_Stocks_ProduitId",
                table: "Stocks",
                column: "ProduitId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Remplacements");

            migrationBuilder.DropTable(
                name: "Stocks");

            migrationBuilder.DropTable(
                name: "Absences");

            migrationBuilder.DropTable(
                name: "Interventions");

            migrationBuilder.DropTable(
                name: "Produits");

            migrationBuilder.DropTable(
                name: "Agents");

            migrationBuilder.DropTable(
                name: "Prestations");

            migrationBuilder.DropTable(
                name: "Contrats");

            migrationBuilder.DropTable(
                name: "Sites");

            migrationBuilder.DropTable(
                name: "Clients");

            migrationBuilder.DropTable(
                name: "Entreprises");
        }
    }
}
