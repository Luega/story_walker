using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using StoryWalker.Domain.Entities;

namespace StoryWalker.Domain.Persistence.Configurations;

public class CharacterConfiguration : IEntityTypeConfiguration<Character>
{
    public void Configure(EntityTypeBuilder<Character> builder)
    {
        builder.HasKey(c => c.Id);

        // Placeholder embedding column (real[]). Query only when doing similarity search.
        // TODO: swap to Pgvector.Vector + HNSW index when the pgvector model lands (ADR-0009).
        // User relationship is configured from UserConfiguration.
    }
}
