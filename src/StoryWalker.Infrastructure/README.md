# StoryWalker.Infrastructure

The **infrastructure layer** (ADR-0015) — the technical implementations behind the domain and
application abstractions. Depends on `StoryWalker.Domain`.

Currently holds the **EF Core model** (`Persistence/`): `StoryWalkerDbContext` and the
per-entity `IEntityTypeConfiguration` mappings (keys, indexes, enum-as-string conversions,
relationships — PostgreSQL + pgvector, ADR-0009). The `DbContext` also stamps
`EntityBase.CreatedAt`/`UpdatedAt` on save.

Future home for the other external adapters: repository implementations, the Azure Service Bus
publisher, Blob Storage, and Microsoft Foundry clients.
