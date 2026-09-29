# StoryWalker.Application

The **application layer** (ADR-0015). Depends on `StoryWalker.Domain`; orchestrates use cases
and holds the system's boundary contracts:

- **API DTOs** (`Api/`) — request/response shapes for the ASP.NET Core API.
- **Generation-brief contract** (`Brief/`) — the agent-authored brief the API enqueues and the
  worker consumes ([ADR-0013](../../docs/adr/0013-agent-authored-generation-brief.md)).
- **Messaging contracts** (`Messaging/`) — the publisher port and the ingestion queue name
  ([ADR-0003](../../docs/adr/0003-async-generation-via-service-bus.md)).

The brief and messaging contracts are the **shared producer/consumer schema** (`apps/api`
produces, `apps/functions` consume) — defining them once here prevents schema drift
([ADR-0012](../../docs/adr/0012-monorepo.md)). This layer stays free of EF Core; persistence
lives in `StoryWalker.Infrastructure`.
