using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace StoryWalker.Infrastructure.Persistence;

/// <summary>
/// Design-time factory used by the <c>dotnet ef</c> tooling. It builds the context directly from a
/// connection string (env <c>ConnectionStrings__Postgres</c>, or a local-dev default) so the tools
/// never boot the API host — keeping the host's startup migrate/seed code out of design time.
/// </summary>
public class StoryWalkerDbContextFactory : IDesignTimeDbContextFactory<StoryWalkerDbContext>
{
    private const string DefaultConnectionString =
        "Host=localhost;Port=5432;Database=storywalker;Username=storywalker;Password=storywalker";

    public StoryWalkerDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
            ?? DefaultConnectionString;

        var options = new DbContextOptionsBuilder<StoryWalkerDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new StoryWalkerDbContext(options);
    }
}
