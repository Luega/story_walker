# StoryWalker.Contracts

Shared .NET class library holding the API DTOs and the **generation-brief JSON contract** —
the agent-authored brief (characters + descriptions + illustration prompts, palette, art
style, per-page text + prompts) that the API enqueues on Azure Service Bus and the worker
consumes ([ADR-0013](../../docs/adr/0013-agent-authored-generation-brief.md)) — plus the
Durable activity input DTOs and the ingestion queue name.

`apps/api` is the producer; `apps/functions` are the consumers. Defining these here once
prevents the producer/consumer schema drift that motivated the monorepo
([ADR-0012](../../docs/adr/0012-monorepo.md)).
