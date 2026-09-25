using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoryWalker.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BasedOnStory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "BasedOnStoryId",
                table: "Stories",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "BasedOnStoryId",
                table: "Conversations",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Stories_BasedOnStoryId",
                table: "Stories",
                column: "BasedOnStoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_BasedOnStoryId",
                table: "Conversations",
                column: "BasedOnStoryId");

            migrationBuilder.AddForeignKey(
                name: "FK_Conversations_Stories_BasedOnStoryId",
                table: "Conversations",
                column: "BasedOnStoryId",
                principalTable: "Stories",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Stories_Stories_BasedOnStoryId",
                table: "Stories",
                column: "BasedOnStoryId",
                principalTable: "Stories",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Conversations_Stories_BasedOnStoryId",
                table: "Conversations");

            migrationBuilder.DropForeignKey(
                name: "FK_Stories_Stories_BasedOnStoryId",
                table: "Stories");

            migrationBuilder.DropIndex(
                name: "IX_Stories_BasedOnStoryId",
                table: "Stories");

            migrationBuilder.DropIndex(
                name: "IX_Conversations_BasedOnStoryId",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "BasedOnStoryId",
                table: "Stories");

            migrationBuilder.DropColumn(
                name: "BasedOnStoryId",
                table: "Conversations");
        }
    }
}
