# ADR 0018 — Persist agent conversations in PostgreSQL

- **Status:** Accepted
- **Date:** 2026-06-27
- **Deciders:** Luca Martinelli

> **Note (2026-09-10):** the local Ollama backend mentioned in *Context* below is gone. `StoryAgent`
> now runs against a Foundry chat-model deployment in every environment ([ADR-0011](0011-microsoft-foundry-agent-and-generation.md)),
> including local development and `docker compose`; the `Agent:Provider` switch, `AddOllamaChat` and
> the `OllamaSharp` dependency have been removed. The reason is fidelity: the small models that run
> on a dev machine are not representative of the ones that will author the generation brief
> ([ADR-0013](0013-agent-authored-generation-brief.md)), so local runs were exercising the plumbing
> rather than the product. Testing against the real, more capable models is what tells us whether
> the agent actually holds a coherent interview and emits a schema-valid brief. The trade-off
> accepted: no offline development, every local turn costs inference and needs Azure credentials
> (a service principal in `.env` for the container — `DefaultAzureCredential` cannot reuse the
> host's `az login` from inside it). Nothing in the decision below changes: conversation history is
> persisted regardless of which chat backend is registered.

## Context

The conversational agent ([ADR-0011](0011-microsoft-foundry-agent-and-generation.md)) runs in the
API as chat completions over `Microsoft.Extensions.AI` `IChatClient` — the same `StoryAgent` drives
local Ollama and a Foundry chat-model deployment. Because chat completions are stateless, the agent
must hold the conversation history itself and resend it each turn. The first cut kept that history
in an in-process dictionary keyed by a generated conversation id.

That store has three problems once we move past a single dev process: it never evicts, so abandoned
conversations grow memory unbounded; a restart or deploy wipes every in-flight conversation; and it
is per-process, so it does not work behind more than one API instance. Worse, on a miss the agent
silently re-seeded an empty history under the same id — the user kept chatting while the agent had
forgotten everything, with no signal. A `Story` is only created once a brief is generated, so the
conversation needs durable identity *before* any story exists.

## Decision

We will persist conversations in PostgreSQL. A `Conversation` aggregate (its id is the conversation
id handed to the client) owns an ordered, append-only collection of `ConversationMessage` rows
(`Seq`, `Role`, `Content`). `StoryAgent` reads and writes this history through the
`StoryWalkerDbContext` each turn instead of an in-memory map. An unknown or malformed conversation
id now raises `ConversationNotFoundException`, which the endpoint maps to **404** so the client
restarts cleanly rather than silently losing context.

## Consequences

### Positive
- Conversations survive restarts/deploys and work across multiple API instances (history lives in
  the database, not process memory).
- No unbounded in-memory growth; abandoned conversations are ordinary rows a retention job can
  prune by age/status.
- Unknown ids fail loudly (404) instead of silently resetting the conversation.
- A natural home for the eventual draft `Story` link and per-user ownership.

### Negative / trade-offs accepted
- A database round-trip per turn and a new table pair to migrate and maintain.
- Full history is reloaded and resent each turn (fine at story-planning lengths; revisit with
  summarisation/windowing if conversations grow long).
- `StoryAgent` (and `IStoryAgent`) is now scoped, since it depends on the scoped DbContext.

### Things this decision obliges us to build or watch
- A retention/cleanup path for stale `Active` conversations (TTL or scheduled purge).
- Set `Status = Completed` when the agent emits a brief ([ADR-0013](0013-agent-authored-generation-brief.md)),
  and populate `Conversation.UserId` once Clerk auth scopes the endpoint.
- Storage growth of message rows; consider summarisation if turn counts climb.

## Alternatives considered

### Keep the in-memory store (add TTL/eviction)
Cheapest change — an `IMemoryCache` with sliding expiration fixes the unbounded-growth issue.
Rejected: still volatile across restarts and not shared across instances, and the silent
context-loss on a miss remains the default failure mode. Wrong foundation for anything past local
dev.

### Stateless API; client resends the full history each turn
The request carries every prior message; the server stores nothing. Rejected: bloats every request,
pushes history management onto each client, and leaves no server-side record to attach the draft
story to or to scope/clean up per user.

### Single JSON column instead of a child message table
Store the serialized message list on `Conversation`. Rejected: couples the column to a chat-SDK
shape and is harder to query/inspect than first-class rows; a child table matches the existing
relational model (e.g. `Page` under `Story`).
