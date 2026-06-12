# ADR 0003 — Asynchronous generation via Azure Service Bus

- **Status:** Accepted
- **Date:** 2026-06-09
- **Deciders:** Luca Martinelli

## Context

Generating a story takes from many seconds to several minutes: it involves a sequence of
AI calls (bible, full text, reference images) plus parallel per-page image generation. An
HTTP request must not block for this duration. We need to decouple the user's request from
the long-running generation work, survive worker restarts, and absorb load spikes.

## Decision

Story generation is **fully asynchronous and message-driven** over **Azure Service Bus**.
When the agent confirms the story details, the API:

1. Persists the `Story` (`status = Generating`),
2. Enqueues **one** message — the agent-authored **generation brief** as validated JSON
   ([ADR-0013](0013-agent-authored-generation-brief.md)) — on the ingestion queue,
3. Returns **`202 Accepted`** immediately.

The API never waits for generation. A Service-Bus-triggered starter begins the Durable
Functions orchestration that generates the story (see
[ADR-0004](0004-durable-functions-for-generation.md)); progress reaches the browser via
[SignalR](0006-azure-signalr-for-realtime.md).

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
- At-least-once delivery means consumers must be **idempotent** (see [ADR-0005](0005-idempotency-and-failure-handling.md)).
- An extra managed service to operate and pay for.

### Things this decision obliges us to build or watch

- A single ingestion queue carrying the generation-brief JSON ([ADR-0013](0013-agent-authored-generation-brief.md));
  the per-stage coordination lives in the Durable orchestration ([ADR-0004](0004-durable-functions-for-generation.md)).
- Message size: the brief (~5–20 KB for ≤10 pages) is well within Service Bus limits
  (256 KB Standard / 1 MB Premium). If a brief ever exceeds the limit, fall back to the
  **claim-check** pattern — store the brief in Blob and enqueue a reference.
- Idempotency, retry, and dead-letter policies.
- Surfacing `Generating` / `Failed` / `Complete` states in the dashboard.

## Alternatives considered

### Synchronous generation in the API request

Simplest mentally. Rejected: would block requests for minutes, time out, and couple the API
to generation throughput.

### Azure Storage Queues

Cheaper and simpler. Rejected: Service Bus offers richer semantics we want (sessions,
dead-letter queues, topics/subscriptions for future fan-out, better poison handling).
