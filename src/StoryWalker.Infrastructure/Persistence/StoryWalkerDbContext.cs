using Microsoft.EntityFrameworkCore;
using StoryWalker.Domain.Entities;

namespace StoryWalker.Infrastructure.Persistence;

/// <summary>
/// EF Core model for the Story Walker domain (PostgreSQL + pgvector — ADR-0009). Referenced by
/// both <c>apps/api</c> and <c>apps/functions</c> so the data model is defined once (ADR-0012).
/// </summary>
public class StoryWalkerDbContext(DbContextOptions<StoryWalkerDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<Story> Stories => Set<Story>();

    public DbSet<Page> Pages => Set<Page>();

    public DbSet<Character> Characters => Set<Character>();

    public DbSet<CharacterPage> CharacterPages => Set<CharacterPage>();

    public DbSet<Conversation> Conversations => Set<Conversation>();

    public DbSet<ConversationMessage> ConversationMessages => Set<ConversationMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(StoryWalkerDbContext).Assembly);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyTimestamps();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        ApplyTimestamps();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// Stamps <see cref="EntityBase.CreatedAt"/> on insert and <see cref="EntityBase.UpdatedAt"/>
    /// on update, so callers never set them by hand. <see cref="CharacterPage"/> has no
    /// timestamps and is unaffected.
    /// </summary>
    private void ApplyTimestamps()
    {
        var now = DateTime.UtcNow;

        foreach (var entry in ChangeTracker.Entries<EntityBase>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = now;
                    break;
                case EntityState.Modified:
                    entry.Entity.UpdatedAt = now;
                    break;
            }
        }
    }
}
