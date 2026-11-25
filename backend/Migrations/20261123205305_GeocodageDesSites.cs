using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Klean.Api.Migrations
{
    /// <inheritdoc />
    public partial class GeocodageDesSites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "GeocodageActif",
                table: "Entreprises",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "GeocodageActiveLe",
                table: "Entreprises",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "GeocodageDesactiveLe",
                table: "Entreprises",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GeocodageActif",
                table: "Entreprises");

            migrationBuilder.DropColumn(
                name: "GeocodageActiveLe",
                table: "Entreprises");

            migrationBuilder.DropColumn(
                name: "GeocodageDesactiveLe",
                table: "Entreprises");
        }
    }
}
