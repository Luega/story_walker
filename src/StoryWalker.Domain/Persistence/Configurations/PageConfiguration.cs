using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryWalker.Domain.Entities;

namespace StoryWalker.Domain.Persistence.Configurations;

public class PageConfiguration : IEntityTypeConfiguration<Page>
{
    public void Configure(EntityTypeBuilder<Page> builder)
    {
        builder.HasKey(p => p.Id);

        // ER models status as a string; keep DB values human-readable for the dashboard.
        builder.Property(p => p.Status).HasConversion<string>();

        // Pages are ordered within a story by page number.
        builder.HasIndex(p => new { p.StoryId, p.PageNo }).IsUnique();
    }
}
