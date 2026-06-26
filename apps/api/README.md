# apps/api — ASP.NET Core API

The ASP.NET Core Web API: validates Clerk JWTs, owns the domain data (PostgreSQL + pgvector),
runs the information-gathering agent (Microsoft Foundry), and enqueues generation jobs on
Azure Service Bus (fire-and-forget, returns `202`). References the shared libraries in
[`../../src`](../../src).

## Run locally

From the repository root, either run everything in Docker, or run only the DB in Docker and the
API on the host.

**Everything in Docker** (one command — DB + API):

```bash
docker compose up --build         # builds the API image, starts postgres + api
```

The `api` service waits for postgres to be healthy, then migrates and seeds on startup. It reaches
the DB by service name (`Host=postgres`), injected via the `ConnectionStrings__Postgres`
environment variable in [`../../docker-compose.yml`](../../docker-compose.yml).

**API on the host** (faster inner loop — DB in Docker, API via `dotnet`):

```bash
docker compose up -d postgres     # start only PostgreSQL (pgvector image) on localhost:5432
dotnet run --project apps/api      # build + run the API on the host
```

In both modes, on startup in **Development** the API applies the latest EF Core migration and
seeds demo data (idempotent), then serves:

- Swagger UI — <http://localhost:5080/swagger>

| Method & route | Purpose | Responses |
| --- | --- | --- |
| `GET /health` | Liveness probe | `200` `"healthy"` |
| `GET /stories` | Story summaries (dashboard list) | `200` |
| `GET /stories/{id}` | Full story with pages and characters | `200`, `404` |
| `PATCH /stories/{id}` | Rename a story (title only) | `200` updated story, `400` empty title, `404` |
| `DELETE /stories/{id}` | Delete a story (pages + links cascade; characters kept) | `204`, `404`, `409` if another story reuses it |

`PATCH` takes `{ "title": "..." }` and returns the updated story.

The connection string lives in [`appsettings.Development.json`](appsettings.Development.json)
under `ConnectionStrings:Postgres`; override it with the `ConnectionStrings__Postgres`
environment variable.

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
deletes, the `BasedOnStory` `Restrict` FK → `409`, EF→SQL projections) is exercised for real. The
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
