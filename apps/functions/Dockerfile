# syntax=docker/dockerfile:1
# Build context is the monorepo ROOT (see docker-compose.yml) so this Dockerfile can reach src/.

# ---- build stage ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

# Restore first (cached until a .csproj or the central versions change). The worker references
# Application and Infrastructure (which reference Domain), so those four csproj +
# Directory.Packages.props are all restore needs.
COPY Directory.Packages.props                                  ./
COPY apps/functions/StoryWalker.Functions.csproj              apps/functions/
COPY src/StoryWalker.Domain/StoryWalker.Domain.csproj         src/StoryWalker.Domain/
COPY src/StoryWalker.Application/StoryWalker.Application.csproj src/StoryWalker.Application/
COPY src/StoryWalker.Infrastructure/StoryWalker.Infrastructure.csproj src/StoryWalker.Infrastructure/
RUN dotnet restore apps/functions/StoryWalker.Functions.csproj

# Copy the rest of the source and publish.
COPY apps/functions/ apps/functions/
COPY src/           src/
RUN dotnet publish apps/functions/StoryWalker.Functions.csproj -c Release -o /app --no-restore

# ---- runtime stage ----
FROM mcr.microsoft.com/azure-functions/dotnet-isolated:4-dotnet-isolated10.0 AS runtime
ENV AzureWebJobsScriptRoot=/home/site/wwwroot \
    AzureFunctionsJobHost__Logging__Console__IsEnabled=true
COPY --from=build /app /home/site/wwwroot
