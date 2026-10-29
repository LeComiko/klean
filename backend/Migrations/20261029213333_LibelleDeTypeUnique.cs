using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Klean.Api.Migrations
{
    /// <summary>
    /// Un libellé de type est unique dans son entreprise, à la casse près (SPEC.md, étape 21,
    /// lot B). Le contrôleur le vérifie déjà avant d'écrire ; l'index tranche quand deux
    /// écritures se croisent entre la lecture et l'insertion.
    ///
    /// En SQL brut : EF ne sait pas décrire un index sur expression, donc le modèle et
    /// l'instantané n'en savent rien. Purement additif.
    /// </summary>
    public partial class LibelleDeTypeUnique : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX "IX_TypesPrestation_EntrepriseId_Libelle_Casse"
                    ON "TypesPrestation" ("EntrepriseId", lower("Libelle"));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP INDEX "IX_TypesPrestation_EntrepriseId_Libelle_Casse";""");
        }
    }
}
