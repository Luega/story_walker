# ADR 0011 — Microsoft Foundry for the agent and generation

- **Status:** Accepted
- **Date:** 2026-06-09
- **Deciders:** Luca Martinelli

## Context

Two distinct AI capabilities are needed: (1) a **conversational agent** that interviews the
user and, when it has enough, **authors the full generation brief** (bible, single-pass story
text, and every illustration prompt) as JSON, and (2) **image generation and embeddings** —
character reference images, per-page images, and text embeddings for retrieval. We want these
under one AI platform on Azure to keep integration, governance, and operations cohesive.

## Decision

We will use **Microsoft Foundry** for the conversational agent, image generation, and
embeddings. The agent runs the information-gathering chat and, when complete, emits the
**structured generation brief** as schema-validated JSON
([ADR-0013](0013-agent-authored-generation-brief.md)). This is where the bible and the
single-pass story text ([ADR-0007](0007-single-pass-text-generation.md)) are authored. The
API validates that JSON and kicks off the asynchronous pipeline
([ADR-0003](0003-async-generation-via-service-bus.md)). The Durable worker then calls Foundry
only for **reference and page images** ([ADR-0008](0008-per-character-reference-images.md))
and **embeddings** ([ADR-0009](0009-postgresql-pgvector-continuity.md)) — not for bible or
text, which already arrive in the brief.

## Consequences

### Positive

- One AI platform for chat, text, image, and embeddings → unified SDK, auth, quota, and
  governance on Azure.
- Clear seam between authoring (the agent produces the JSON brief) and rendering (the worker
  generates images from it).
- Model choices per task (chat vs. text vs. image vs. embedding) within one platform.

### Negative / trade-offs accepted

- Vendor/platform dependency; model availability and capabilities (e.g., image reference
  conditioning) constrain design — hence the fallback noted in ADR-0008.
- AI inference is the dominant cost driver; quotas and rate limits must be managed.

### Things this decision obliges us to build or watch

- The hand-off contract is the generation-brief JSON schema ([ADR-0013](0013-agent-authored-generation-brief.md)),
  validated by the API before enqueuing.
- Model/version selection per task and prompt-template management.
- Quota, rate-limit, and retry handling on Foundry calls (agent in the API; images/embeddings in the Functions).

## Alternatives considered

### Mix of providers (e.g., one vendor for chat, another for images)

Best-of-breed per task. Rejected: multiplies integration, auth, billing, and governance
surfaces; the team wants one Azure-native platform.

### Self-hosted open models

Full control, no per-call vendor cost. Rejected: heavy MLOps burden and infrastructure cost
that a small team should not own at this stage.
