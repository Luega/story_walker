using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoryWalker.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UserStoryContext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Nullable with no default, unlike the Tier column: "not answered yet" is genuinely
            // absent here, and an empty string would read as an answer the user never gave.
            migrationBuilder.AddColumn<string>(
                name: "ArtStyleContext",
                table: "Users",
                type: "character varying(600)",
                maxLength: 600,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CharactersContext",
                table: "Users",
                type: "character varying(600)",
                maxLength: 600,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KidsContext",
                table: "Users",
                type: "character varying(600)",
                maxLength: 600,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StoryContext",
                table: "Users",
                type: "character varying(600)",
                maxLength: 600,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ArtStyleContext",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "CharactersContext",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "KidsContext",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "StoryContext",
                table: "Users");
        }
    }
}
