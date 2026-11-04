using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Klean.Api.Migrations
{
    /// <inheritdoc />
    public partial class ConsommationsParType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConsommationsType",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EntrepriseId = table.Column<Guid>(type: "uuid", nullable: false),
                    TypePrestationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProduitId = table.Column<Guid>(type: "uuid", nullable: false),
                    QuantiteParPassage = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsommationsType", x => x.Id);
                    table.CheckConstraint("CK_ConsommationsType_QuantitePositive", "\"QuantiteParPassage\" > 0");
                    table.ForeignKey(
                        name: "FK_ConsommationsType_Produits_ProduitId",
                        column: x => x.ProduitId,
                        principalTable: "Produits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConsommationsType_TypesPrestation_TypePrestationId",
                        column: x => x.TypePrestationId,
                        principalTable: "TypesPrestation",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConsommationsType_EntrepriseId",
                table: "ConsommationsType",
                column: "EntrepriseId");

            migrationBuilder.CreateIndex(
                name: "IX_ConsommationsType_ProduitId",
                table: "ConsommationsType",
                column: "ProduitId");

            migrationBuilder.CreateIndex(
                name: "IX_ConsommationsType_TypePrestationId_ProduitId",
                table: "ConsommationsType",
                columns: new[] { "TypePrestationId", "ProduitId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConsommationsType");
        }
    }
}
