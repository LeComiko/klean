using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Klean.Api.Migrations
{
    /// <inheritdoc />
    public partial class SessionsRevocables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "JetonsRafraichissement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UtilisateurId = table.Column<Guid>(type: "uuid", nullable: false),
                    Empreinte = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DateCreation = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DateExpiration = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DateRevocation = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RemplacePar = table.Column<Guid>(type: "uuid", nullable: true),
                    MotifRevocation = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    AdresseIp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JetonsRafraichissement", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JetonsRafraichissement_AspNetUsers_UtilisateurId",
                        column: x => x.UtilisateurId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_JetonsRafraichissement_Empreinte",
                table: "JetonsRafraichissement",
                column: "Empreinte",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JetonsRafraichissement_UtilisateurId",
                table: "JetonsRafraichissement",
                column: "UtilisateurId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "JetonsRafraichissement");
        }
    }
}
