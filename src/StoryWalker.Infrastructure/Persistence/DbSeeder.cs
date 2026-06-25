using Microsoft.EntityFrameworkCore;
using StoryWalker.Domain.Entities;
using StoryWalker.Domain.Enums;

namespace StoryWalker.Infrastructure.Persistence;

/// <summary>
/// Seeds demo data for local development so the API returns something meaningful out of the box.
/// Idempotent: does nothing once the database already has a user. Timestamps are stamped
/// automatically by <see cref="StoryWalkerDbContext"/>'s SaveChanges override.
/// </summary>
public static class DbSeeder
{
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

        var luna = new Character
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Name = "Luna",
            Description = "round-faced girl, red curly hair, yellow raincoat",
            IllustrationPrompt = "a cheerful girl with red curly hair in a yellow raincoat, soft watercolour",
            Seed = 1001,
        };

        var pip = new Character
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Name = "Pip",
            Description = "a small grey kitten with bright green eyes",
            IllustrationPrompt = "a tiny grey kitten with big green eyes, soft watercolour",
            Seed = 1002,
        };

        var story = new Story
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Title = "Luna and the Rainy Day Adventure",
            Summary = "Luna and her kitten Pip discover the magic hiding in a rainy afternoon.",
            Status = StoryStatus.Complete,
            ArtStyle = "soft watercolour",
            Palette = "warm yellows and rainy blues",
        };

        var page1 = new Page
        {
            Id = Guid.NewGuid(),
            StoryId = story.Id,
            PageNo = 1,
            Content = "Luna pressed her nose to the window. Outside, the rain drew silver lines down the glass.",
            IllustrationPrompt = "Luna at a window watching the rain, Pip at her feet",
            Status = PageStatus.Complete,
        };

        var page2 = new Page
        {
            Id = Guid.NewGuid(),
            StoryId = story.Id,
            PageNo = 2,
            Content = "\"Come on, Pip!\" she said, pulling on her yellow raincoat. The puddles were waiting.",
            IllustrationPrompt = "Luna in her yellow raincoat by the front door, Pip looking up",
            Status = PageStatus.Complete,
        };

        var page3 = new Page
        {
            Id = Guid.NewGuid(),
            StoryId = story.Id,
            PageNo = 3,
            Content = "They splashed until the sky cleared, and a rainbow stretched over the whole street.",
            IllustrationPrompt = "Luna and Pip splashing in puddles under a rainbow",
            Status = PageStatus.Complete,
        };

        // Luna appears on every page; Pip joins her on pages 2 and 3.
        var characterPages = new[]
        {
            new CharacterPage { CharacterId = luna.Id, PageId = page1.Id },
            new CharacterPage { CharacterId = luna.Id, PageId = page2.Id },
            new CharacterPage { CharacterId = pip.Id, PageId = page2.Id },
            new CharacterPage { CharacterId = luna.Id, PageId = page3.Id },
            new CharacterPage { CharacterId = pip.Id, PageId = page3.Id },
        };

        db.Users.Add(user);
        db.Characters.AddRange(luna, pip);
        db.Stories.Add(story);
        db.Pages.AddRange(page1, page2, page3);
        db.CharacterPages.AddRange(characterPages);

        await db.SaveChangesAsync();
    }
}
