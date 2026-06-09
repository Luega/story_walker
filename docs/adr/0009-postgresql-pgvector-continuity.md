# ADR 0009 — PostgreSQL + pgvector for sequel/prequel continuity

- **Status:** Accepted
- **Date:** 2026-06-09
- **Deciders:** Story Walker team

## Context

Users can create a **sequel** or **prequel** of an existing story. The new story must
connect narratively to the original (continue from its ending, or build up to its
beginning) and stay consistent with its characters and lore — even across a long series
where prior content no longer fits in a single prompt. We also want the agent to find a
relevant existing story when the user describes one in conversation
("another adventure with Luna in the snow").

## Decision

We will use a **single PostgreSQL database with the `pgvector` extension** for both
relational data and semantic retrieval — no separate vector database.

- A **lineage** is modelled with `Story.parent_story_id` (self-referencing FK) and
  `Story.relation` (`original` | `sequel` | `prequel`); stories of one lineage share a
  `Series` that owns the bible and characters.
- Story content is **chunked and embedded** into a `StoryChunk` table holding a
  `vector` column (per-page text, a story summary, and character/lore notes), using a
  Foundry embedding model.
- Generating a sequel/prequel **inherits** the parent's bible, characters, and seeds
  (a direct FK lookup — no vectors needed for visual continuity), and uses **vector
  similarity search (RAG)** to retrieve the most relevant prior passages to ground the new
  text, plus the parent's ending (sequel) or opening (prequel).
- On completion, the new story's content is embedded so it becomes searchable for *its*
  future sequels.

## Consequences

### Positive
- One database for relational + vector data → transactional consistency, one backup/ops
  story, no cross-store sync.
- RAG keeps prompts bounded even for long series (retrieve relevant lore, not everything).
- Enables semantic discovery of stories/characters in the agent flow.

### Negative / trade-offs accepted
- `pgvector` at very large scale is less specialised than dedicated vector DBs; acceptable
  for this product's volume, with ANN indexing (HNSW/IVFFlat) when needed.
- An embedding step is added to the end of generation; embeddings must be re-generated if
  content changes.

### Things this decision obliges us to build or watch
- A chunking strategy and embedding model choice; an ANN index on the `vector` column.
- Retrieval scoped to the correct lineage/series (and the owning user) for relevance and privacy.
- Re-embedding on edits.

## Alternatives considered

### Separate dedicated vector DB (e.g., a managed vector service)
Best-in-class ANN at huge scale. Rejected: adds a second datastore to sync and operate for
no benefit at our scale; loses transactional consistency with the relational data.

### No retrieval — stuff the whole parent story into the prompt
Simplest. Works for a single short parent. Rejected: does not scale to multi-book series and
wastes context/cost.
