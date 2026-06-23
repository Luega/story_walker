using Microsoft.EntityFrameworkCore;
using StoryWalker.Domain.Entities;

namespace StoryWalker.Domain.Persistence;

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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(StoryWalkerDbContext).Assembly);
    }
}
