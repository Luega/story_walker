# ADR 0005 — Idempotency and failure handling in the generation pipeline

- **Status:** Accepted
- **Date:** 2026-06-09
- **Deciders:** Luca Martinelli

> **Amended.** The idempotency rule (§1) stands, and is now the resume mechanism itself. The rest
> describes Durable Functions, replaced on 2026-09-15 by a plain Service Bus function
> ([ADR-0020](0020-plain-service-bus-function-for-generation.md)): retries (§2) happen in-process in
> `PageImageRenderer`, then through Service Bus redelivery. Of §3, a failed story is no longer
> retried but can only be deleted ([ADR-0026](0026-one-failed-page-fails-the-story.md)), and a story
> whose message is lost — dead-lettered or expired — is failed from the dead-letter queue
> ([ADR-0032](0032-settle-and-remove-dead-lettered-messages.md)), with a timer as backstop
> ([ADR-0031](0031-fail-stories-stuck-in-generating.md)).

## Context

With Durable Functions ([ADR-0004](0004-durable-functions-for-generation.md)), the
orchestrator handles the hard coordination for us: **fan-in** for both parallel stages
(`Task.WhenAll`), **checkpointing**, and **automatic retries**. So the atomic completion
counters and the manual fan-in we would have hand-rolled under plain choreography are no
longer needed.

Two responsibilities remain ours, because Durable guarantees **at-least-once** execution of
activities (an activity can run more than once — on retry, or if the host crashes after the
work but before the result is recorded): keeping activities **idempotent**, and defining what
happens when an activity **fails permanently**.

## Decision

### 1. Idempotent activities
Every activity **checks state before doing expensive work**, keyed on its natural identity
(`storyId`; `(storyId, characterId)` for reference images; `(storyId, pageNo)` for page
images). If the unit is already done, the activity returns the existing result instead of
calling Foundry again — no duplicate generation, no duplicate Blob upload. A reused character
is the same case: if a reference image already exists, the activity retrieves it rather than
generating one.

### 2. Retries
Each activity is wrapped in a Durable **retry policy** (max attempts + exponential back-off)
to ride out transient Foundry/Blob failures. The orchestrator stays oblivious to transient
errors; only a *permanent* failure surfaces to it.

### 3. Permanent-failure handling
If an activity exhausts its retries, the orchestrator catches the exception and records the
failure in the domain model: the affected `Character` or `Page` is marked `Failed` and the
`Story` is marked `Failed` (never left silently stuck). The dashboard surfaces this and offers
a retry of the failed units — implemented by re-running the orchestration, whose idempotent
activities skip the work already completed.

Fan-in itself needs no DB bookkeeping: `Task.WhenAll` in the orchestrator is the join point.
Domain `status` columns (`Story.status`, `Page.status`) are still written by activities so the
dashboard has a source of truth independent of the Durable runtime.

## Consequences

### Positive
- The orchestrator's `Task.WhenAll` detects each stage's completion — no atomic counters to
  design or test.
- Durable's retry policies and checkpointing handle transient faults and host restarts.
- Idempotent activities make at-least-once execution safe: duplicate runs cost nothing extra.

### Negative / trade-offs accepted
- Idempotency guards must be present in **every** activity — easy to forget; must be covered
  by tests.
- A permanent-failure path (mark Failed, expose retry) is still ours to build.

### Things this decision obliges us to build or watch
- Tests for duplicate activity execution (same unit twice → one effect).
- Per-activity retry policies tuned for Foundry/Blob.
- A "retry failed units" path that re-runs the orchestration safely.

## Alternatives considered

### Hand-rolled atomic completion counters (the plain-choreography approach)
`UPDATE story SET completed_pages = completed_pages + 1 … RETURNING` to detect fan-in, plus a
Service Bus dead-letter policy. Rejected together with plain choreography in
[ADR-0004](0004-durable-functions-for-generation.md): Durable provides fan-in, retries, and
checkpointing natively, so this bespoke coordination is no longer warranted.

### Skipping idempotency (trusting exactly-once)
Simpler activities. Rejected: Durable activities are **at-least-once**, so duplicate runs are
expected; without idempotency they would cause duplicate generations and double Blob uploads.
