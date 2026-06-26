# ADR 0017 — Real PostgreSQL via Testcontainers for integration tests

- **Status:** Accepted
- **Date:** 2026-06-26
- **Deciders:** Luca Martinelli

## Context

`apps/api` is the first runnable host, and its Story endpoints
([`apps/api/Endpoints/StoryEndpoints.cs`](../../apps/api/Endpoints/StoryEndpoints.cs)) carry behaviour
that is **relational and PostgreSQL-specific**, not just CRUD:

- `DELETE /stories/{id}` relies on the **cascade** from `Story` to `Page`/`CharacterPage`, while
  user-owned `Character`s are kept.
- The self-referential `BasedOnStory` foreign key uses **`Restrict`**, so deleting a story another
  story reuses must raise `DbUpdateException` → HTTP `409`.
- `GET`/`PATCH` use EF Core projections (`Expression<Func<Story, StoryDto>>`) **translated to SQL**.

A test suite that does not exercise a real database engine would either be unable to express these
cases or would pass for the wrong reasons. We want the endpoint tests to run against the same engine
as production (PostgreSQL + pgvector — [ADR-0009](0009-postgresql-pgvector-continuity.md)), while
staying fast enough to run on every build and in CI.

## Decision

We will run the API integration tests against a **real PostgreSQL started with Testcontainers**
(`pgvector/pgvector:pg17`, the same image as [`docker-compose.yml`](../../docker-compose.yml)), booted
in memory through `WebApplicationFactory<Program>`
([`tests/StoryWalker.Api.Tests`](../../tests/StoryWalker.Api.Tests)).

To keep this cheap and deterministic:

- **One container per test run**, shared via an xUnit **collection fixture**
  (`ICollectionFixture<StoryWalkerApiFactory>`); the fixture starts the container and applies EF Core
  migrations **once**.
- **Per-test isolation** with **Respawn**, which truncates the data between tests (ignoring
  `__EFMigrationsHistory`) instead of recreating the schema or the container.
- The container connection string is injected via the `ConnectionStrings__Postgres` environment
  variable (the same mechanism docker-compose uses), and the test environment is `Testing` so the
  Development-only startup auto-migrate/seed block stays off — the fixture owns migration and seeding.

This requires a **Docker daemon** available wherever the tests run (local dev and CI).

## Consequences

### Positive
- **High fidelity**: cascade deletes, the `Restrict` → `409` path, and EF→SQL translation are tested
  on the real engine — the bugs we care about are the bugs we can catch.
- The test database is the production engine and image, so schema/migration behaviour matches.
- Sharing one container + Respawn keeps the suite fast (whole suite ≈ 1s after the image is pulled)
  while each test still sees a clean database.

### Negative / trade-offs accepted
- **Docker is a hard prerequisite** to run the suite; a machine or CI runner without Docker cannot
  execute these tests.
- Slower than an in-process database for the very first run (image pull + container start), and
  heavier than a pure unit test.
- Tests touch real I/O, so they are integration tests by nature — not a substitute for fast unit
  tests of pure logic.

### Things this decision obliges us to build or watch
- CI must provide a Docker-enabled runner (follow-up: a workflow that runs `dotnet test`;
  GitHub-hosted `ubuntu-latest` ships with Docker, so no extra service setup is needed).
- Keep the Testcontainers image pinned to the same major as production
  (`pgvector/pgvector:pg17`) so the test engine tracks the real one.
- Pure-logic tests (Domain, seeder, mappers) should be plain unit tests **without** a container —
  this ADR governs the API integration suite, not all tests.

## Alternatives considered

### EF Core InMemory provider
Zero dependencies and very fast. Rejected: it does **not** enforce relational constraints — no
cascade, no foreign keys — so the `DELETE` cascade and the `Restrict` → `409` path cannot be tested,
and projections are not validated against real SQL. It would give green tests that prove little of
what these endpoints actually do.

### SQLite in-memory
Relational (enforces foreign keys and cascade) and needs no Docker. Rejected as the primary choice:
the SQL dialect differs from PostgreSQL (PascalCase quoting, `real[]`, Npgsql-specific translations),
so it tests *a* database, not *our* database — divergences would surface only in production.

### A shared developer/CI PostgreSQL instance (not per-run)
Point the tests at one long-lived Postgres. Rejected: shared mutable state causes cross-run
interference and flaky tests, requires out-of-band provisioning, and couples the suite to
environment setup. Testcontainers gives an isolated, disposable instance owned by the test run.
