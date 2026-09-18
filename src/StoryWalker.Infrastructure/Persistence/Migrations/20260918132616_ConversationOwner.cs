using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoryWalker.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ConversationOwner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Conversations predating ownership have no user to attribute them to, and an
            // unfinished interview is worth nothing once abandoned. Messages cascade. Without this
            // the NULLs would be backfilled with the zero Guid below and the foreign key would fail.
            migrationBuilder.Sql("DELETE FROM \"Conversations\";");

            migrationBuilder.AlterColumn<Guid>(
                name: "UserId",
                table: "Conversations",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_UserId",
                table: "Conversations",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Conversations_Users_UserId",
                table: "Conversations",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Conversations_Users_UserId",
                table: "Conversations");

            migrationBuilder.DropIndex(
                name: "IX_Conversations_UserId",
                table: "Conversations");

            migrationBuilder.AlterColumn<Guid>(
                name: "UserId",
                table: "Conversations",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");
        }
    }
}
