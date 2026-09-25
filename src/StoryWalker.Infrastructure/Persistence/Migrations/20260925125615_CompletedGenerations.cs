using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoryWalker.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CompletedGenerations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CompletedGenerations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    StoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompletedGenerations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompletedGenerations_Stories_StoryId",
                        column: x => x.StoryId,
                        principalTable: "Stories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_CompletedGenerations_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CompletedGenerations_StoryId",
                table: "CompletedGenerations",
                column: "StoryId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompletedGenerations_UserId_CompletedAt",
                table: "CompletedGenerations",
                columns: new[] { "UserId", "CompletedAt" });

            // Stories finished before the ledger existed still count this week (ADR-0030). Their
            // completion time was never recorded; the last update is the closest thing to it.
            migrationBuilder.Sql(
                """
                INSERT INTO "CompletedGenerations" ("Id", "UserId", "StoryId", "CompletedAt")
                SELECT gen_random_uuid(), "UserId", "Id", COALESCE("UpdatedAt", "CreatedAt")
                FROM "Stories"
                WHERE "Status" = 'Complete';
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CompletedGenerations");
        }
    }
}
