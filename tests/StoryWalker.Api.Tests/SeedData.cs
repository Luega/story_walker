using Microsoft.EntityFrameworkCore;
using StoryWalker.Domain.Entities;
using StoryWalker.Domain.Enums;
using StoryWalker.Infrastructure.Persistence;

namespace StoryWalker.Api.Tests;

/// <summary>Test data builders that write directly to the database via a DbContext.</summary>
internal static class SeedData
{
    /// <summary>Inserts the standard demo graph (user → 2 characters → story → 3 pages → links).</summary>
    public static async Task<Guid> SeedDemoStoryAsync(StoryWalkerDbContext db)
    {
        await DbSeeder.SeedAsync(db);
        return await db.Stories.Select(s => s.Id).FirstAsync();
    }

    /// <summary>
    /// Inserts two stories where the second reuses the first (<see cref="Story.BasedOnStoryId"/>),
    /// so deleting the base hits the Restrict FK and yields a 409. Returns (base, dependent) ids.
    /// </summary>
    public static async Task<(Guid BaseStoryId, Guid DependentStoryId)> SeedBasedOnPairAsync(
        StoryWalkerDbContext db
    )
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            ClerkUserId = $"user_{Guid.NewGuid():N}",
            Email = "based-on@storywalker.app",
        };

        var baseStory = NewStory(user.Id, "Original story");
        var dependent = NewStory(user.Id, "Reuse of the original");
        dependent.BasedOnStoryId = baseStory.Id;

        db.Users.Add(user);
        db.Stories.AddRange(baseStory, dependent);
        await db.SaveChangesAsync();

        return (baseStory.Id, dependent.Id);
    }

    private static Story NewStory(Guid userId, string title) =>
        new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Title = title,
            Summary = "A short test summary.",
            Status = StoryStatus.Complete,
            ArtStyle = "soft watercolour",
            Palette = "warm yellows",
        };
}
