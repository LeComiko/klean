using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Klean.Api.Migrations
{
    /// <inheritdoc />
    public partial class RattachementChefEquipe : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ChefEquipeId",
                table: "Agents",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Agents_ChefEquipeId",
                table: "Agents",
                column: "ChefEquipeId");

            migrationBuilder.AddForeignKey(
                name: "FK_Agents_AspNetUsers_ChefEquipeId",
                table: "Agents",
                column: "ChefEquipeId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Agents_AspNetUsers_ChefEquipeId",
                table: "Agents");

            migrationBuilder.DropIndex(
                name: "IX_Agents_ChefEquipeId",
                table: "Agents");

            migrationBuilder.DropColumn(
                name: "ChefEquipeId",
                table: "Agents");
        }
    }
}
