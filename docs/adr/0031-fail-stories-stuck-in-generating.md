# ADR 0031 — A timer fails stories stuck in Generating

- **Status:** Accepted
- **Date:** 2026-09-29
- **Deciders:** Luca Martinelli

> **Amended (2026-09-29) by [ADR-0032](0032-settle-and-remove-dead-lettered-messages.md).** Expired
> messages are now dead-lettered too, and a function on the dead-letter queue settles their story at
> once and removes the message. This timer remains as the backstop.

## Context

The worker settles every story it gets to run: `Complete`, or `Failed` with a report back to the
conversation ([ADR-0026](0026-one-failed-page-fails-the-story.md),
[ADR-0027](0027-failed-story-returns-to-its-conversation.md)). Permanent image errors are caught and
fail the page instead of throwing, precisely so the message is not redelivered into the dead-letter
queue ([ADR-0020](0020-plain-service-bus-function-for-generation.md)).

Two ways out of the queue never reach that code:

- **Dead-lettering.** An exception the worker does not catch — the database or Blob Storage down for
  longer than the ten deliveries last, a brief that no longer deserialises — abandons the message
  ten times and Service Bus moves it to `story-generation/$DeadLetterQueue`. Nothing reads that
  queue.
- **Expiry.** The queue's `DefaultMessageTimeToLive` is one hour and
  `DeadLetteringOnMessageExpiration` is off, so a message nobody picks up within the hour — the
  worker stopped, or a backlog behind `maxConcurrentCalls: 1` — is dropped without a trace.

Either way the story stays `Generating` for ever. The dashboard shows it as still being made, the
conversation is never reopened, and since a generating story holds one of the week's slots
([ADR-0030](0030-weekly-story-limit.md)) the parent also loses a story they never got.

## Decision

We will add a timer-triggered function, `FailStaleGenerations`, that runs every 15 minutes and
settles every story still `Generating` two hours after it was queued (`Story.CreatedAt`). For each
it reports a technical problem to the conversation, exactly as the worker would (ADR-0027), then
marks the story `Failed`.

The two hours follow from the queue's own limits. A message expires one hour after it is sent, and
Service Bus delivers no expired message, so no run can **start** later than that; a run lasts at
most `functionTimeout`, 30 minutes. After an hour and a half no run can still be going, and the
extra half hour is margin. A story generating for two hours is not slow: it is lost.

We fix the symptom (the story) rather than the cause (the message), so the sweeper does not care
which of the two ways the message left the queue, nor about causes we have not thought of.

## Consequences

### Positive
- No story stays `Generating` indefinitely, and its weekly slot is given back.
- The parent learns what happened the same way as for any other failure: the conversation reopens
  with the agent's advice to try again.
- One mechanism covers dead-lettering, expiry, and anything else that loses a message.

### Negative / trade-offs accepted
- Up to 2 h 15 min before a lost story is settled. Acceptable: this path is for faults, not for the
  ordinary failures the worker already settles within minutes.
- The dead-lettered message itself is still not read or replayed. Replaying it would regenerate a
  story that has already been reported failed; the parent retries from the conversation instead.
- The report names the first page without a picture, or page 1 when the pages were never written.
  The agent is only told it was a technical problem, which is the truth.

### Things this decision obliges us to build or watch
- **The two hours rest on the queue's settings.** If `DefaultMessageTimeToLive` or `functionTimeout`
  grow, the threshold must grow with them, or the sweeper could fail a story a live run is still
  rendering (and which that run would then flip back to `Complete`). The Azure queue, when it is
  provisioned, must use the same one-hour TTL as `docker/servicebus/Config.json`.
- **Dead-lettered messages carry the brief** — text about someone's child — and the dead-letter
  queue does not expire them. They should be purged, or given a retention, before production.
- The report is written before the status changes, so a sweep that fails halfway is retried on the
  next tick; the recorder writes the report only once (ADR-0027).

## Alternatives considered

### A function triggered by the dead-letter queue
Viable, and it would react within seconds. Rejected as the only mechanism because it misses expired
messages, which are dropped rather than dead-lettered while `DeadLetteringOnMessageExpiration` is off.
Turning that on would route both through the dead-letter queue, but the sweeper would still be the
simpler guarantee: it checks the state the parent actually sees.

### Both a dead-letter function and the timer
Faster settlement for the dead-letter case, at the cost of a second function and a second way for a
story to be failed. Not worth it for a fault path at the current volume.
