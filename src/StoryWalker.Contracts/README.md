# StoryWalker.Contracts

Shared .NET class library holding the API DTOs and the **Azure Service Bus message
contracts** exchanged across the generation pipeline (e.g. the bible/text/refimage messages
and the per-page `{ storyId, pageNo }` message), plus the queue-name constants.

`apps/api` is the producer; `apps/functions` are the consumers. Defining these here once
prevents the producer/consumer schema drift that motivated the monorepo
([ADR-0012](../../docs/adr/0012-monorepo.md)).
