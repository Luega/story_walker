# apps/functions — Azure Durable Functions (generation worker)

The story-generation worker: an **Azure Durable Functions** orchestration. A
Service-Bus-triggered starter begins the orchestration; the orchestrator drives the ordered
stages and uses `Task.WhenAll` for the two parallel fan-out/fan-in stages (see
[ADR-0004](../../docs/adr/0004-durable-functions-for-generation.md)).

The text, bible, and all illustration prompts arrive in the agent-authored brief
([ADR-0013](../../docs/adr/0013-agent-authored-generation-brief.md)); the worker only renders
images and embeds the text.

Orchestrator + activities:

1. `PersistBrief` — write the `Character`, `Page`, and `CharacterPage` rows (text + prompts)
   from the brief JSON (the `Story` row is created by the API at enqueue time).
2. `EmbedStory` — write the story-summary and character-description vectors to the
   `STORY`/`CHARACTER` `embedding` columns for retrieval
   ([ADR-0009](../../docs/adr/0009-postgresql-pgvector-continuity.md)); depends only on the
   text, so it runs concurrently with the image stages.
3. `ResolveRefImage` ×M (fan-out) — generate a reference image from the character's
   `illustrationPrompt` for a new character, or retrieve the existing one for a returning
   character; the orchestrator awaits all (fan-in).
4. `GeneratePageImage` ×N (fan-out) — per-page image from the page's `illustrationPrompt`,
   conditioned on the resolved references; pushes SignalR progress; the orchestrator awaits
   all (fan-in).
5. `CompleteStory` — mark complete, push the final SignalR event.

Activities are **idempotent** and use Durable retry policies; failure handling is described in
[ADR-0005](../../docs/adr/0005-idempotency-and-failure-handling.md).

References the shared libraries in [`../../src`](../../src) for the EF Core model and the
Service Bus message contract.
