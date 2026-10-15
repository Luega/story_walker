# Story Walker

Story Walker is a kids' story-generation app. An authenticated user opens a private
dashboard of their stories, chats with an AI agent that gathers the details for a new
story, and a background worker generates 0–10 pages of consistent text and illustrations.
Users can also create a new story that reuses the characters and art style of an existing one.

## Stack

| Concern           | Technology            |
| ----------------- | --------------------- |
| Front-end         | Next.js               |
| API               | ASP .NET Core         |
| Generation worker | Azure Functions       |
| Messaging         | Azure Service Bus     |
| Progress updates  | Polling (SignalR not built, ADR-0006) |
| Database          | PostgreSQL            |
| Image storage     | Azure Blob Storage    |
| Authentication    | Clerk                 |
| AI (agent + gen)  | Microsoft Foundry     |

## Repository layout

All components live in a single monorepo (see [ADR-0012](docs/adr/0012-monorepo.md)).

```
story_walker/                       # monorepo root
├── docs/                             # architecture documentation
│   ├── docs/adr                        # Architecture Decision Records
│   └── docs/diagrams/                  # C4 + ER
├── apps/                           # deployable applications
│   ├── ui/                           # Next.js front-end
│   ├── api/                          # ASP.NET Core API
│   └── functions/                    # Azure Functions
├── src/                            # shared .NET libraries (layered — ADR-0015)
│   ├── StoryWalker.Domain/           # entities + enums (pure; no EF, no DTOs)
│   ├── StoryWalker.Application/      # DTOs, generation-brief + message contracts, use cases
│   └── StoryWalker.Infrastructure/   # EF Core model (DbContext + configurations), adapters
├── iac/                            # infrastructure-as-code (Terraform, ADR-0034/0035)
├── .github/workflows/              # CI per component + the prod deploy (ADR-0035)
└── README.md                       # repository entry point
```

## Documents

### Diagrams

- [C4 — System Context & Containers](docs/diagrams/c4-context-container.md)
- [Entity-Relationship diagram](docs/diagrams/er-diagram.md)

### Architecture Decision Records

- [ADR template](docs/adr/0000-template.md)
- [0001 — Next.js front-end + ASP.NET Core API split](docs/adr/0001-nextjs-aspnetcore-split.md)
- [0002 — Clerk for authentication](docs/adr/0002-clerk-for-authentication.md)
- [0003 — Asynchronous generation via Azure Service Bus](docs/adr/0003-async-generation-via-service-bus.md)
- [0004 — Durable Functions for the generation pipeline](docs/adr/0004-durable-functions-for-generation.md) *(superseded by 0020)*
- [0005 — Idempotency and failure handling in the generation pipeline](docs/adr/0005-idempotency-and-failure-handling.md) *(amended by 0020, 0026, 0031, 0032)*
- [0006 — Azure SignalR for realtime progress](docs/adr/0006-azure-signalr-for-realtime.md) *(not built)*
- [0007 — Single-pass text generation for narrative coherence](docs/adr/0007-single-pass-text-generation.md)
- [0008 — Per-character reference images for visual consistency](docs/adr/0008-per-character-reference-images.md) *(superseded by 0019)*
- [0009 — PostgreSQL + pgvector for story continuity and reuse](docs/adr/0009-postgresql-pgvector-continuity.md) *(superseded by 0019)*
- [0010 — Azure Blob Storage for generated images](docs/adr/0010-azure-blob-storage-for-images.md)
- [0011 — Microsoft Foundry for the agent and generation](docs/adr/0011-microsoft-foundry-agent-and-generation.md)
- [0012 — Monorepo for all components](docs/adr/0012-monorepo.md)
- [0013 — Agent-authored generation brief (JSON contract)](docs/adr/0013-agent-authored-generation-brief.md)
- [0014 — Domain entities and the EF Core model share one project](docs/adr/0014-entities-and-ef-model-in-one-project.md) *(superseded by 0015)*
- [0015 — Layered architecture (Domain / Application / Infrastructure)](docs/adr/0015-layered-architecture.md)
- [0016 — Central Package Management for NuGet versions](docs/adr/0016-central-package-management.md)
- [0017 — Real PostgreSQL via Testcontainers for integration tests](docs/adr/0017-testcontainers-for-integration-tests.md)
- [0018 — Persist agent conversations in PostgreSQL](docs/adr/0018-persist-agent-conversations.md)
- [0019 — MVP scope reduction: self-contained six-page stories](docs/adr/0019-mvp-scope-reduction.md)
- [0020 — A plain Service Bus function instead of Durable Functions](docs/adr/0020-plain-service-bus-function-for-generation.md)
- [0021 — Clerk integration: server-side token proxy and JIT provisioning](docs/adr/0021-clerk-integration.md)
- [0022 — Standing user context in the agent's system prompt](docs/adr/0022-standing-user-context-in-the-agent-prompt.md)
- [0023 — OpenTelemetry to Application Insights, end to end](docs/adr/0023-opentelemetry-to-application-insights.md)
- [0024 — Child-safe content: prompt rules and a friendly content-filter answer](docs/adr/0024-child-safe-content.md)
- [0025 — Rewrite an illustration prompt once after a moderation refusal](docs/adr/0025-rewrite-illustration-prompt-after-moderation.md)
- [0026 — One failed page fails the story, and a failed story can only be deleted](docs/adr/0026-one-failed-page-fails-the-story.md)
- [0027 — A failed story returns to its conversation, with the agent's advice](docs/adr/0027-failed-story-returns-to-its-conversation.md)
- [0028 — Conversation history: kept, titled, resumable, and disclosed](docs/adr/0028-conversation-history.md)
- [0029 — A new story based on a previous one, from a fresh conversation](docs/adr/0029-stories-based-on-a-previous-one.md)
- [0030 — A weekly limit of seven finished stories](docs/adr/0030-weekly-story-limit.md)
- [0031 — A timer fails stories stuck in Generating](docs/adr/0031-fail-stories-stuck-in-generating.md) *(amended by 0032)*
- [0032 — Dead-lettered messages settle their story and are removed](docs/adr/0032-settle-and-remove-dead-lettered-messages.md)
- [0033 — A playful visual design, and a one-page reader below desktop](docs/adr/0033-playful-visual-design.md)
- [0034 — Azure hosting, described in Terraform](docs/adr/0034-azure-hosting-with-terraform.md) *(proposed; amended by 0035)*
- [0035 — One lean prod environment, deployed by GitHub Actions](docs/adr/0035-single-lean-prod-and-github-actions.md) *(proposed; amended by 0036)*
- [0036 — Changes reach main through pull requests, gated by one required check](docs/adr/0036-pull-requests-and-one-required-check.md) *(proposed)*
