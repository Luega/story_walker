# Entity-Relationship Diagram

Logical data model for Story Walker (PostgreSQL + `pgvector`), in Mermaid. It captures the
story **lineage** (sequels/prequels), the **per-character** consistency model, the
**asynchronous generation** status fields, the **agent conversation**, and the **embeddings**
used for retrieval.

> Rationale lives in the ADRs — notably
> [0005](../adr/0005-completion-counter-idempotency-deadletter.md) (status/counter fields),
> [0008](../adr/0008-per-character-reference-images.md) (characters & reference images), and
> [0009](../adr/0009-postgresql-pgvector-continuity.md) (lineage & embeddings).

```mermaid
erDiagram
    USER ||--o{ SERIES : owns
    USER ||--o{ STORY : owns
    USER ||--o{ CONVERSATION : has

    SERIES ||--o{ STORY : groups
    SERIES ||--o{ CHARACTER : "defines (bible)"

    STORY ||--o{ STORY : "parent of (sequel/prequel)"
    STORY ||--o{ PAGE : contains
    STORY ||--o{ STORY_CHUNK : "embedded as"
    STORY ||--o| CONVERSATION : "created via"

    PAGE ||--o{ PAGE_CHARACTER : features
    CHARACTER ||--o{ PAGE_CHARACTER : "appears in"

    CONVERSATION ||--o{ MESSAGE : contains

    USER {
        uuid id PK
        string clerk_user_id UK "identity from Clerk (ADR-0002)"
        string email
        timestamptz created_at
    }

    SERIES {
        uuid id PK
        uuid user_id FK
        string title
        string art_style "shared bible: style"
        string palette "shared bible: colour palette"
        text summary
        timestamptz created_at
    }

    STORY {
        uuid id PK
        uuid series_id FK
        uuid user_id FK
        uuid parent_story_id FK "nullable; self-ref lineage"
        string relation "original | sequel | prequel"
        string title
        string status "Draft | Gathering | Generating | Complete | Failed"
        int page_count "N"
        int completed_pages "atomic fan-in counter (ADR-0005)"
        text brief "confirmed agent brief"
        timestamptz created_at
        timestamptz completed_at
    }

    CHARACTER {
        uuid id PK
        uuid series_id FK "shared across the lineage"
        string name
        text description "bible entry"
        string reference_image_url "Blob URL (ADR-0008/0010)"
        bigint seed "locked per character"
        uuid created_in_story_id FK "story that introduced it"
        timestamptz created_at
    }

    PAGE {
        uuid id PK
        uuid story_id FK
        int page_no
        text content "generated text for this page"
        string image_url "Blob URL; null until image done"
        string status "Pending | TextDone | Complete | Failed"
        timestamptz created_at
    }

    PAGE_CHARACTER {
        uuid page_id FK
        uuid character_id FK
        string role "which characters appear -> which refs to condition on"
    }

    STORY_CHUNK {
        uuid id PK
        uuid story_id FK
        string chunk_type "page | summary | lore | character"
        int page_no "nullable"
        text content
        vector embedding "pgvector; ANN index (ADR-0009)"
        timestamptz created_at
    }

    CONVERSATION {
        uuid id PK
        uuid user_id FK
        uuid story_id FK "nullable until the story is created"
        string status "Active | Completed | Abandoned"
        timestamptz created_at
    }

    MESSAGE {
        uuid id PK
        uuid conversation_id FK
        string role "user | assistant"
        text content
        timestamptz created_at
    }
```

## Notes

- **Lineage.** `STORY.parent_story_id` is a self-referencing FK; `relation` records whether a
  story is an `original`, `sequel`, or `prequel`. All stories of one lineage share a `SERIES`,
  which owns the bible-level style and the `CHARACTER` set — so sequels inherit characters and
  art style automatically and only *append* new characters.
- **Per-character consistency.** Reference images and seeds live on `CHARACTER`, not on
  `STORY`. `PAGE_CHARACTER` records which characters appear on each page so the image step
  conditions on exactly the right reference images (ADR-0008).
- **Async generation.** `STORY.status` + `STORY.completed_pages` and `PAGE.status` drive the
  choreographed pipeline and the atomic fan-in (ADR-0004 / ADR-0005). They are also the
  source of truth the dashboard reconciles against if a SignalR event is missed (ADR-0006).
- **Retrieval.** `STORY_CHUNK.embedding` is a `pgvector` column (with an HNSW/IVFFlat index)
  holding per-page, summary, lore, and character embeddings used by RAG when generating a
  sequel/prequel (ADR-0009). Retrieval is always scoped by `user_id` and lineage.
- **Images** are bytes in Blob Storage; the DB stores only URLs (`PAGE.image_url`,
  `CHARACTER.reference_image_url`) — ADR-0010.
