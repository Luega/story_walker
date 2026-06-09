# StoryWalker.Domain

Shared .NET class library holding the domain entities and the EF Core model: `User`,
`Series`, `Story`, `Page`, `Character`, `PageCharacter`, `StoryChunk`, `Conversation`,
`Message` (see the [ER diagram](../../docs/diagrams/er-diagram.md)).

Referenced by both `apps/api` and `apps/functions` so the data model is defined once. Per
[ADR-0012](../../docs/adr/0012-monorepo.md), a change here triggers the CI pipelines of both
dependent components.
