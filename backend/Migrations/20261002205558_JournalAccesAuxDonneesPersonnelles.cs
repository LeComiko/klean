using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Klean.Api.Migrations
{
    /// <inheritdoc />
    public partial class JournalAccesAuxDonneesPersonnelles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccesJournalises",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EntrepriseId = table.Column<Guid>(type: "uuid", nullable: false),
                    UtilisateurId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ressource = table.Column<string>(type: "text", nullable: false),
                    Nature = table.Column<string>(type: "text", nullable: false),
                    CibleId = table.Column<Guid>(type: "uuid", nullable: true),
                    Lignes = table.Column<int>(type: "integer", nullable: false),
                    Horodatage = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccesJournalises", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccesJournalises_EntrepriseId_Horodatage",
                table: "AccesJournalises",
                columns: new[] { "EntrepriseId", "Horodatage" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccesJournalises");
        }
    }
}
