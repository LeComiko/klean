using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Klean.Api.Migrations
{
    /// <inheritdoc />
    public partial class Pointages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Pointages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EntrepriseId = table.Column<Guid>(type: "uuid", nullable: false),
                    InterventionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Moment = table.Column<string>(type: "text", nullable: false),
                    Horodatage = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Preuve = table.Column<string>(type: "text", nullable: false),
                    DistanceMetres = table.Column<int>(type: "integer", nullable: true),
                    PrecisionMetres = table.Column<int>(type: "integer", nullable: true),
                    AuteurId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Pointages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Pointages_Interventions_InterventionId",
                        column: x => x.InterventionId,
                        principalTable: "Interventions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Pointages_EntrepriseId_Horodatage",
                table: "Pointages",
                columns: new[] { "EntrepriseId", "Horodatage" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_Pointages_InterventionId_Moment",
                table: "Pointages",
                columns: new[] { "InterventionId", "Moment" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Pointages");
        }
    }
}
