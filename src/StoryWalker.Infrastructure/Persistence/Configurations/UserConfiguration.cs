using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryWalker.Application.Api;
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

        // The same cap the endpoint validates, enforced by the column too: the prompt these four
        // answers are composed into has to stay dominated by its instructions.
        builder.Property(u => u.KidsContext).HasMaxLength(UserSettingsDto.MaxFieldLength);
        builder.Property(u => u.StoryContext).HasMaxLength(UserSettingsDto.MaxFieldLength);
        builder.Property(u => u.CharactersContext).HasMaxLength(UserSettingsDto.MaxFieldLength);
        builder.Property(u => u.ArtStyleContext).HasMaxLength(UserSettingsDto.MaxFieldLength);

        builder.HasMany(u => u.Stories)
            .WithOne(s => s.User)
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
