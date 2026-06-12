# ADR 0013 — Agent-authored generation brief (JSON contract)

- **Status:** Accepted
- **Date:** 2026-06-12
- **Deciders:** Luca Martinelli

## Context

The conversational agent ([ADR-0011](0011-microsoft-foundry-agent-and-generation.md)) gathers
the details for a new story. The question is *how much* it produces. Generating the bible and
the story text as separate worker activities (the earlier design) splits authoring across two
systems: the agent holds the full conversation context, but a later activity re-derives the
bible and text from a terse brief — risking drift and losing context.

Since the agent already has the complete context and a single coherent pass gives the best
narrative + visual consistency ([ADR-0007](0007-single-pass-text-generation.md),
[ADR-0008](0008-per-character-reference-images.md)), it is the right place to author the
*entire* plan — not just collect requirements.

## Decision

When the agent has enough information, it emits a **complete, schema-validated generation
brief as JSON** (using Foundry structured outputs). The API validates the JSON against the
schema and enqueues it to Azure Service Bus ([ADR-0003](0003-async-generation-via-service-bus.md));
the Durable worker ([ADR-0004](0004-durable-functions-for-generation.md)) only **renders
images** from it. The brief is the producer/consumer contract and lives in
`StoryWalker.Contracts`.

```jsonc
{
  "story":    { "title": "…", "basedOnStoryId": null },  // set when reusing another story
  "artStyle": "…",                          // applies to this story
  "palette":  "…",                          // applies to this story
  "characters": [
    {
      "name": "Luna",
      "description": "round-faced girl, red curly hair, yellow raincoat",
      "illustrationPrompt": "…",            // used to generate the reference image
      "existingCharacterId": null           // set for a reused character → retrieve, don't generate
    }
  ],
  "pages": [
    {
      "pageNo": 1,
      "text": "…",                          // the page's story text
      "illustrationPrompt": "…",            // used to generate the page image
      "characters": ["Luna"]                // who appears → which reference images to condition on
    }
  ]
}
```

- `basedOnStoryId` records the story this one **reuses** characters and art style from
  (null for a fresh story).
- `existingCharacterId` lets a reused character reference an existing one, so the worker's
  `ResolveRefImage` step retrieves the existing reference image instead of generating a new one.
- `pages[].characters` populates `CHARACTER_PAGE`, telling `GeneratePageImage` which reference
  images to condition on.

## Consequences

### Positive
- **Single coherent authoring pass**: characters, palette, art style, text, and all prompts
  are produced together with full conversation context — strongest consistency.
- **Simpler worker**: the orchestration drops `GenerateBible` and `GenerateText` and becomes
  image-only (`PersistBrief → ResolveRefImage → GeneratePageImage → CompleteStory`).
- **One explicit contract**: the JSON schema is the hand-off between agent, API, and worker.

### Negative / trade-offs accepted
- The agent's JSON must be **validated** (and repaired/regenerated on malformed output) before
  enqueuing — a guard we must build.
- The brief is larger than a bare `storyId` message; see the message-size note in
  [ADR-0003](0003-async-generation-via-service-bus.md) (well within limits; claim-check
  fallback available).
- Prompt-quality for images now depends on the agent's prompt authoring, not a dedicated
  image-prompt step.

### Things this decision obliges us to build or watch
- A JSON schema + validation in the API; a regenerate/repair path on validation failure.
- The schema DTO in `StoryWalker.Contracts`, kept in sync across producer and consumer.
- When reusing a story, the agent must resolve reused characters to `existingCharacterId` (via
  the pgvector retrieval in [ADR-0009](0009-postgresql-pgvector-continuity.md)).

## Alternatives considered

### Bible + text generated as worker activities (the prior design)
The agent gathers a terse brief; `GenerateBible` and `GenerateText` activities author content
inside the orchestration. Rejected: splits authoring across two systems, re-derives context
the agent already had, and adds two model-calling activities to the worker for no consistency
benefit.

### Agent emits only text; the worker derives prompts
Smaller agent output. Rejected: a separate prompt-derivation step loses the agent's holistic
view of characters/scene and risks prompt/text mismatch; authoring everything together is more
coherent.
