# ADR 0003 — Asynchronous generation via Azure Service Bus

- **Status:** Accepted
- **Date:** 2026-06-09
- **Deciders:** Story Walker team

## Context

Generating a story takes from many seconds to several minutes: it involves a sequence of
AI calls (bible, full text, reference images) plus parallel per-page image generation. An
HTTP request must not block for this duration. We need to decouple the user's request from
the long-running generation work, survive worker restarts, and absorb load spikes.

## Decision

Story generation is **fully asynchronous and message-driven** over **Azure Service Bus**.
When the agent confirms the story details, the API:

1. Persists the `Story` (`status = Generating`),
2. Enqueues **one** message on the first generation queue,
3. Returns **`202 Accepted`** immediately.

The API never waits for generation. The pipeline runs entirely in response to Service Bus
messages; progress reaches the browser via [SignalR](0006-azure-signalr-for-realtime.md).

## Consequences

### Positive
- The API stays fast and stateless; requests close in milliseconds.
- The queue buffers spikes and decouples producers from consumers.
- At-least-once delivery + retries + dead-lettering give durability against transient
  failures and worker restarts.
- The same enqueue-and-return contract holds regardless of the worker compute choice.

### Negative / trade-offs accepted
- Eventual consistency: the story is not ready when the request returns; the UI must reflect
  in-progress state.
- At-least-once delivery means consumers must be **idempotent** (see [ADR-0005](0005-completion-counter-idempotency-deadletter.md)).
- An extra managed service to operate and pay for.

### Things this decision obliges us to build or watch
- Queue topology (one queue per pipeline stage) and message schemas.
- Idempotency, retry, and dead-letter policies (ADR-0005).
- Surfacing `Generating` / `Failed` / `Complete` states in the dashboard.

## Alternatives considered

### Synchronous generation in the API request
Simplest mentally. Rejected: would block requests for minutes, time out, and couple the API
to generation throughput.

### Azure Storage Queues
Cheaper and simpler. Rejected: Service Bus offers richer semantics we want (sessions,
dead-letter queues, topics/subscriptions for future fan-out, better poison handling).
