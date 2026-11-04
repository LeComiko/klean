using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Klean.Api.Migrations
{
    /// <inheritdoc />
    public partial class TerritoireDeLEntreprise : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Territoire",
                table: "Entreprises",
                type: "text",
                nullable: false,
                defaultValueSql: "'Metropole'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Territoire",
                table: "Entreprises");
        }
    }
}
