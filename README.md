# Story Walker

Story Walker is a kids' story-generation app. An authenticated user opens a private
dashboard of their stories, chats with an AI agent that gathers the details for a new
story, and a background worker generates 0–10 pages of consistent text and illustrations.
Users can also create a **sequel** or **prequel** of an existing story, reusing its
characters and art style for continuity.

## Stack

| Concern           | Technology              |
| ----------------- | ----------------------- |
| Front-end         | Next.js                 |
| API               | ASP .NET Core           |
| Generation worker | Azure Functions         |
| Messaging         | Azure Service Bus       |
| Realtime          | Azure SignalR Service   |
| Database          | PostgreSQL + `pgvector` |
| Image storage     | Azure Blob Storage      |
| Authentication    | Clerk                   |
| AI (agent + gen)  | Microsoft Foundry       |

## Repository layout

All components live in a single monorepo (see [ADR-0012](docs/adr0012-monorepo.md)).

```
story_walker/                       # monorepo root
├── docs/                             # architecture documentation
│   ├── docs/adr                        # Architecture Decision Records
│   └── docs/diagrams/                  # C4 + ER
├── apps/                           # deployable applications
│   ├── ui/                           # Next.js front-end
│   ├── api/                          # ASP.NET Core API
│   └── functions/                    # Azure Functions
├── src/                            # shared .NET libraries
│   ├── StoryWalker.Domain/           # entities + EF Core model
│   └── StoryWalker.Contracts/        # DTOs + Service Bus message schemas
├── infra/                          # infrastructure-as-code
├── .github/workflows/              # CI/CD pipelines
└── README.md                       # repository entry point
```

## Documents

### Diagrams

- [C4 — System Context & Containers](docs/diagrams/c4-context-container.md)
- [Entity-Relationship diagram](docs/diagrams/er-diagram.md)

### Architecture Decision Records

- [ADR template](docs/adr0000-template.md)
- [0001 — Next.js front-end + ASP.NET Core API split](docs/adr0001-nextjs-aspnetcore-split.md)
- [0002 — Clerk for authentication](docs/adr0002-clerk-for-authentication.md)
- [0003 — Asynchronous generation via Azure Service Bus](docs/adr0003-async-generation-via-service-bus.md)
- [0004 — Plain Azure Functions (choreography) for the pipeline](docs/adr0004-plain-azure-functions-choreography.md)
- [0005 — Atomic completion counter, idempotency & dead-letter policy](docs/adr0005-completion-counter-idempotency-deadletter.md)
- [0006 — Azure SignalR for realtime progress](docs/adr0006-azure-signalr-for-realtime.md)
- [0007 — Single-pass text generation for narrative coherence](docs/adr0007-single-pass-text-generation.md)
- [0008 — Per-character reference images for visual consistency](docs/adr0008-per-character-reference-images.md)
- [0009 — PostgreSQL + pgvector for sequel/prequel continuity](docs/adr0009-postgresql-pgvector-continuity.md)
- [0010 — Azure Blob Storage for generated images](docs/adr0010-azure-blob-storage-for-images.md)
- [0011 — Microsoft Foundry for the agent and generation](docs/adr0011-microsoft-foundry-agent-and-generation.md)
- [0012 — Monorepo for all components](docs/adr0012-monorepo.md)
