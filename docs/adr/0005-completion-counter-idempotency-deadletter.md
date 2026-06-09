# ADR 0005 — Atomic completion counter, idempotency & dead-letter policy

- **Status:** Accepted
- **Date:** 2026-06-09
- **Deciders:** Story Walker team

## Context

Choreographed plain Functions ([ADR-0004](0004-plain-azure-functions-choreography.md)) have
no orchestrator. Three responsibilities that Durable Functions would provide for free are
therefore ours to implement: detecting that the parallel image stage has fully completed
("fan-in"), tolerating Service Bus's **at-least-once** delivery, and handling steps that
keep failing.

## Decision

### 1. Fan-in via an atomic completion counter
`Story` carries `page_count` (N) and `completed_pages`. When a page image finishes, the
Function increments the counter **atomically** and reads the new value back in a single
statement:

```sql
UPDATE story SET completed_pages = completed_pages + 1
WHERE id = @storyId
RETURNING completed_pages;
```

The single instance that gets back a value equal to `page_count` is the last to finish; it
sets `status = Complete` and fires the SignalR "story complete" event. Because the increment
is atomic, "complete" fires **exactly once** even if pages finish simultaneously.

### 2. Idempotency
Every Function is safe to run more than once. Each step **checks state before doing work**,
keyed on its natural identity (`storyId`, or `(storyId, pageNo)` for pages): if the page is
already `Complete`, it acks and stops — no second model call, no double increment.

### 3. Dead-letter / poison policy
Service Bus retries a failing message up to a configured max, then dead-letters it. A
monitored process (or a dead-letter-triggered Function) marks the affected `Page` as
`Failed`. A story with one or more failed pages is marked `Failed` (not silently stuck);
the dashboard surfaces this and offers a retry of the failed pages only.

## Consequences

### Positive
- "Story complete" is detected reliably and fires once, with no orchestrator.
- Duplicate deliveries cannot cause duplicate generations, double counts, or double completion.
- Stories never hang indefinitely on a failing page.

### Negative / trade-offs accepted
- Correctness depends on the increment being genuinely atomic and idempotency guards being
  present in every step — easy to get subtly wrong, must be covered by tests.
- A small amount of bespoke coordination code to maintain.

### Things this decision obliges us to build or watch
- Tests for concurrent completion (two pages finishing at once → one completion event).
- Tests for duplicate delivery (same message twice → one effect).
- A dead-letter monitor and a "retry failed pages" path.

## Alternatives considered

### Durable Functions fan-in
Removes all of the above. Rejected per [ADR-0004](0004-plain-azure-functions-choreography.md)
in favour of the simpler programming model; this ADR is the cost of that choice.

### Counting rows instead of a counter column
`SELECT count(*) FROM page WHERE story_id=… AND status='Complete'` then compare to N.
Rejected: prone to race conditions between the count and the completion decision; the atomic
`UPDATE … RETURNING` is race-free.
