# ADR 0019 — MVP scope reduction: self-contained six-page stories

- **Status:** Accepted
- **Date:** 2026-09-15
- **Deciders:** Luca Martinelli

## Context

The data model was designed for a product considerably larger than the one we need to ship first:
character reuse across stories ([ADR-0008](0008-per-character-reference-images.md)), semantic
discovery over pgvector ([ADR-0009](0009-postgresql-pgvector-continuity.md)), a `STORY` self-FK for
"another adventure with Luna", and a `CHARACTER_PAGE` join.

None of it was implemented. The `embedding` columns were plain `real[]` that nothing ever wrote,
because the `EmbedStory` activity was never built. `Story.Summary` was `required` and always written
as `""`. And `StoryAgent.ForceNewCharacters` existed **only to blank out** the reuse links on every
brief — the reuse fields were carried through four layers so that one method could null them.

The target is a demonstrable MVP in roughly 56 hours: one story, a fixed six pages, text pitched at
the child's age, with an illustration per page. Every story stands alone.

A hard constraint surfaced while planning the images. The image API exposes **no seed**:
`OpenAI.Images.ImageGenerationOptions` (OpenAI SDK 2.1.0 and 2.11.0) carries only `Model`, `N`,
`Prompt`, `ResponseFormat` and `EndUserId`, and the Azure REST contract for `POST /images/generations`
lists no `seed` for `dall-e-3` or `gpt-image-1`. The per-character locked seed that ADR-0008 relies
on is therefore **not implementable with this SDK**, independently of any scope decision.

## Decision

We will make every story self-contained: no embeddings, no sequels or prequels, no saved or reused
characters. The `Character` and `CharacterPage` entities are removed along with `Story.Embedding`,
`Character.Embedding`, `Story.BasedOnStoryId` (and its self-FK), `Story.Summary` and `Story.Palette`.
A story carries a `TargetAge`, a fixed six pages, and one `ArtStyle`.

Character visual consistency is bought entirely through the prompt: the brief-authoring pass must
restate each character's full physical appearance in **every** page's `illustrationPrompt`, and the
renderer appends a byte-identical, code-composed style suffix to all six prompts.

## Consequences

### Positive
- Concrete entities drop from seven to five, brief records from four to two, EF configurations from
  seven to five, migrations from two to one. Net −1621/+358 lines.
- The reuse fields and the method whose only job was to blank them both disappear.
- `GenerationBriefValidator` enforces an exact page count rather than a cap, so the validator and
  the agent prompt finally agree — they previously disagreed (prompt said exactly 6, validator
  allowed up to 10).
- `TargetAge` becomes a real column. It was previously gathered in conversation, interpolated into a
  prompt, and then discarded.

### Negative / trade-offs accepted
- Character consistency across pages becomes **approximate**. Repeating a description is weaker than
  a reference image would be — but a reference image needs a seed to be worth conditioning on, and
  there is no seed.
- Reintroducing reuse or discovery later means a new migration and a wider brief. Cheap now (no
  production database), less cheap after launch.
- `docs/diagrams/er-diagram.md` and the layer READMEs describe a model that no longer exists until
  they are regenerated.

### Things this decision obliges us to build or watch
- Watch the illustrations across a real six-page story: if characters drift badly, the fallback is a
  reference-sheet image passed to `images/edits` with `input_fidelity: high`, which is the only real
  conditioning lever the API offers.
- Keep the "restate every appearance" rule in `StoryAgentPrompt.BriefAuthoringInstructions`. It
  reads like a style note and is in fact load-bearing.

## Alternatives considered

### Keep the model and simply not use the features
Rejected: the fields were not inert. They propagated through the brief, the DTOs, the EF
configuration, the endpoint projection and five test files, and required `ForceNewCharacters` to
neutralise them on every single brief.

### Keep `Character` as a per-story child entity
Viable, and it would have preserved a place to hang a reference image later. Rejected because
without reuse and without a seed a character row stores nothing the page prompt does not already
carry, at the cost of two tables and a join.

## Supersedes

- [ADR-0008](0008-per-character-reference-images.md) — per-character reference images and locked
  seeds. The seed does not exist in the API.
- [ADR-0009](0009-postgresql-pgvector-continuity.md) — pgvector for continuity and reuse. No reuse,
  no discovery, no embeddings.
