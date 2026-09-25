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

        // Losing the conversation (a retention purge, say) must not take the story with it; the
        // story only loses the place its failure would be reported to (ADR-0027).
        builder.HasOne(s => s.Conversation)
            .WithMany()
            .HasForeignKey(s => s.ConversationId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(s => s.Pages)
            .WithOne(p => p.Story)
            .HasForeignKey(p => p.StoryId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
