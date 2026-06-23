using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryWalker.Domain.Entities;

namespace StoryWalker.Domain.Persistence.Configurations;

public class CharacterPageConfiguration : IEntityTypeConfiguration<CharacterPage>
{
    public void Configure(EntityTypeBuilder<CharacterPage> builder)
    {
        // Composite key: a character appears on a page at most once.
        builder.HasKey(cp => new { cp.CharacterId, cp.PageId });

        builder.HasOne(cp => cp.Character)
            .WithMany(c => c.CharacterPages)
            .HasForeignKey(cp => cp.CharacterId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(cp => cp.Page)
            .WithMany(p => p.CharacterPages)
            .HasForeignKey(cp => cp.PageId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
