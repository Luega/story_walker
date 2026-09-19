using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoryWalker.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UserTier : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // "Free" rather than the empty string EF scaffolds: every user predating the column
            // is on the free plan, and an empty string is not a UserTier at all.
            migrationBuilder.AddColumn<string>(
                name: "Tier",
                table: "Users",
                type: "text",
                nullable: false,
                defaultValue: "Free");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Tier",
                table: "Users");
        }
    }
}
