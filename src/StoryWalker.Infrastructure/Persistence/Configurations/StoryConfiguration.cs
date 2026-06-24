using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryWalker.Domain.Entities;

namespace StoryWalker.Infrastructure.Persistence.Configurations;

public class StoryConfiguration : IEntityTypeConfiguration<Story>
{
    public void Configure(EntityTypeBuilder<Story> builder)
    {
        builder.HasKey(s => s.Id);

        // ER models status as a string; keep DB values human-readable for the dashboard.
        builder.Property(s => s.Status).HasConversion<string>();

        // Self-reference: the story this one reuses characters + art style from (nullable).
        // Restrict so a source story can't be deleted out from under its reusing stories.
        builder.HasOne(s => s.BasedOnStory)
            .WithMany()
            .HasForeignKey(s => s.BasedOnStoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(s => s.Pages)
            .WithOne(p => p.Story)
            .HasForeignKey(p => p.StoryId)
            .OnDelete(DeleteBehavior.Cascade);

        // Placeholder embedding column (real[]). Query only when doing similarity search.
        // TODO: swap to Pgvector.Vector + HNSW index when the pgvector model lands (ADR-0009).
    }
}
