using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryWalker.Domain.Entities;

namespace StoryWalker.Infrastructure.Persistence.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasKey(u => u.Id);

        // Identity from Clerk (ADR-0002): one domain user per external subject.
        builder.HasIndex(u => u.ClerkUserId).IsUnique();

        // Like every other enum in the model, the plan is stored as a readable string rather than
        // an ordinal, so adding paid tiers later cannot renumber the existing rows.
        builder.Property(u => u.Tier).HasConversion<string>();

        builder.HasMany(u => u.Stories)
            .WithOne(s => s.User)
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
