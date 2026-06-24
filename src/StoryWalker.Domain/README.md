# StoryWalker.Domain

The **domain layer** — the pure heart of the model (ADR-0015). Holds the entities (`User`,
`Story`, `Page`, `Character`, `CharacterPage`, plus `EntityBase`) and enums (`StoryStatus`,
`PageStatus`); see the [ER diagram](../../docs/diagrams/er-diagram.md).

This project has **no dependencies** — no EF Core, no DTOs, no infrastructure. The EF Core
model (DbContext + configurations) lives in `StoryWalker.Infrastructure`; DTOs and message
contracts live in `StoryWalker.Application`. Keeping the domain dependency-free is the point of
the layering ([ADR-0015](../../docs/adr/0015-layered-architecture.md)).

Referenced (transitively) by both `apps/api` and `apps/functions` so the model is defined once
([ADR-0012](../../docs/adr/0012-monorepo.md)).
