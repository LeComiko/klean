using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Klean.Api.Migrations
{
    /// <inheritdoc />
    public partial class CompteRenduDuPassage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Anomalies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EntrepriseId = table.Column<Guid>(type: "uuid", nullable: false),
                    InterventionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Statut = table.Column<string>(type: "text", nullable: false),
                    DateSignalement = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AuteurId = table.Column<Guid>(type: "uuid", nullable: false),
                    DateTraitement = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TraiteeParId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Anomalies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Anomalies_Interventions_InterventionId",
                        column: x => x.InterventionId,
                        principalTable: "Interventions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PointsControle",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EntrepriseId = table.Column<Guid>(type: "uuid", nullable: false),
                    TypePrestationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Libelle = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Ordre = table.Column<int>(type: "integer", nullable: false),
                    Actif = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PointsControle", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PointsControle_TypesPrestation_TypePrestationId",
                        column: x => x.TypePrestationId,
                        principalTable: "TypesPrestation",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PhotosAnomalie",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EntrepriseId = table.Column<Guid>(type: "uuid", nullable: false),
                    AnomalieId = table.Column<Guid>(type: "uuid", nullable: false),
                    Contenu = table.Column<byte[]>(type: "bytea", nullable: false),
                    Taille = table.Column<int>(type: "integer", nullable: false),
                    DateAjout = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhotosAnomalie", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PhotosAnomalie_Anomalies_AnomalieId",
                        column: x => x.AnomalieId,
                        principalTable: "Anomalies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ControlesEffectues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EntrepriseId = table.Column<Guid>(type: "uuid", nullable: false),
                    InterventionId = table.Column<Guid>(type: "uuid", nullable: false),
                    PointControleId = table.Column<Guid>(type: "uuid", nullable: true),
                    Libelle = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Ordre = table.Column<int>(type: "integer", nullable: false),
                    Fait = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ControlesEffectues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ControlesEffectues_Interventions_InterventionId",
                        column: x => x.InterventionId,
                        principalTable: "Interventions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ControlesEffectues_PointsControle_PointControleId",
                        column: x => x.PointControleId,
                        principalTable: "PointsControle",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Anomalies_EntrepriseId_DateSignalement",
                table: "Anomalies",
                columns: new[] { "EntrepriseId", "DateSignalement" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_Anomalies_InterventionId",
                table: "Anomalies",
                column: "InterventionId");

            migrationBuilder.CreateIndex(
                name: "IX_ControlesEffectues_EntrepriseId",
                table: "ControlesEffectues",
                column: "EntrepriseId");

            migrationBuilder.CreateIndex(
                name: "IX_ControlesEffectues_InterventionId",
                table: "ControlesEffectues",
                column: "InterventionId");

            migrationBuilder.CreateIndex(
                name: "IX_ControlesEffectues_PointControleId",
                table: "ControlesEffectues",
                column: "PointControleId");

            migrationBuilder.CreateIndex(
                name: "IX_PhotosAnomalie_AnomalieId",
                table: "PhotosAnomalie",
                column: "AnomalieId");

            migrationBuilder.CreateIndex(
                name: "IX_PhotosAnomalie_EntrepriseId",
                table: "PhotosAnomalie",
                column: "EntrepriseId");

            migrationBuilder.CreateIndex(
                name: "IX_PointsControle_EntrepriseId",
                table: "PointsControle",
                column: "EntrepriseId");

            migrationBuilder.CreateIndex(
                name: "IX_PointsControle_TypePrestationId",
                table: "PointsControle",
                column: "TypePrestationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ControlesEffectues");

            migrationBuilder.DropTable(
                name: "PhotosAnomalie");

            migrationBuilder.DropTable(
                name: "PointsControle");

            migrationBuilder.DropTable(
                name: "Anomalies");
        }
    }
}
