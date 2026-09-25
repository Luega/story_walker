# ADR 0026 — One failed page fails the story, and a failed story can only be deleted

- **Status:** Accepted
- **Date:** 2026-09-25
- **Deciders:** Luca Martinelli

## Context

The worker settles a story `Complete` when **at least one** page rendered
(`StoryPersister.CompleteStoryAsync`). The idea was that a story with five of six pictures is still
worth reading. In practice this gives a picture book with a hole in it: on 2026-09-24 a story about
George was delivered as `Complete` with no illustration on page 3.

Since [ADR-0025](0025-rewrite-illustration-prompt-after-moderation.md), a page that ends `Failed` has
already had its second chance: a moderation refusal gets one rewritten prompt before the page gives
up. So a `Failed` page is a real failure, not a first refusal.

## Decision

1. **A story is `Complete` only when every page is `Complete`.** Any other outcome makes it
   `Failed`: a page that failed, or a page never rendered.
2. **The worker stops at the first page that fails**, after the rewrite attempt, and settles the
   story. The pages after it stay `Pending` and are never paid for, since a failed story can only be
   deleted anyway.
3. **A failed story can only be deleted.** The dashboard card cannot be opened (already the case
   for every status other than `Complete`) and has no rename action. It is covered by a light red
   overlay that marks it inactive, and its delete action is always visible. `PATCH /stories/{id}`
   answers `409 Conflict` for a failed story, so the rule holds on the server too.
4. **Existing data is left as it is.** Stories already settled `Complete` with a failed page keep
   their status. The rule applies to stories generated from now on.

## Consequences

### Positive
- A story the child opens is always complete: six pages, six pictures.
- A failed story costs at most the pages up to the first failure, not all six.

### Negative / trade-offs accepted
- A single unlucky page throws away five good pictures. There is no way to retry just that page;
  the parent has to start a new story.
- Stories settled before this change can still be `Complete` with a missing picture.

### Things this decision obliges us to build or watch
- If failed stories become common, a "try that page again" action, or regenerating the story from
  the saved brief, becomes worth building. Neither exists today.

## Alternatives considered

### Keep "at least one page rendered"
This is what we had. It delivers incomplete books, which is exactly what prompted this ADR.

### Render every page even after a failure
It shows the parent how much of the story worked, but a failed story is delete-only, so those
pictures would be paid for and never seen.
