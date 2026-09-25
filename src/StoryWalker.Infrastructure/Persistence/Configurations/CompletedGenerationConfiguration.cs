using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryWalker.Domain.Entities;

namespace StoryWalker.Infrastructure.Persistence.Configurations;

public class CompletedGenerationConfiguration : IEntityTypeConfiguration<CompletedGeneration>
{
    public void Configure(EntityTypeBuilder<CompletedGeneration> builder)
    {
        builder.HasKey(g => g.Id);

        // Deleting a user takes their ledger with them; deleting a story does not (ADR-0030).
        builder
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(g => g.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        builder
            .HasOne<Story>()
            .WithMany()
            .HasForeignKey(g => g.StoryId)
            .OnDelete(DeleteBehavior.SetNull);

        // The weekly count: one user's rows since a given instant.
        builder.HasIndex(g => new { g.UserId, g.CompletedAt });

        // A story finishes once. Unique, so a redelivered message cannot count it twice; null
        // StoryIds (deleted stories) do not collide in PostgreSQL.
        builder.HasIndex(g => g.StoryId).IsUnique();
    }
}
