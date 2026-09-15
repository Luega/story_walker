using Microsoft.EntityFrameworkCore;
using StoryWalker.Infrastructure.Persistence;

namespace StoryWalker.Api.Tests;

/// <summary>Test data builders that write directly to the database via a DbContext.</summary>
internal static class SeedData
{
    /// <summary>Inserts the standard demo graph (user -> story -> 6 pages).</summary>
    public static async Task<Guid> SeedDemoStoryAsync(StoryWalkerDbContext db)
    {
        await DbSeeder.SeedAsync(db);
        return await db.Stories.Select(s => s.Id).FirstAsync();
    }
}
