# ADR 0011 — Microsoft Foundry for the agent and generation

- **Status:** Accepted
- **Date:** 2026-06-09
- **Deciders:** Story Walker team

## Context

Two distinct AI capabilities are needed: (1) a **conversational agent** that interviews the
user to gather the details for a new story and signals when it has enough to proceed, and
(2) **content generation** — story text, a bible, character reference images, per-page
images, and text embeddings for retrieval. We want these under one AI platform on Azure to
keep integration, governance, and operations cohesive.

## Decision

We will use **Microsoft Foundry** for both the conversational agent and all generation
(text, images, and embeddings). The agent runs the information-gathering chat and, when the
brief is complete, the API persists the brief and kicks off the asynchronous pipeline
([ADR-0003](0003-async-generation-via-service-bus.md)). The generation Functions call
Foundry for the bible, the single-pass story text
([ADR-0007](0007-single-pass-text-generation.md)), reference and page images
([ADR-0008](0008-per-character-reference-images.md)), and embeddings
([ADR-0009](0009-postgresql-pgvector-continuity.md)).

## Consequences

### Positive
- One AI platform for chat, text, image, and embeddings → unified SDK, auth, quota, and
  governance on Azure.
- Clear seam between conversation (gather the brief) and generation (produce the story).
- Model choices per task (chat vs. text vs. image vs. embedding) within one platform.

### Negative / trade-offs accepted
- Vendor/platform dependency; model availability and capabilities (e.g., image reference
  conditioning) constrain design — hence the fallback noted in ADR-0008.
- AI inference is the dominant cost driver; quotas and rate limits must be managed.

### Things this decision obliges us to build or watch
- A defined hand-off contract between the agent's "brief complete" signal and the API.
- Model/version selection per task and prompt-template management.
- Quota, rate-limit, and retry handling on Foundry calls within the Functions.

## Alternatives considered

### Mix of providers (e.g., one vendor for chat, another for images)
Best-of-breed per task. Rejected: multiplies integration, auth, billing, and governance
surfaces; the team wants one Azure-native platform.

### Self-hosted open models
Full control, no per-call vendor cost. Rejected: heavy MLOps burden and infrastructure cost
that a small team should not own at this stage.
