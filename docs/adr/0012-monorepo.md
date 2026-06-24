# ADR 0012 — Monorepo for all Story Walker components

- **Status:** Accepted
- **Date:** 2026-06-09
- **Deciders:** Luca Martinelli

## Context

Story Walker is made of several deployables: a Next.js front-end, an ASP.NET Core API, a set
of Azure Functions (the generation worker), infrastructure-as-code, and cross-cutting
documentation (ADRs, C4, ER). Early on the UI and the API were created as **two separate Git
repositories**, the parent folder holding the documentation was **not** a repository, and a
third repository for the Azure Functions was about to be created. We need a deliberate
repository strategy before that fragmentation hardens.

A decisive technical factor: the API and the Functions are both .NET and **share code** —
the EF Core domain model (`Story`, `Page`, `Character`, `CharacterPage`…) and the Service Bus
**message contracts** (the API is the producer, the Functions are the consumers). The
documentation, likewise, describes the *whole* system, not any single component.

## Decision

We will use a **single monorepo** containing all components, shared libraries, infrastructure,
and documentation:

```
story_walker/
├── docs/                       # ADRs, C4, ER (whole-system documentation)
├── apps/
│   ├── ui/                     # Next.js 16
│   ├── api/                    # ASP.NET Core
│   └── functions/              # Azure Functions (generation worker)
├── src/                          # shared .NET libraries, layered (see ADR-0015)
│   ├── StoryWalker.Domain/         # entities + enums (pure)
│   ├── StoryWalker.Application/     # DTOs + Service Bus message schemas + use cases
│   └── StoryWalker.Infrastructure/ # EF Core model + external adapters
├── infra/                      # IaC (Bicep/Terraform)
└── .github/workflows/          # CI/CD with path filters per component
```

CI/CD uses **GitHub Actions path filters** so each component builds and deploys independently
(e.g. `apps/ui/**` triggers only the UI pipeline); shared code paths (`src/**`) trigger the
pipelines of every component that depends on them (API and Functions).

## Consequences

### Positive
- **Atomic cross-cutting changes**: a Service Bus message schema change updates the producer
  (API) and consumers (Functions) in one commit/PR — impossible to keep coherent across repos.
- **Shared .NET libraries** (`Domain`, `Application`, `Infrastructure` — see
  [ADR-0015](0015-layered-architecture.md)) are referenced directly, with no cross-repo
  publishing or version drift.
- **Documentation has a natural home** at the root, describing the whole system.
- A single clone, a single CI configuration, simpler onboarding for a small team.
- Independent deploys are preserved via path filters.

### Negative / trade-offs accepted
- Path filters are declared by hand (we state "if `src/**` changes, build API + Functions");
  the CI does not infer the dependency graph automatically.
- Coarser access control than per-repo (acceptable for a small team).
- Repository grows over time; mitigated by clear top-level boundaries.

### Things this decision obliges us to build or watch
- Migration of the existing `story_walker_UI` and `story_walker_BE` repos into the monorepo —
  preserving history via `git subtree add --prefix=…`, or starting clean given the early stage.
- Path-filtered GitHub Actions workflows, including `src/**` in the API and Functions filters.
- A single `.gitignore` strategy spanning Node and .NET.

## Alternatives considered

### Polyrepo (one repository per component)
The current trajectory (UI, BE, Functions, docs each separate). Rejected: forces cross-repo
synchronisation of the shared .NET domain model and message contracts, leaves documentation
without a natural home, and multiplies CI configuration — with no offsetting benefit at this
team size.

### Hybrid (one .NET repo for API + Functions + docs, UI separate)
Keeps the shared .NET code and docs together while isolating the JS stack. Rejected as the
primary choice: it still splits the UI's contract with the API across repos and duplicates CI
patterns; retained as a fallback if a hard constraint later forces the UI into its own repo.
