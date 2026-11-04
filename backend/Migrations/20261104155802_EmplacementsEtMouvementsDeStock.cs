using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Klean.Api.Migrations
{
    /// <inheritdoc />
    public partial class EmplacementsEtMouvementsDeStock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ⚠️ Les anciennes lignes visaient un dépôt ou un véhicule par un Guid que rien ne
            // nommait (SPEC.md, étape 22) : aucune clé étrangère ne peut les reprendre. La
            // table était vide partout à l'écriture de cette migration. Sur une instance qui
            // en aurait, mieux vaut refuser net, avec la raison, que de s'arrêter sur une
            // clé étrangère au milieu des opérations.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "Stocks") THEN
                        RAISE EXCEPTION 'La table Stocks n''est pas vide : ses emplacements ne sont pas reprenables automatiquement (SPEC.md, étape 22). Relever les quantités, vider la table, appliquer la migration, puis les ressaisir par des mouvements d''inventaire.';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropIndex(
                name: "IX_Stocks_ProduitId",
                table: "Stocks");

            migrationBuilder.DropColumn(
                name: "TypeEmplacement",
                table: "Stocks");

            migrationBuilder.AlterColumn<Guid>(
                name: "EmplacementId",
                table: "Stocks",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "SiteId",
                table: "Stocks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Actif",
                table: "Produits",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "Emplacements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EntrepriseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Nom = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    Actif = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Emplacements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Emplacements_Entreprises_EntrepriseId",
                        column: x => x.EntrepriseId,
                        principalTable: "Entreprises",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MouvementsStock",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EntrepriseId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProduitId = table.Column<Guid>(type: "uuid", nullable: false),
                    Nature = table.Column<string>(type: "text", nullable: false),
                    OrigineEmplacementId = table.Column<Guid>(type: "uuid", nullable: true),
                    OrigineSiteId = table.Column<Guid>(type: "uuid", nullable: true),
                    DestinationEmplacementId = table.Column<Guid>(type: "uuid", nullable: true),
                    DestinationSiteId = table.Column<Guid>(type: "uuid", nullable: true),
                    Quantite = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    Ecart = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    DateMouvement = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AuteurId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MouvementsStock", x => x.Id);
                    table.CheckConstraint("CK_MouvementsStock_UneDestination", "\"DestinationEmplacementId\" IS NULL OR \"DestinationSiteId\" IS NULL");
                    table.CheckConstraint("CK_MouvementsStock_UneOrigine", "\"OrigineEmplacementId\" IS NULL OR \"OrigineSiteId\" IS NULL");
                    table.ForeignKey(
                        name: "FK_MouvementsStock_Emplacements_DestinationEmplacementId",
                        column: x => x.DestinationEmplacementId,
                        principalTable: "Emplacements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MouvementsStock_Emplacements_OrigineEmplacementId",
                        column: x => x.OrigineEmplacementId,
                        principalTable: "Emplacements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MouvementsStock_Produits_ProduitId",
                        column: x => x.ProduitId,
                        principalTable: "Produits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MouvementsStock_Sites_DestinationSiteId",
                        column: x => x.DestinationSiteId,
                        principalTable: "Sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MouvementsStock_Sites_OrigineSiteId",
                        column: x => x.OrigineSiteId,
                        principalTable: "Sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Stocks_EmplacementId",
                table: "Stocks",
                column: "EmplacementId");

            migrationBuilder.CreateIndex(
                name: "IX_Stocks_ProduitId_EmplacementId",
                table: "Stocks",
                columns: new[] { "ProduitId", "EmplacementId" },
                unique: true,
                filter: "\"EmplacementId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Stocks_ProduitId_SiteId",
                table: "Stocks",
                columns: new[] { "ProduitId", "SiteId" },
                unique: true,
                filter: "\"SiteId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Stocks_SiteId",
                table: "Stocks",
                column: "SiteId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Stocks_QuantitePositive",
                table: "Stocks",
                sql: "\"Quantite\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Stocks_UnLieu",
                table: "Stocks",
                sql: "(\"EmplacementId\" IS NULL) <> (\"SiteId\" IS NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_Emplacements_EntrepriseId",
                table: "Emplacements",
                column: "EntrepriseId");

            migrationBuilder.CreateIndex(
                name: "IX_MouvementsStock_DestinationEmplacementId",
                table: "MouvementsStock",
                column: "DestinationEmplacementId");

            migrationBuilder.CreateIndex(
                name: "IX_MouvementsStock_DestinationSiteId",
                table: "MouvementsStock",
                column: "DestinationSiteId");

            migrationBuilder.CreateIndex(
                name: "IX_MouvementsStock_EntrepriseId_DateMouvement",
                table: "MouvementsStock",
                columns: new[] { "EntrepriseId", "DateMouvement" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_MouvementsStock_OrigineEmplacementId",
                table: "MouvementsStock",
                column: "OrigineEmplacementId");

            migrationBuilder.CreateIndex(
                name: "IX_MouvementsStock_OrigineSiteId",
                table: "MouvementsStock",
                column: "OrigineSiteId");

            migrationBuilder.CreateIndex(
                name: "IX_MouvementsStock_ProduitId",
                table: "MouvementsStock",
                column: "ProduitId");

            migrationBuilder.AddForeignKey(
                name: "FK_Stocks_Emplacements_EmplacementId",
                table: "Stocks",
                column: "EmplacementId",
                principalTable: "Emplacements",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Stocks_Sites_SiteId",
                table: "Stocks",
                column: "SiteId",
                principalTable: "Sites",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Stocks_Emplacements_EmplacementId",
                table: "Stocks");

            migrationBuilder.DropForeignKey(
                name: "FK_Stocks_Sites_SiteId",
                table: "Stocks");

            migrationBuilder.DropTable(
                name: "MouvementsStock");

            migrationBuilder.DropTable(
                name: "Emplacements");

            migrationBuilder.DropIndex(
                name: "IX_Stocks_EmplacementId",
                table: "Stocks");

            migrationBuilder.DropIndex(
                name: "IX_Stocks_ProduitId_EmplacementId",
                table: "Stocks");

            migrationBuilder.DropIndex(
                name: "IX_Stocks_ProduitId_SiteId",
                table: "Stocks");

            migrationBuilder.DropIndex(
                name: "IX_Stocks_SiteId",
                table: "Stocks");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Stocks_QuantitePositive",
                table: "Stocks");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Stocks_UnLieu",
                table: "Stocks");

            migrationBuilder.DropColumn(
                name: "SiteId",
                table: "Stocks");

            migrationBuilder.DropColumn(
                name: "Actif",
                table: "Produits");

            migrationBuilder.AlterColumn<Guid>(
                name: "EmplacementId",
                table: "Stocks",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TypeEmplacement",
                table: "Stocks",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_Stocks_ProduitId",
                table: "Stocks",
                column: "ProduitId");
        }
    }
}
