# ADR 0014 — Domain entities and the EF Core model share one project

- **Status:** Accepted
- **Date:** 2026-06-23
- **Deciders:** Luca Martinelli

## Context

The shared data model lives in `StoryWalker.Domain` and is referenced by both `apps/api`
(producer) and `apps/functions` (consumer) so it is defined once
([ADR-0012](0012-monorepo.md)). The question is *how* to structure that library: a strict
Clean/Onion architecture would split it into separate projects — `Domain` (pure entities, no
persistence dependency), `Application` (use cases + repository interfaces), and
`Infrastructure` (EF Core `DbContext`, configurations, external services) — so the domain
model never depends on the ORM.

Two facts shape the choice here:

- The entities **are** the persistence model — EF Core maps them directly to tables; there is
  no separate hand-mapped storage model to keep in sync with a richer domain model.
- The data model is small and stable (five entities — see the
  [ER diagram](../diagrams/er-diagram.md)); the product is a kids' story app, not a system
  that must survive swapping its ORM.

> Note: `infra/` in this repo is **infrastructure-as-code** (provisioning Azure resources),
> unrelated to the Clean-architecture "Infrastructure layer". Persistence is application code
> that compiles into an assembly, so it cannot live under `infra/`.

## Decision

We will keep the domain **entities and the EF Core model in a single project**,
`StoryWalker.Domain`, rather than splitting persistence into a separate `Infrastructure`
project. EF Core (`DbContext` + `IEntityTypeConfiguration` mappings) lives under a
`Persistence/` folder; entities live under `Entities/`.

To contain the coupling this introduces:

- Entities stay **plain POCOs** — no EF Core attributes on them; all mapping is isolated in
  `Persistence/Configurations/` (one `IEntityTypeConfiguration` per entity, discovered via
  `ApplyConfigurationsFromAssembly`).
- `StoryWalker.Contracts` (DTOs + message schemas) stays **EF-free**, so the API/wire surface
  carries no persistence coupling.

## Consequences

### Positive
- **One source of truth** for the model, shared by API and worker — the anti-drift goal of the
  monorepo ([ADR-0012](0012-monorepo.md)) with no extra mapping layer.
- **Less ceremony**: no `Application`/`Infrastructure` projects, repository interfaces, or
  entity-to-storage mapping to maintain at this scale.
- The `Entities/` ↔ `Persistence/` folder split keeps concerns visually separated and draws a
  clean seam for a later extraction if needed.

### Negative / trade-offs accepted
- The entities sit in a project that **references EF Core**, so the domain is not ORM-pure.
  Mitigated by keeping entities attribute-free and the mapping isolated to one folder.
- If business logic later grows complex, the lack of a dedicated `Application` layer may need
  revisiting.

### Things this decision obliges us to build or watch
- Keep mapping out of the entity classes (configurations only) so the seam stays clean.
- If the domain outgrows this, extract `Persistence/` into a `StoryWalker.Infrastructure`
  project — a move-and-reference along the existing folder boundary, not a rewrite.

## Alternatives considered

### Strict Clean/Onion split (Domain / Application / Infrastructure projects)
The textbook layering: a persistence-free `Domain`, an `Application` layer of use cases and
interfaces, and an `Infrastructure` project holding EF Core. Rejected for now: it buys
indirection (extra projects, repository interfaces, storage mapping) with little payoff when
the entities already are the persistence model and the schema is small and stable. The folder
structure keeps the option open.

### Put persistence under `infra/`
Rejected on a category error: `infra/` is infrastructure-as-code (Bicep/Terraform that
provisions cloud resources), not a .NET layer. Persistence is compiled C# that both apps
reference, so it belongs in a shared library under `src/`.
