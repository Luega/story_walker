using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoryWalker.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StoryConversation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ConversationId",
                table: "Stories",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Stories_ConversationId",
                table: "Stories",
                column: "ConversationId");

            migrationBuilder.AddForeignKey(
                name: "FK_Stories_Conversations_ConversationId",
                table: "Stories",
                column: "ConversationId",
                principalTable: "Conversations",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Stories_Conversations_ConversationId",
                table: "Stories");

            migrationBuilder.DropIndex(
                name: "IX_Stories_ConversationId",
                table: "Stories");

            migrationBuilder.DropColumn(
                name: "ConversationId",
                table: "Stories");
        }
    }
}
