using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Klean.Api.Migrations
{
    /// <inheritdoc />
    public partial class CatalogueDesPrestations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TypePrestationId",
                table: "Prestations",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TypesPrestation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EntrepriseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Libelle = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DureeParDefautMinutes = table.Column<int>(type: "integer", nullable: false),
                    Actif = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TypesPrestation", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TypesPrestation_Entreprises_EntrepriseId",
                        column: x => x.EntrepriseId,
                        principalTable: "Entreprises",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Prestations_TypePrestationId",
                table: "Prestations",
                column: "TypePrestationId");

            migrationBuilder.CreateIndex(
                name: "IX_TypesPrestation_EntrepriseId",
                table: "TypesPrestation",
                column: "EntrepriseId");

            migrationBuilder.AddForeignKey(
                name: "FK_Prestations_TypesPrestation_TypePrestationId",
                table: "Prestations",
                column: "TypePrestationId",
                principalTable: "TypesPrestation",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Prestations_TypesPrestation_TypePrestationId",
                table: "Prestations");

            migrationBuilder.DropTable(
                name: "TypesPrestation");

            migrationBuilder.DropIndex(
                name: "IX_Prestations_TypePrestationId",
                table: "Prestations");

            migrationBuilder.DropColumn(
                name: "TypePrestationId",
                table: "Prestations");
        }
    }
}
