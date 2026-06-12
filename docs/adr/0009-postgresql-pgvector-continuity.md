# ADR 0009 — PostgreSQL + pgvector for story continuity and reuse

- **Status:** Accepted
- **Date:** 2026-06-09
- **Deciders:** Luca Martinelli

## Context

A user can create a new story that **reuses** the characters and art style of an existing one,
and the new story should stay consistent with that source — even when prior content no longer
fits in a single prompt. We also want the agent to **find** a relevant existing story or
character when the user describes one ("another adventure with Luna in the snow"), without the
user having to point at an exact row.

## Decision

We will use a **single PostgreSQL database with the `pgvector` extension** for both relational
data and semantic retrieval — no separate vector database.

- **Identity and reuse are deterministic.** Characters are user-owned `CHARACTER` rows carrying
  their `reference_image_url` and `seed`; a story reuses a character by linking the existing row
  (`CHARACTER_PAGE`), and records the story it was built on via `STORY.based_on_story_id`. Reuse
  is never decided by a similarity score.
- **Discovery is semantic, with one vector per row.** Embeddings are stored **inline**: a
  story-summary `embedding` column on `STORY` and a description `embedding` column on
  `CHARACTER` (Foundry embedding model; HNSW index per column). No separate embeddings table —
  one vector per story/character is enough for *discovery* at this scale.
- **Authoring a reusing story.** When the agent authors the brief
  ([ADR-0013](0013-agent-authored-generation-brief.md)), it uses **vector similarity search**
  over the `STORY` and `CHARACTER` embedding columns to find the most relevant prior story and
  reusable characters, then references returning characters by `existingCharacterId` and
  inherits the source story's art style. Once the source story is identified, its full (short)
  text grounds the new text the agent writes.
- **Embedding step.** During generation, an `EmbedStory` activity writes the story-summary and
  character-description embeddings (concurrent with image rendering — see
  [ADR-0004](0004-durable-functions-for-generation.md)), so the story becomes discoverable later.

## Consequences

### Positive
- One database for relational + vector data → transactional consistency, one backup/ops story,
  no cross-store sync.
- RAG keeps prompts bounded even when reusing across many prior stories (retrieve relevant
  content, not everything).
- Enables semantic discovery of reusable stories/characters in the agent flow, while keeping
  identity/reuse on hard FK links.

### Negative / trade-offs accepted
- `pgvector` at very large scale is less specialised than dedicated vector DBs; acceptable for
  this product's volume, with ANN indexing (HNSW/IVFFlat) when needed.
- **One vector per row** — no passage-level retrieval. Fine for short stories + discovery; if
  long/chaptered stories later need fine-grained search, reintroduce a separate chunk table.
- An embedding step is added to generation; embeddings must be re-generated if content changes.

### Things this decision obliges us to build or watch
- An embedding model choice and an HNSW index on each `embedding` column.
- Read the vector column only when doing similarity search (keep normal row reads lean).
- Retrieval scoped to the owning user for relevance and privacy.
- Re-embedding on edits.

## Alternatives considered

### Separate dedicated vector DB (e.g., a managed vector service)
Best-in-class ANN at huge scale. Rejected: adds a second datastore to sync and operate for no
benefit at our scale; loses transactional consistency with the relational data.

### Using similarity to decide character reuse
Let semantic search pick "the same character." Rejected: identity must be exact — a fuzzy match
could reuse the wrong character or miss one, breaking the visual consistency ADR-0008 enforces.
Similarity is for *discovery*; the actual reuse is always the deterministic FK link.
