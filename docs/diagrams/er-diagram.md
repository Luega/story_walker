# Entity-Relationship Diagram

Minimal logical data model for Story Walker (PostgreSQL + `pgvector`), in Mermaid. A story may
**reuse** characters and art style from another story; characters are user-owned and shared
across stories. `pgvector` embeddings power semantic discovery of reusable stories/characters.

> Rationale lives in the ADRs — notably
> [0008](../adr/0008-per-character-reference-images.md) (characters & reference images),
> [0009](../adr/0009-postgresql-pgvector-continuity.md) (reuse & embeddings), and
> [0013](../adr/0013-agent-authored-generation-brief.md) (brief-sourced text & prompt fields).

```mermaid
erDiagram
    USER      ||--o{ STORY : owns
    USER      ||--o{ CHARACTER : owns
    STORY     ||--o{ STORY : "reuses (based_on_story_id)"
    STORY     ||--o{ PAGE : contains
    PAGE      ||--o{ CHARACTER_PAGE : features
    CHARACTER ||--o{ CHARACTER_PAGE : "appears on"

    USER {
        uuid id PK
        string clerk_user_id UK "identity from Clerk (ADR-0002)"
        string email
        string tier "subscription plan; Free is the only one so far"
    }

    STORY {
        uuid id PK
        uuid user_id FK
        uuid based_on_story_id FK "nullable; story whose characters + art style this one reuses"
        string title
        text summary "from the brief"
        string status "Draft | Generating | Complete | Failed"
        string art_style "from the brief; copied from the source story when reusing"
        string palette "from the brief"
        vector embedding "pgvector; story summary — for discovery (ADR-0009)"
        timestamp created_at
    }

    CHARACTER {
        uuid id PK
        uuid user_id FK "user-owned; reusable across the user's stories"
        string name
        text description "from the brief (ADR-0013)"
        text illustration_prompt "from the brief; generates the reference image (ADR-0013)"
        string reference_image_url "Blob URL; deterministic identity (ADR-0008/0010)"
        bigint seed "locked per character"
        vector embedding "pgvector; description — for discovery (ADR-0009)"
    }

    PAGE {
        uuid id PK
        uuid story_id FK
        int page_no
        text content "page text, from the brief (ADR-0013)"
        text illustration_prompt "from the brief; generates the page image (ADR-0013)"
        string image_url "Blob URL; null until image done"
        string status "Pending | Complete | Failed"
    }

    CHARACTER_PAGE {
        uuid character_id FK
        uuid page_id FK
    }
```

## Notes

- **Ownership & reuse.** A `USER` owns `STORY` and `CHARACTER`. Characters are **user-owned and
  reusable** across the user's stories — a story uses a character by linking it to that story's
  pages (`CHARACTER_PAGE`). A new story can **reuse** another story's characters and art style;
  `STORY.based_on_story_id` records the source (nullable). There is no "series" and no
  sequel/prequel concept — just reuse.
- **Per-character consistency.** `reference_image_url` and `seed` on `CHARACTER` give a
  character its **deterministic identity**; reuse is by linking the existing row, never by a
  similarity match (ADR-0008). `CHARACTER_PAGE` records which characters appear on each page so
  the image step conditions on exactly the right reference images.
- **Brief-sourced fields.** The agent-authored generation brief (ADR-0013) populates
  `STORY.art_style`/`palette`, each `CHARACTER.description`/`illustration_prompt`, and each
  `PAGE.content`/`illustration_prompt`. The brief is a transport format only — it is **not**
  persisted as a column; the normalized rows are the source of truth.
- **Generation status.** `STORY.status` and `PAGE.status` reflect progress. Fan-in is handled
  by the Durable Functions orchestrator (`Task.WhenAll`), **not** DB counters (ADR-0004/0005);
  these columns are the dashboard's source of truth if a SignalR event is missed (ADR-0006).
- **Semantic discovery.** The `embedding` columns on `STORY` (a story-summary vector) and
  `CHARACTER` (a description vector) hold `pgvector` values with an HNSW index. They power
  _discovery_ — finding relevant prior stories and reusable characters — while reuse/identity
  itself is always the deterministic FK link, not the similarity score (ADR-0009). One vector
  per row (no chunk-level retrieval); a separate chunk table would be reintroduced only if
  long/chaptered stories later need passage-level search. Query the vector column only when
  doing similarity search. Scoped by owner.
- **Images** are bytes in Blob Storage; the DB stores only URLs (`PAGE.image_url`,
  `CHARACTER.reference_image_url`) — ADR-0010.
