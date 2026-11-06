using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Klean.Api.Migrations
{
    /// <inheritdoc />
    public partial class MotDePasseProvisoire : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "MotDePasseProvisoire",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MotDePasseProvisoire",
                table: "AspNetUsers");
        }
    }
}
