using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Klean.Api.Migrations
{
    /// <inheritdoc />
    public partial class RecurrenceDesPrestations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AgentTitulaireId",
                table: "Prestations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "HeureDebut",
                table: "Prestations",
                type: "interval",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Jours",
                table: "Prestations",
                type: "text",
                nullable: false,
                defaultValueSql: "'Aucun'");

            migrationBuilder.AddColumn<bool>(
                name: "InclutJoursFeries",
                table: "Contrats",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Prestations_AgentTitulaireId",
                table: "Prestations",
                column: "AgentTitulaireId");

            migrationBuilder.AddForeignKey(
                name: "FK_Prestations_Agents_AgentTitulaireId",
                table: "Prestations",
                column: "AgentTitulaireId",
                principalTable: "Agents",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Prestations_Agents_AgentTitulaireId",
                table: "Prestations");

            migrationBuilder.DropIndex(
                name: "IX_Prestations_AgentTitulaireId",
                table: "Prestations");

            migrationBuilder.DropColumn(
                name: "AgentTitulaireId",
                table: "Prestations");

            migrationBuilder.DropColumn(
                name: "HeureDebut",
                table: "Prestations");

            migrationBuilder.DropColumn(
                name: "Jours",
                table: "Prestations");

            migrationBuilder.DropColumn(
                name: "InclutJoursFeries",
                table: "Contrats");
        }
    }
}
