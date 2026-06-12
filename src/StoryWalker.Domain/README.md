# StoryWalker.Domain

Shared .NET class library holding the domain entities and the EF Core model: `User`,
`Story`, `Page`, `Character`, and `CharacterPage` (see the
[ER diagram](../../docs/diagrams/er-diagram.md)). Story/character embeddings live as `vector`
columns on `Story` and `Character` (ADR-0009), not a separate entity.

Referenced by both `apps/api` and `apps/functions` so the data model is defined once. Per
[ADR-0012](../../docs/adr/0012-monorepo.md), a change here triggers the CI pipelines of both
dependent components.
