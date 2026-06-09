# Story Walker

A kids' story-generation app. An authenticated user opens a private dashboard of their
stories, chats with an AI agent that gathers the details for a new story, and a background
worker generates 0–10 pages of consistent text and illustrations. Users can also create a
**sequel** or **prequel** of an existing story, reusing its characters and art style.

This is a **monorepo** (see [ADR-0012](docs/adr/0012-monorepo.md)). Architecture, decisions,
and diagrams live in [`docs/`](docs/README.md).

## Layout

```
story_walker/
├── docs/            # architecture documentation (ADRs, C4, ER)
├── apps/
│   ├── ui/          # Next.js 16 front-end
│   ├── api/         # ASP.NET Core API
│   └── functions/   # Azure Functions (generation worker)
├── src/
│   ├── StoryWalker.Domain/      # entities + EF Core model (shared)
│   └── StoryWalker.Contracts/   # DTOs + Service Bus message schemas (shared)
├── infra/           # infrastructure-as-code (Bicep/Terraform)
└── .github/workflows/  # CI/CD pipelines (path-filtered per component)
```

## Documentation

Start with [`docs/README.md`](docs/README.md) for the stack overview, the C4 and ER diagrams,
and the full set of Architecture Decision Records.

## Status

Scaffolding stage. The monorepo starts clean: `apps/ui`, `apps/api`, `apps/functions`, and
the shared `src/` libraries are built fresh against the architecture in [`docs/`](docs/README.md).
# story_walker
