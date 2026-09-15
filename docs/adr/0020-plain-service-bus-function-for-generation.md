# ADR 0020 — A plain Service Bus function instead of Durable Functions

- **Status:** Accepted
- **Date:** 2026-09-15
- **Deciders:** Luca Martinelli

## Context

[ADR-0004](0004-durable-functions-for-generation.md) chose Durable Functions for a pipeline of five
stages with two parallel fan-out/fan-in phases and a concurrent embedding branch. That pipeline no
longer exists. After [ADR-0019](0019-mvp-scope-reduction.md) the work is linear and short: persist
six pages, render six images, settle the story's status. The embedding branch is gone, the
reference-image stage is gone, and the per-page fan-out was deliberately replaced by a serial loop
because six concurrent calls against an image deployment on default quota produce a 429 storm while
six sequential renders take roughly the "few minutes" the agent already promises the user.

Re-examined against the pipeline as it now stands, four of the five reasons to use Durable have
lapsed:

- **Checkpointing between stages** — already provided by the database. `PersistPagesAsync` returns
  early when the story has pages; `RenderAsync` returns early when the page has an `ImageUrl`. These
  are the idempotency guarantees [ADR-0005](0005-idempotency-and-failure-handling.md) already
  required. A redelivered message resumes at the first page without a picture.
- **Deduplicating redelivered messages** — the same idempotency covers it. The orchestration
  instance id was a second mechanism for a problem already solved.
- **Escaping the single-function timeout** — the Consumption plan (5 min default, 10 max) is now a
  legacy plan; Microsoft directs new serverless apps to **Flex Consumption**, whose `functionTimeout`
  defaults to **30 minutes**. Six images at even 60 s each sit well inside that.
- **Instance status observability** — real, but marginal for an MVP whose status is already a column
  on `Story` and a `PageStatus` per page.

The fifth reason — **per-step retry with exponential backoff** — is genuine and had to be replaced
rather than dropped. Throwing straight out of a triggered function abandons the Service Bus message,
which becomes available again immediately, so a throttled deployment would burn the queue's ten
deliveries in seconds instead of waiting for the throttle to clear.

There is also a cost consideration: the Durable task hub polls its control queues continuously. For
a workload of a few stories a day those storage transactions can exceed the cost of the executions
themselves.

## Decision

We will replace the orchestrator and its four activities with a single Service Bus-triggered
function, `GenerateStory`, which persists the pages, renders each image in order, and settles the
story. Retrying transient failures moves into `PageImageRenderer`, which backs off in-process
(3 attempts, 5 s doubling) while the Service Bus extension renews the message lock; on exhaustion it
rethrows so the message is redelivered and the run resumes from the first unrendered page.

## Consequences

### Positive
- Four files and 207 lines become one file and about 70. The `Extensions.DurableTask` package, the
  `hubName` configuration, and the `WEBSITE_HOSTNAME` workaround the `DurableClient` binding needed
  in a container all disappear.
- No orchestrator determinism rules to respect, and the pipeline can be stepped through in a
  debugger.
- **Every one of the service tests was unaffected by this change.** Because the activities were
  already thin shells over `StoryPersister` and `PageImageRenderer`, the swap touched only the
  Functions layer.
- No Durable task hub polling.

### Negative / trade-offs accepted
- Retry is coarser. Durable retried one activity; we now retry a page in-process and, on exhaustion,
  re-run the whole function — cheap only because every completed step is a no-op.
- We lose the orchestration instance history as a debugging aid; the database and the logs are what
  is left.
- Growing the pipeline into something Durable is genuinely good at — timers, waiting on human
  approval, real fan-out over many more pages — would mean bringing it back.

### Things this decision obliges us to build or watch
- Watch the wall-clock time of a full run against the real image deployment. Six serial renders must
  stay comfortably inside `functionTimeout` (currently 20 minutes in `host.json`); if a slower model
  pushes it, either raise the timeout or split the work across messages.
- `maxConcurrentCalls` is pinned to 1 so two stories cannot compete for the same image quota. Revisit
  when quota allows.
- Keep the idempotency guarantees honest. They are no longer a nicety: they are the resume mechanism.

## Alternatives considered

### Keep Durable Functions
Viable — it works today and is tested. Rejected because it is now carrying four files to provide
checkpointing the database already provides, dedupe the idempotency already provides, and a timeout
escape the hosting plan already provides. Retaining it for the retry policy alone did not justify
the surface.

### One message per page
Would give per-page retry and back-pressure for free through the queue. Rejected as more moving
parts than Durable, not fewer — a second queue, a fan-in problem to decide when the story is
complete, and message ordering to reason about.

## Supersedes

- [ADR-0004](0004-durable-functions-for-generation.md) — Durable Functions for the generation
  pipeline.
