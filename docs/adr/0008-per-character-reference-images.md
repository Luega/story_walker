# ADR 0008 — Per-character reference images for visual consistency

- **Status:** Accepted
- **Date:** 2026-06-09
- **Deciders:** Story Walker team

## Context

Illustrations across a story — and across its sequels/prequels — must look like the same
book: the same character faces, outfits, art style, and palette on every page. Image models
**drift** across independent calls. A naive "one reference image per story" model also fails
when a sequel introduces a **new character** who has no entry in the original.

## Decision

Visual consistency is enforced with three layers, anchored on **per-character** assets:

1. **A living, per-series bible** — art style, palette, and a *set of characters*. A sequel
   **inherits** the parent's bible and **appends** new characters/locations rather than
   freezing a snapshot.
2. **Per-character reference images** — each `Character` has its own `reference_image_url`
   and `seed`. Every page image is conditioned on the reference image(s) of the characters
   appearing on that page (see `PageCharacter` in the
   [ER diagram](../diagrams/er-diagram.md)). This is the strongest consistency lever.
3. **A locked seed per character** — fixes the image model's starting noise, removing one
   source of variation. (A minor lever: the prompt changes per page, so the reference image
   and bible text do the heavy lifting; the seed fine-tunes reproducibility.)

For a sequel/prequel, the reference-image step **reuses existing characters' assets** and
**generates new reference images only for genuinely new characters**, appending them to the
series bible.

## Consequences

### Positive
- Characters look identical across pages and across an entire series.
- New characters are a first-class case, not an edge case — sequels can introduce them
  without breaking continuity.
- Reusing parent assets saves generation cost and time for returning characters.

### Negative / trade-offs accepted
- Depends on the image model supporting reference/image conditioning; if a model variant
  lacks it, we fall back to bible-text + seed only (weaker but functional).
- More entities to model (`Character`, `PageCharacter`) and more prompt-assembly logic.

### Things this decision obliges us to build or watch
- Detecting which characters appear on each page (to pass the right reference images).
- Detecting new vs. existing characters from the agent conversation for sequels.
- A documented fallback when reference conditioning is unavailable.

## Alternatives considered

### One reference image per story
Simple. Rejected: cannot represent new characters introduced in a sequel, and conflates
multiple characters into one anchor.

### Seed + bible text only (no reference images)
Lighter, no dependency on reference conditioning. Rejected as the default: noticeably weaker
character consistency. Retained as the fallback when a model lacks reference conditioning.

### Fine-tuning a model per character
Strongest possible fidelity. Rejected: far too slow and costly per story for this product.
