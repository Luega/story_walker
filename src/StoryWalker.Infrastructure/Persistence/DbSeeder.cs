using Microsoft.EntityFrameworkCore;
using StoryWalker.Domain.Entities;
using StoryWalker.Domain.Enums;

namespace StoryWalker.Infrastructure.Persistence;

/// <summary>
/// Seeds demo data for local development so the API returns something meaningful out of the box.
/// Idempotent: does nothing once the database already has a user. Timestamps are stamped
/// automatically by <see cref="StoryWalkerDbContext"/>'s SaveChanges override.
/// <para>
/// The seeded user is not only demo data: until Clerk auth lands (ADR-0002) it is the row
/// <c>StartGenerationAsync</c> picks as the owner of every new story.
/// </para>
/// Note how each page's illustration prompt restates the characters' appearance in full. That
/// repetition is the whole consistency mechanism now that per-character reference images are
/// gone (ADR-0019), so the seed data models the shape the agent is asked to produce.
/// </summary>
public static class DbSeeder
{
    private const string Cast =
        "Luna, a round-faced girl with red curly hair in a yellow raincoat, "
        + "and Pip, a small grey kitten with bright green eyes";

    public static async Task SeedAsync(StoryWalkerDbContext db)
    {
        if (await db.Users.AnyAsync())
        {
            return;
        }

        var user = new User
        {
            Id = Guid.NewGuid(),
            ClerkUserId = "user_demo_seed",
            Email = "demo@storywalker.app",
        };

        var story = new Story
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Title = "Luna and the Rainy Day Adventure",
            TargetAge = 5,
            Status = StoryStatus.Complete,
            ArtStyle = "soft watercolour, warm yellows and rainy blues",
        };

        var pages = new[]
        {
            NewPage(story.Id, 1,
                "Luna pressed her nose to the window. Outside, the rain drew silver lines down the glass.",
                $"{Cast}. Luna at a window watching the rain, Pip curled at her feet."),
            NewPage(story.Id, 2,
                "\"Come on, Pip!\" she said, pulling on her yellow raincoat. The puddles were waiting.",
                $"{Cast}. Luna by the front door in her raincoat, Pip looking up at her."),
            NewPage(story.Id, 3,
                "The first puddle was small. The second was bigger. The third went all the way over her boots.",
                $"{Cast}. Luna jumping into a wide puddle, Pip springing back from the splash."),
            NewPage(story.Id, 4,
                "Pip did not like the water at all, so Luna carried him in the crook of her arm.",
                $"{Cast}. Luna walking down a wet street carrying Pip against her chest."),
            NewPage(story.Id, 5,
                "Then the rain stopped, all at once, the way it sometimes does.",
                $"{Cast}. Luna and Pip standing still on a glistening pavement, looking up at a clearing sky."),
            NewPage(story.Id, 6,
                "A rainbow stretched over the whole street, and Luna decided rainy days were her favourite after all.",
                $"{Cast}. Luna and Pip beneath a wide rainbow arching over the rooftops."),
        };

        db.Users.Add(user);
        db.Stories.Add(story);
        db.Pages.AddRange(pages);

        await db.SaveChangesAsync();
    }

    private static Page NewPage(Guid storyId, int pageNo, string content, string illustrationPrompt) =>
        new()
        {
            Id = Guid.NewGuid(),
            StoryId = storyId,
            PageNo = pageNo,
            Content = content,
            IllustrationPrompt = illustrationPrompt,
            Status = PageStatus.Complete,
        };
}
