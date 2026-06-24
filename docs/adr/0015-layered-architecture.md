# ADR 0015 — Layered architecture (Domain / Application / Infrastructure)

- **Status:** Accepted
- **Date:** 2026-06-24
- **Deciders:** Luca Martinelli

## Context

The shared .NET code began as two projects: `StoryWalker.Domain` (entities **and** the EF Core
model) and `StoryWalker.Contracts` (DTOs + message schemas). [ADR-0014](0014-entities-and-ef-model-in-one-project.md)
deliberately merged entities with the EF model to avoid extra projects.

As the model grew we wanted a structure aligned with **Domain-Driven Design / Clean
Architecture**, where the dependency rule points inward: the domain models the business and
knows nothing about persistence, transport, or external services. The previous layout violated
that on two counts — EF Core sat inside the domain, and the DTO/message contracts were a
peer project rather than an application concern. We are still greenfield (nothing committed,
~15 small files), so restructuring now is cheap.

## Decision

We will organise the shared code into three layers with an inward dependency rule:

```
src/
  StoryWalker.Domain/         # entities, enums, EntityBase — NO dependencies
  StoryWalker.Application/     # use cases, API DTOs, generation-brief + messaging contracts
                              #   → depends on Domain
  StoryWalker.Infrastructure/ # EF Core (DbContext + configurations) and external adapters
                              #   → depends on Domain
```

- **Domain** is dependency-free: entities (`User`, `Story`, `Page`, `Character`,
  `CharacterPage`, `EntityBase`) and enums only. No EF Core, no DTOs.
- **Application** holds the boundary contracts and (later) use cases. The former
  `StoryWalker.Contracts` is **folded into Application** rather than kept as a separate
  package: API DTOs (`Api/`), the generation brief (`Brief/`), and the Durable messaging
  contracts + queue name (`Messaging/`).
- **Infrastructure** holds the EF Core model — `StoryWalkerDbContext` and the per-entity
  `IEntityTypeConfiguration` mappings — and is the future home for repository implementations
  and the Service Bus / Blob / Foundry adapters.

This **supersedes [ADR-0014](0014-entities-and-ef-model-in-one-project.md)** (entities + EF in
one project) and refines the `src/` layout described in [ADR-0012](0012-monorepo.md); the
monorepo decision itself is unchanged.

## Consequences

### Positive
- **Pure domain**: entities carry no persistence or transport coupling — the DDD goal.
- Clear seams: persistence concerns are isolated in Infrastructure, boundary contracts in
  Application, each independently testable and replaceable.
- The shared producer/consumer contracts (brief + messaging) still live in one place
  (Application), preserving the anti-drift property of [ADR-0012](0012-monorepo.md).

### Negative / trade-offs accepted
- More projects and project references to wire than the two-project layout.
- The brief/message contracts live in `Application` rather than a dedicated `Contracts`
  package; if a thin external consumer ever needs only the wire schema, we may extract a
  separate contracts package then.
- `apps/api` and `apps/functions` now reference Application + Infrastructure (Domain comes
  transitively) instead of two flat libraries.

### Things this decision obliges us to build or watch
- Keep the dependency rule intact: Domain must not reference Application or Infrastructure.
- When repositories/adapters arrive, define their interfaces in Domain/Application and
  implement them in Infrastructure.
- Update the app project references and CI path filters to the three-project layout.

## Alternatives considered

### Keep entities + EF model in one project ([ADR-0014](0014-entities-and-ef-model-in-one-project.md))
The prior decision. Rejected now: it keeps EF Core inside the domain, preventing a pure domain
layer; the small-scale simplicity argument no longer outweighs the wish for DDD layering.

### Put the DTOs/contracts inside the Domain layer
Considered (one fewer project). Rejected: DTOs and message schemas are boundary/transport
concerns, not domain concepts — placing them in the domain reintroduces exactly the coupling
this ADR removes, and would churn the domain whenever an API or message shape changes.

### Keep a separate `StoryWalker.Contracts` package
Viable and orthodox. Not chosen: at this stage the contracts are only consumed by our own
api/functions, so folding them into Application keeps the project count down; a dedicated
package can be extracted later if an external consumer needs the bare schema.
