# ADR 0004 — Durable Functions for the generation pipeline

- **Status:** Accepted
- **Date:** 2026-06-09
- **Deciders:** Luca Martinelli

## Context

The text, bible, and all illustration prompts arrive in the agent-authored generation brief
([ADR-0013](0013-agent-authored-generation-brief.md)); the worker only **renders images** and
**embeds the text**. That leaves an **ordered sequence with two parallel image stages**, plus
an independent embedding step:

1. **Persist the brief** — `Character`, `Page`, and `Character_Page` rows — from the JSON (the
   `Story` row is created by the API at enqueue time, see
   [ADR-0003](0003-async-generation-via-service-bus.md)),
2. **Resolve character reference images in parallel** (fan-out): for each character, either
   generate a new reference image from its `illustrationPrompt`, or retrieve the existing one
   if the character is already present in the database,
3. Generate **per-page images in parallel** (fan-out), each from the page's
   `illustrationPrompt`, conditioned on the reference images of the characters on that page,
4. **Embed the story** — write the story-summary and character-description vectors to the
   `STORY`/`CHARACTER` `embedding` columns for reuse/discovery
   ([ADR-0009](0009-postgresql-pgvector-continuity.md)) — this depends only on the text (already
   in the brief), so it runs **concurrently** with the image stages,
5. Mark the story complete.

The image stages run in order 2 → 3, each a parallel fan-out with its own **fan-in**: step 3
must not begin until every character reference image from step 2 is ready, and the story is
complete only when every page image _and_ the embedding are done. We evaluated plain
queue-triggered Functions (choreography), Durable Functions, and a Container Apps worker. The
deciding factor was that this is a genuine _orchestration_ — ordered fan-out/fan-in stages with
a concurrent branch — and a plain-Functions approach would force us to hand-roll all of the
coordination ourselves.

## Decision

We will implement the pipeline as **Azure Durable Functions**. An **orchestrator** function
defines the workflow; **activity** functions do the work. Fan-out/fan-in uses `Task.WhenAll`,
so completion of each parallel stage is detected by the framework — no hand-rolled counters.

```csharp
// Orchestrator (deterministic; no I/O, DateTime.Now, or randomness here)
async Task RunAsync(GenerationBrief brief)
{
    var plan = await ctx.CallActivityAsync<StoryPlan>("PersistBrief", brief); // character + page + link rows

    // Embedding depends only on the text → start it now, concurrent with the image work
    var embed = ctx.CallActivityAsync("EmbedStory", plan.StoryId); // → STORY/CHARACTER embedding cols (ADR-0009)

    // Fan-out #1 — resolve character reference images (generate from prompt, or retrieve existing)
    await Task.WhenAll(plan.Characters.Select(c =>
        ctx.CallActivityAsync("ResolveRefImage", c)));      // fan-in: WhenAll

    // Fan-out #2 — per-page images, from each page's prompt, conditioned on the resolved references
    await Task.WhenAll(plan.Pages.Select(p =>
        ctx.CallActivityAsync("GeneratePageImage", p)));    // fan-in: WhenAll

    await embed;                                            // ensure embeddings persisted
    await ctx.CallActivityAsync("CompleteStory", plan.StoryId);
}
```

The orchestration does **no text or bible generation** — both arrive in the brief
([ADR-0013](0013-agent-authored-generation-brief.md)); it only renders images and embeds the
text. The API stays
fire-and-forget (see [ADR-0003](0003-async-generation-via-service-bus.md)): it enqueues the
brief JSON on Service Bus and returns `202`; a Service-Bus-triggered **starter** function
begins the orchestration. Service Bus is now only the **ingestion trigger** — the per-stage
queues are gone, because the orchestrator coordinates the stages internally via its task hub.

## Consequences

### Positive

- **Fan-in is built in** (`Task.WhenAll`) for both parallel stages — no atomic completion
  counters to design, test, or get subtly wrong.
- **Automatic retries** (per-activity retry policies with back-off) and **checkpointing**: if
  the worker restarts mid-pipeline, the orchestration resumes from the last checkpoint.
- **Orchestration status/visibility** out of the box (query the instance) for debugging.
- Still serverless and scales to zero (Consumption/Flex); cost is dominated by AI inference —
  the Durable storage-transaction overhead is negligible at our volume.

### Negative / trade-offs accepted

- **Orchestrator determinism constraints**: no `DateTime.Now`, `Guid.NewGuid()`, randomness,
  or direct I/O inside the orchestrator — all of that must live in activities.
- Adds an Azure Storage **task hub** for orchestration state (event sourcing); small,
  metered, but a new moving part.
- New programming model and learning curve.
- Activities still execute **at-least-once**, so idempotency is still required (see
  [ADR-0005](0005-idempotency-and-failure-handling.md)).

### Things this decision obliges us to build or watch

- Idempotency guards on activities and a failure policy ([ADR-0005](0005-idempotency-and-failure-handling.md)).
- Keep domain `status` columns on `Story` and `Page` for the dashboard — orchestration
  runtime status is separate from the domain state the UI reads.

## Alternatives considered

### Plain Functions (queue choreography)

Each stage a separate queue-triggered Function; stages hand off via Service Bus messages; two
**hand-rolled atomic completion counters** detect each fan-in. Rejected: with two fan-ins,
idempotency on three keys, and a poison-message policy, the bespoke coordination is
significant and error-prone — Durable provides all of it natively. The hosting-cost
difference is negligible at our volume, so cost is not a deciding factor.

### Container Apps worker

Plain .NET, warm replicas, straight-line code with `Task.WhenAll`, KEDA queue scaling.
Rejected (for now): we prefer serverless scale-to-zero, and Durable additionally gives
checkpointing, retries, and orchestration visibility we would otherwise build by hand.
