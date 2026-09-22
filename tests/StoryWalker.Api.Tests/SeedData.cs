using Microsoft.EntityFrameworkCore;
using StoryWalker.Application.Api;
using StoryWalker.Domain.Entities;
using StoryWalker.Domain.Enums;
using StoryWalker.Infrastructure.Persistence;

namespace StoryWalker.Api.Tests;

/// <summary>
/// Test data builders that write directly to the database via a DbContext. This used to be
/// Infrastructure's DbSeeder, seeding demo data at API startup; once every story has a real owner
/// (ADR-0002) a story owned by a demo user is invisible to every account, so the tests became its
/// only consumer and it moved here.
/// <para>
/// Note how each page's illustration prompt restates the characters' appearance in full. That
/// repetition is the whole consistency mechanism now that per-character reference images are gone
/// (ADR-0019), so the seed data models the shape the agent is asked to produce.
/// </para>
/// </summary>
internal static class SeedData
{
    private const string Cast =
        "Luna, a round-faced girl with red curly hair in a yellow raincoat, "
        + "and Pip, a small grey kitten with bright green eyes";

    /// <summary>
    /// Inserts a user for the given Clerk subject and returns its id. Pass
    /// <paramref name="settings"/> to give them the standing story context the agent composes into
    /// the system prompt of a new conversation.
    /// </summary>
    public static async Task<Guid> SeedUserAsync(
        StoryWalkerDbContext db,
        string clerkUserId,
        UserSettingsDto? settings = null
    )
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            ClerkUserId = clerkUserId,
            Email = $"{clerkUserId}@storywalker.app",
            KidsContext = settings?.Kids,
            StoryContext = settings?.Story,
            CharactersContext = settings?.Characters,
            ArtStyleContext = settings?.ArtStyle,
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();

        return user.Id;
    }

    /// <summary>Inserts the demo story (6 complete pages) owned by <paramref name="ownerUserId"/>.</summary>
    public static async Task<Guid> SeedDemoStoryAsync(StoryWalkerDbContext db, Guid ownerUserId)
    {
        var story = new Story
        {
            Id = Guid.NewGuid(),
            UserId = ownerUserId,
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

        db.Stories.Add(story);
        db.Pages.AddRange(pages);
        await db.SaveChangesAsync();

        return story.Id;
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
