# Story Walker — Architecture Documentation

Story Walker is a kids' story-generation app. An authenticated user opens a private
dashboard of their stories, chats with an AI agent that gathers the details for a new
story, and a background worker generates 0–10 pages of consistent text and illustrations.
Users can also create a **sequel** or **prequel** of an existing story, reusing its
characters and art style for continuity.

## Stack at a glance

| Concern            | Technology                                   |
|--------------------|----------------------------------------------|
| Front-end          | Next.js 16                                   |
| API                | .NET ASP.NET Core                            |
| Generation worker  | Azure Functions (plain, queue-triggered)     |
| Messaging          | Azure Service Bus                            |
| Realtime           | Azure SignalR Service                        |
| Database           | PostgreSQL + `pgvector`                      |
| Image storage      | Azure Blob Storage                           |
| Authentication     | Clerk                                        |
| AI (agent + gen)   | Microsoft Foundry                            |
| Repository         | Monorepo (one Git repo, path-filtered CI)    |

## Repository layout

All components live in a single monorepo (see [ADR-0012](adr/0012-monorepo.md)). API and
Functions share the .NET domain model and the Service Bus message contracts via `src/`;
documentation describes the whole system and lives at the root.

```
story_walker/                       # monorepo root (this repository)
├── docs/                           # architecture documentation
│   ├── README.md                   # this file
│   ├── adr/                        # Architecture Decision Records
│   └── diagrams/                   # C4 + ER (Mermaid)
├── apps/                           # deployable applications
│   ├── ui/                         # Next.js 16 front-end
│   ├── api/                        # ASP.NET Core API
│   └── functions/                  # Azure Functions (generation worker)
├── src/                            # shared .NET libraries
│   ├── StoryWalker.Domain/         # entities + EF Core model
│   └── StoryWalker.Contracts/      # DTOs + Service Bus message schemas
├── infra/                          # infrastructure-as-code (Bicep/Terraform)
├── .github/workflows/              # CI/CD pipelines (path-filtered per component)
└── README.md                       # repository entry point
```

> **Path-filtered CI:** each `apps/<component>/**` change triggers only that component's
> pipeline; a change under `src/**` triggers the API *and* Functions pipelines, since both
> depend on the shared libraries. Documentation-only changes (`docs/**`, `**.md`) build nothing.

## Documents

### Diagrams
- [C4 — System Context & Containers](diagrams/c4-context-container.md)
- [Entity-Relationship diagram](diagrams/er-diagram.md)

### Architecture Decision Records
- [ADR template](adr/0000-template.md)
- [0001 — Next.js front-end + ASP.NET Core API split](adr/0001-nextjs-aspnetcore-split.md)
- [0002 — Clerk for authentication](adr/0002-clerk-for-authentication.md)
- [0003 — Asynchronous generation via Azure Service Bus](adr/0003-async-generation-via-service-bus.md)
- [0004 — Plain Azure Functions (choreography) for the pipeline](adr/0004-plain-azure-functions-choreography.md)
- [0005 — Atomic completion counter, idempotency & dead-letter policy](adr/0005-completion-counter-idempotency-deadletter.md)
- [0006 — Azure SignalR for realtime progress](adr/0006-azure-signalr-for-realtime.md)
- [0007 — Single-pass text generation for narrative coherence](adr/0007-single-pass-text-generation.md)
- [0008 — Per-character reference images for visual consistency](adr/0008-per-character-reference-images.md)
- [0009 — PostgreSQL + pgvector for sequel/prequel continuity](adr/0009-postgresql-pgvector-continuity.md)
- [0010 — Azure Blob Storage for generated images](adr/0010-azure-blob-storage-for-images.md)
- [0011 — Microsoft Foundry for the agent and generation](adr/0011-microsoft-foundry-agent-and-generation.md)
- [0012 — Monorepo for all components](adr/0012-monorepo.md)
