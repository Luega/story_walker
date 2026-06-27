using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryWalker.Domain.Entities;

namespace StoryWalker.Infrastructure.Persistence.Configurations;

public class ConversationMessageConfiguration : IEntityTypeConfiguration<ConversationMessage>
{
    public void Configure(EntityTypeBuilder<ConversationMessage> builder)
    {
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Role).HasConversion<string>();

        // One message per position per conversation; also the natural ordering index.
        builder.HasIndex(m => new { m.ConversationId, m.Seq }).IsUnique();
    }
}
