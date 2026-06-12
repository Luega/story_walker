# ADR 0008 — Per-character reference images for visual consistency

- **Status:** Accepted
- **Date:** 2026-06-09
- **Deciders:** Luca Martinelli

## Context

Illustrations across a story — and across other stories that reuse its characters — must look
like the same book: the same character faces, outfits, art style, and palette on every page.
Image models **drift** across independent calls. A naive "one reference image per story" model
also fails when a story introduces a **new character**, or reuses a character from another story.

## Decision

Visual consistency is enforced with three layers, anchored on **per-character** assets:

1. **A consistent style + a reusable character set** — art style and palette live on the
   `Story`; characters are **user-owned and reusable**. A new story reuses an existing story's
   art style and characters, and adds new characters as needed — nothing is frozen into a
   fixed snapshot.
2. **Per-character reference images** — each `Character` has its own `reference_image_url`
   and `seed`. Every page image is conditioned on the reference image(s) of the characters
   appearing on that page (see `CharacterPage` in the
   [ER diagram](../diagrams/er-diagram.md)). This is the strongest consistency lever.
3. **A locked seed per character** — fixes the image model's starting noise, removing one
   source of variation. (A minor lever: the prompt changes per page, so the reference image
   and the character's description do the heavy lifting; the seed fine-tunes reproducibility.)

Each reference image is generated from the character's `illustration_prompt`, authored by the
agent in the brief ([ADR-0013](0013-agent-authored-generation-brief.md)). When a story reuses
characters, the agent marks returning ones with `existingCharacterId`, so the reference-image
step **reuses their existing assets** and **generates new reference images only for genuinely
new characters**.

## Consequences

### Positive
- Characters look identical across pages and across every story that reuses them.
- New and reused characters are both first-class — a story can introduce new characters or
  reuse existing ones without breaking continuity.
- Reusing existing assets saves generation cost and time for returning characters.

### Negative / trade-offs accepted
- Depends on the image model supporting reference/image conditioning; if a model variant
  lacks it, we fall back to prompt text + seed only (weaker but functional).
- More entities to model (`Character`, `CharacterPage`) and more prompt-assembly logic.

### Things this decision obliges us to build or watch
- Mapping which characters appear on each page (from the brief's `pages[].characters`) to
  pass the right reference images.
- The agent resolves new vs. existing characters and encodes returning ones as
  `existingCharacterId` in the brief ([ADR-0013](0013-agent-authored-generation-brief.md)).
- A documented fallback when reference conditioning is unavailable.

## Alternatives considered

### One reference image per story
Simple. Rejected: cannot represent new characters introduced in or reused across stories, and
conflates multiple characters into one anchor.

### Seed + prompt text only (no reference images)
Lighter, no dependency on reference conditioning. Rejected as the default: noticeably weaker
character consistency. Retained as the fallback when a model lacks reference conditioning.

### Fine-tuning a model per character
Strongest possible fidelity. Rejected: far too slow and costly per story for this product.
