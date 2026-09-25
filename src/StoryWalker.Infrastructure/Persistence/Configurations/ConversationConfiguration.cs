using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryWalker.Domain.Entities;

namespace StoryWalker.Infrastructure.Persistence.Configurations;

public class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    public void Configure(EntityTypeBuilder<Conversation> builder)
    {
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Status).HasConversion<string>();

        // No navigation on User: conversations are reached through the agent, never listed per user.
        builder
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(c => c.UserId);

        // The story a conversation was started from (ADR-0029). Deleting it leaves the
        // conversation, only unlinked.
        builder
            .HasOne<Story>()
            .WithMany()
            .HasForeignKey(c => c.BasedOnStoryId)
            .OnDelete(DeleteBehavior.SetNull);

        // Messages are owned by the conversation and cascade with it.
        builder
            .HasMany(c => c.Messages)
            .WithOne(m => m.Conversation)
            .HasForeignKey(m => m.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
