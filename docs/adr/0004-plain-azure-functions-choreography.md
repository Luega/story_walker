# ADR 0004 — Plain Azure Functions (choreography) for the generation pipeline

- **Status:** Accepted
- **Date:** 2026-06-09
- **Deciders:** Story Walker team

## Context

The generation pipeline is an **ordered sequence with one parallel stage**:

1. Generate the **bible** (characters, art style, palette, outline),
2. Generate the **full story text** in one coherent pass,
3. Generate/inherit **character reference images**,
4. Generate **per-page images in parallel** (fan-out),
5. Mark the story complete.

Steps 1–3 must run in order; step 4 fans out across the pages. We evaluated Durable
Functions, plain queue-triggered Functions, and a Container Apps worker. The deciding
factors were timeout safety, cost at low/spiky volume, programming-model simplicity, and
fit with the message-driven design in [ADR-0003](0003-async-generation-via-service-bus.md).

## Decision

We will implement the pipeline as **plain, queue-triggered Azure Functions coordinated by
choreography** — no Durable Functions extension. Each stage is its own Function bound to a
Service Bus queue; when a stage finishes it enqueues the message(s) for the next stage:

```
bible-queue    → Fn_GenerateBible    → enqueues text msg
text-queue     → Fn_GenerateText     → enqueues refimage msg
refimage-queue → Fn_GenerateRefImage → enqueues N pageimage msgs   (fan-out)
pageimage-queue→ Fn_GeneratePageImage → (parallel) per-page image, then completion check
```

Steps 1–3 are sequential because each enqueues exactly one next message; step 4 is parallel
because step 3 enqueues many messages at once and the runtime scales the consumer out.

## Consequences

### Positive
- No per-execution timeout problem: each Function does **one** short unit of work.
- Plain .NET with no orchestrator constraints (no determinism rules on `DateTime`, randomness, I/O).
- Natural parallelism for step 4 via the runtime's queue-driven scale-out.
- Scales to zero; cost is dominated by AI inference, not hosting (a wash at our volume).
- Per-step retries and dead-lettering come from Service Bus.

### Negative / trade-offs accepted
- No built-in orchestration: fan-in ("all pages done?"), ordering, and visibility are ours
  to implement (see [ADR-0005](0005-completion-counter-idempotency-deadletter.md)).
- Cold starts on the Consumption plan delay individual short steps (acceptable; not a long job).
- Pipeline state is reconstructed from our own DB status columns, not an orchestration store.

### Things this decision obliges us to build or watch
- The atomic completion counter, idempotency guards, and poison-message policy (ADR-0005).
- Clear `status` columns on `Story` and `Page` for observability.

## Alternatives considered

### Durable Functions
Models the ordered+fan-out pipeline natively (sequential `await`s, `Task.WhenAll` for
fan-in, built-in retries/checkpointing). Rejected (for now): the orchestrator's
deterministic-code constraints add a learning curve, and our pipeline is simple enough that
the bookkeeping we hand-roll is small. A strong future option if the workflow grows.

### Container Apps worker
Plain .NET, warm replicas, same stack as the API; pipeline as straight-line code with KEDA
queue scaling. Rejected (for now): the team prefers serverless scale-to-zero and per-step
isolation; marginally cheaper only at sustained high throughput.
