# ADR 0025 — Rewrite an illustration prompt once after a moderation refusal

- **Status:** Accepted
- **Date:** 2026-09-25
- **Deciders:** Luca Martinelli

## Context

On 2026-09-24 page 3 of a story about George, a rabbit wearing a hat with paper ears, ended
`Failed`. The image deployment (`gpt-image-1-mini`) answered `400` with code `moderation_blocked`
and category `other`. The story was fine: it taught a child to be careful with scissors. The page's
illustration prompt was the problem. It had George holding scissors near the hat and added that
"his real ears are clearly visible and intact". A negation like that brings in exactly the idea it
means to rule out.

`PageImageRenderer` treats every non-transient failure as permanent
([ADR-0005](0005-idempotency-and-failure-handling.md), [ADR-0020](0020-plain-service-bus-function-for-generation.md)).
The page is tried once, marked `Failed`, and stays that way. Sending the same prompt again is
pointless: the classifier gives the same answer every time.

The reader's main worry is coherence. A rewrite that drifts could produce a picture, or a story,
that no longer fits the rest of the book.

## Decision

When the image model refuses a page's prompt on moderation grounds, the worker asks the chat model
to rewrite **only that page's illustration prompt**, and tries again **once**.

Coherence is protected by what the rewrite is allowed to touch:

1. **The story text is never changed.** Only `Page.IllustrationPrompt` is rewritten.
   `Page.Content` stays exactly as the parent confirmed it, so at worst one picture changes and the
   plot never does.
2. **The rewrite is anchored to the page text.** The rewriter gets the page's text and must
   illustrate the same moment, with the same characters and setting. It may only change what could
   read as dangerous: sharp objects put down or out of frame, no injury or body-part allusions, and
   no negations of harm.
3. **Character descriptions are copied word for word.** They are the only thing keeping characters
   recognisable across pages ([ADR-0019](0019-mvp-scope-reduction.md)). The art style is appended in
   code by `BuildPrompt`, so it cannot drift.
4. **One rewrite, no chain.** If the rewritten prompt is refused as well, the page is `Failed` as
   before.

The rewritten prompt is saved to `Page.IllustrationPrompt`, so the database records what was
actually drawn. Only a moderation refusal triggers this. Any other permanent failure, such as a 401
or a 404, behaves exactly as before.

The worker therefore needs a chat client. It reads `Foundry:Deployment`, the same chat deployment
the API uses, and refuses to start without it.

## Consequences

### Positive
- A legitimate story no longer loses a page to a clumsily worded scene.
- The chat model is called only when the image filter actually refuses, so the other pages cost
  nothing extra.

### Negative / trade-offs accepted
- The rewritten picture can show a slightly different beat of the scene than the original prompt
  described. It still matches the page text, which is what the child reads.
- The original prompt survives only in the log line of the first refusal, not in the database.
- A refused page takes one more chat call and one more image call (about 30–40 s) before it
  completes. That is still inside `functionTimeout`.
- The worker gains a required setting and a dependency on the chat deployment.

### Things this decision obliges us to build or watch
- Watch how often the rewrite is needed and how often it still fails. If it is common, a rule for
  illustration prompts in the authoring prompt becomes worth adding.
- Moderation codes are matched by name (`moderation_blocked`, `content_policy_violation`,
  `content_filter`, `ResponsibleAIPolicyViolation`). A provider that changes its code falls back to
  a `Failed` page, not to an error.

## Alternatives considered

### A reviewer agent that checks the brief before generation starts
It would have to predict an opaque classifier (category `other`), so it would block legitimate
stories like this one and still miss refusals, while adding latency and cost to every story.

### A rule for illustration prompts in the authoring prompt
It costs nothing and would prevent many refusals, but it cannot guarantee anything, and it does not
help once a refusal has happened. We may still add it later. For now we keep only the fix that acts
on the classifier's real verdict.

### Retrying the same prompt
The classifier is deterministic enough that it would be refused again. It only spends the budget.
