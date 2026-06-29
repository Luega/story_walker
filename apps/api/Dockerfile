# syntax=docker/dockerfile:1
# Build context is the monorepo ROOT (see docker-compose.yml) so this Dockerfile can reach src/.

# ---- build stage ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

# Copy only the project files first and restore — this layer is cached until a .csproj changes,
# so source-only edits skip the (slow) NuGet restore on rebuild. Directory.Packages.props carries
# the centrally-managed package versions (ADR-0016), so restore needs it.
COPY Directory.Packages.props                                          ./
COPY apps/api/StoryWalker.Api.csproj                                   apps/api/
COPY src/StoryWalker.Domain/StoryWalker.Domain.csproj                 src/StoryWalker.Domain/
COPY src/StoryWalker.Application/StoryWalker.Application.csproj        src/StoryWalker.Application/
COPY src/StoryWalker.Infrastructure/StoryWalker.Infrastructure.csproj src/StoryWalker.Infrastructure/
RUN dotnet restore apps/api/StoryWalker.Api.csproj

# Copy the rest of the source and publish a self-contained framework-dependent app.
COPY apps/api/ apps/api/
COPY src/      src/
RUN dotnet publish apps/api/StoryWalker.Api.csproj -c Release -o /app --no-restore

# ---- runtime stage ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
# The aspnet image runs as a non-root user and listens on 8080 by default.
EXPOSE 8080
ENTRYPOINT ["dotnet", "StoryWalker.Api.dll"]
