# ADR 0032 — Dead-lettered messages settle their story and are removed

- **Status:** Accepted
- **Date:** 2026-09-29
- **Deciders:** Luca Martinelli

## Context

[ADR-0031](0031-fail-stories-stuck-in-generating.md) fails a story stuck in `Generating` from a
timer, two hours after it was queued. It left the dead-letter queue unread, and named two problems
with that:

- a story whose message was dead-lettered after ten minutes still waits up to 2 h 15 min before the
  parent learns it failed;
- **a dead-lettered message carries the brief** — text about someone's child — and the dead-letter
  queue never expires it. Nothing removes it.

It also rejected a dead-letter function as the *only* mechanism, because an expired message was
dropped rather than dead-lettered (`DeadLetteringOnMessageExpiration` off).

## Decision

1. **Expired messages are dead-lettered too.** `DeadLetteringOnMessageExpiration` is turned on, so
   both ways of losing a message end in `story-generation/$DeadLetterQueue`.
2. **A function triggered by the dead-letter queue settles the story at once**:
   `SettleDeadLetteredGeneration` reports a technical problem to the conversation and marks the
   story `Failed`, exactly as the timer does (the same `LostGenerationSettler`), then logs the
   story id, the dead-letter reason and the delivery count — never the body.
3. **The message is then completed**, which removes it. The brief is already in the database (the
   story row, and the pages if they were written); the only copy that outlived its purpose is gone.
4. **The timer stays**, as the backstop for anything that loses a message without dead-lettering it,
   or a dead-letter function that is down.

A dead-lettered message is **not replayed**. Ten deliveries have already failed; the parent is told
to try again from the conversation, which starts a fresh story.

## Consequences

### Positive
- A dead-lettered or expired story is settled within seconds of leaving the queue, not hours.
- No brief is kept in Service Bus beyond its run.
- The reason Service Bus gives (`MaxDeliveryCountExceeded`, `TTLExpiredException`) reaches the
  logs, next to the story id.

### Negative / trade-offs accepted
- A dead-lettered message is gone once handled: to find out why a run failed ten times we have the
  logs, not the message. The brief itself is still in the database.
- Two ways a story can be failed for a lost message. Both go through `LostGenerationSettler`, which
  does nothing for a story no longer `Generating`, so whichever comes second is a no-op.
- A message that cannot be tied to a story (its id is not a story id) is only logged and removed.

### Things this decision obliges us to build or watch
- The Azure queue must have `DeadLetteringOnMessageExpiration` on, like the emulator's.
- If the dead-letter function itself keeps failing, its messages are redelivered from the
  dead-letter queue indefinitely (there is no dead-letter queue of the dead-letter queue); the timer
  still settles the story, and the failures show in the logs.

## Alternatives considered

### Replay dead-lettered messages onto the main queue
Would recover stories lost to an outage that has since passed. Rejected: without a cap it loops on a
message that fails for good, and a replay after the parent was told the story failed produces a
story nobody is waiting for.

### Keep dead-lettered messages for inspection, with a retention
Useful for debugging. Rejected for now: the message is a copy of data already in the database, and
the part worth keeping — why it failed — is logged.

## Amends

- [ADR-0031](0031-fail-stories-stuck-in-generating.md) — the timer is now the backstop; the
  dead-letter queue is read.
