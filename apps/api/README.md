# apps/api — ASP.NET Core API

The ASP.NET Core Web API: owns the domain data (PostgreSQL), runs the information-gathering agent
(Microsoft Foundry), and enqueues generation jobs on Azure Service Bus (fire-and-forget, returns
`202`). References the shared libraries in [`../../src`](../../src). Every `/stories` route
requires a Clerk session token (ADR-0002). A story belongs to the caller who started it, and another
user's story answers `404`, exactly like one that does not exist.

## Configuration

Locally the API reads `appsettings.Development.json`, which is **git-ignored** (`.gitignore`
excludes every `appsettings.*.json` except the base file) because it can hold a Foundry key. Start
from the committed template:

```bash
cp apps/api/appsettings.Development.json.example apps/api/appsettings.Development.json
```

then fill in `Foundry:Endpoint` and `Foundry:Deployment` — the API refuses to start without them.
The Postgres and Service Bus values already point at `docker-compose.yml` and work as-is. The
template's odd extension is deliberate: `appsettings.Development.example.json` would match the
ignore rule and never reach the repository.

| Key | Local value | Notes |
|---|---|---|
| `ConnectionStrings:Postgres` | compose's postgres | Override with `ConnectionStrings__Postgres`. |
| `Foundry:Endpoint` | **you fill in** | Your Foundry resource, e.g. `https://<name>.services.ai.azure.com`. |
| `Foundry:Deployment` | **you fill in** | The **chat** model deployment the agent runs on. The worker uses a different, image deployment on the same resource. |
| `Foundry:ApiKey` | optional | Leave empty to authenticate with your own `az login`. **Secret** — never commit a real value. |
| `ServiceBus:ConnectionString` | emulator string | The emulator key is a public placeholder, not a secret. |

Running the API **in a container** instead reads the same settings from environment variables
(`Foundry__Endpoint`, …) set by `docker-compose.yml` from a root `.env` file — copy
[`../../.env.example`](../../.env.example) for that path. There is no `az login` inside the
container, so that path needs `FOUNDRY_API_KEY`.

## Run locally

From the repository root.

**API on the host** (faster inner loop, and reuses your `az login`):

```bash
docker compose up -d               # infrastructure only (postgres, Service Bus emulator, azurite, pgAdmin, Storage Explorer)
dotnet run --project apps/api      # build + run the API on the host
```

**API in Docker** — it sits behind the `api` compose profile:

```bash
docker compose --profile api up --build
```

In both modes, on startup in **Development** the API applies the latest EF Core migration, then
serves. Nothing is seeded: a demo story would belong to no real account, so a first sign-in starts on
an empty dashboard.

- Swagger UI — <http://localhost:5080/swagger> (same port on the host and in Docker)

| Method & route | Purpose | Responses |
| --- | --- | --- |
| `GET /health` | Liveness probe | `200` `"healthy"` |
| `POST /stories/conversations` | One agent turn; omit `conversationId` to start | `200` reply, `202` + `Location` once the brief is authored and enqueued, `400` empty message, `404` unknown conversation, `502` unusable brief |
| `GET /stories` | The caller's story summaries | `200` |
| `GET /stories/{id}` | Full story: `targetAge`, `artStyle`, six pages with `imageUrl` and per-page `status` | `200`, `404` unknown or not yours |
| `PATCH /stories/{id}` | Rename a story (title only) | `200` updated story, `400` empty title, `404` unknown or not yours |
| `DELETE /stories/{id}` | Delete a story (pages cascade) | `204`, `404` unknown or not yours |

Every `/stories` route answers `401` without a valid token.

`PATCH` takes `{ "title": "..." }` and returns the updated story.

After a `202`, poll `GET /stories/{id}`: the story stays `Generating` until the worker in
[`../functions`](../functions) has rendered the pages, then becomes `Complete` (or `Failed` if no
page could be illustrated).

## Inspect the database (pgAdmin)

`docker compose up` also starts **pgAdmin** for browsing PostgreSQL:

- URL — <http://localhost:5050> (login `admin@storywalker.app` / `admin`)
- The **Story Walker (local)** server is pre-registered (see
  [`../../docker/pgadmin/servers.json`](../../docker/pgadmin/servers.json)); expand it and enter
  the DB password `storywalker` once. Tables are under
  *Story Walker (local) → Databases → storywalker → Schemas → public → Tables*.

Prefer the terminal? `docker exec -it storywalker-postgres psql -U storywalker -d storywalker`
(table names are PascalCase, so quote them: `SELECT * FROM "Stories";`).

## Tests

Endpoint integration tests live in
[`../../tests/StoryWalker.Api.Tests`](../../tests/StoryWalker.Api.Tests). They boot the real API in
memory (`WebApplicationFactory<Program>`) against a **throwaway PostgreSQL** started with
Testcontainers (`pgvector/pgvector:pg17`), so the relational behaviour the endpoints rely on (cascade
deletes, EF→SQL projections) is exercised for real. The same suite also tests the worker's
generation services (`GenerationServicesTests`). The
container migrates once and Respawn resets the data between tests.

```bash
dotnet test StoryWalker.slnx     # requires Docker running (Testcontainers pulls/starts Postgres)
```

The API exposes its `internal` `Program` class to the test assembly via `<InternalsVisibleTo>` in
[`StoryWalker.Api.csproj`](StoryWalker.Api.csproj). The test environment is `Testing`, so the startup
auto-migrate/seed block (Development-only) stays off — the test fixture owns migration and seeding.

## Migrations

EF Core migrations live in
[`../../src/StoryWalker.Infrastructure/Persistence/Migrations`](../../src/StoryWalker.Infrastructure/Persistence/Migrations)
(`dotnet-ef` is pinned in [`../../.config/dotnet-tools.json`](../../.config/dotnet-tools.json);
run `dotnet tool restore` once). To add a migration:

```bash
dotnet ef migrations add <Name> \
  --project src/StoryWalker.Infrastructure \
  --startup-project apps/api \
  --output-dir Persistence/Migrations
```
