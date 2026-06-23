# C4 — System Context & Containers

C4 model (Levels 1 & 2) for Story Walker, in Mermaid. Level 1 shows the system in its
environment; Level 2 shows the deployable containers and how they collaborate. A generation
sequence diagram is included to make the asynchronous flow concrete.

> See the ADRs in [`../adr`](../adr) for the rationale behind each container and dependency.

## Level 1 — System Context

```mermaid
C4Context
    title Story Walker — System Context

    Person(parent, "Parent / Guardian", "Creates and reads stories for their child")

    System(storyWalker, "Story Walker", "Generates consistent illustrated kids' stories, reusing characters and art style across them")

    System_Ext(clerk, "Clerk", "Authentication & identity (OIDC / JWT)")
    System_Ext(foundry, "Microsoft Foundry", "Conversational agent + text, image & embedding generation")

    Rel(parent, storyWalker, "Signs in, creates & reads stories", "HTTPS")
    Rel(storyWalker, clerk, "Authenticates users, validates JWTs", "HTTPS/OIDC")
    Rel(storyWalker, foundry, "Runs the agent; generates text, images, embeddings", "HTTPS")

    UpdateLayoutConfig($c4ShapeInRow="2", $c4BoundaryInRow="1")
```

## Level 2 — Containers

```mermaid
C4Container
    title Story Walker — Containers

    Person(parent, "Parent / Guardian", "Creates and reads stories")

    System_Ext(clerk, "Clerk", "Auth & identity")
    System_Ext(foundry, "Microsoft Foundry", "Agent + text/image/embedding generation")

    System_Boundary(sw, "Story Walker") {
        Container(web, "Web App", "Next.js 16 / React", "Dashboard, agent chat, live story viewer")
        Container(api, "API", "ASP.NET Core", "Auth'd REST API, owns domain & data, enqueues generation")
        Container(funcs, "Generation Worker", "Azure Durable Functions", "Orchestration: reference images, page images, text embeddings")
        ContainerQueue(sb, "Service Bus", "Azure Service Bus", "Ingestion queue that starts the orchestration")
        Container(signalr, "Realtime Hub", "Azure SignalR Service", "Pushes page-ready / story-complete events to the browser")
        ContainerDb(pg, "Database", "PostgreSQL + pgvector", "Users, stories, pages, characters, embeddings")
        ContainerDb(blob, "Image Store", "Azure Blob Storage", "Page & character reference images")
    }

    Rel(parent, web, "Uses", "HTTPS")
    Rel(web, clerk, "Sign-in / session", "HTTPS/OIDC")
    Rel(web, api, "REST (JWT bearer)", "HTTPS/JSON")
    Rel(web, signalr, "Subscribes to story updates", "WebSocket")
    Rel(web, blob, "Loads images directly", "HTTPS (SAS/CDN)")

    Rel(api, clerk, "Validates JWT (JWKS)", "HTTPS")
    Rel(api, pg, "Reads/writes domain data", "EF Core / TCP")
    Rel(api, foundry, "Runs agent; authors the generation brief (JSON)", "HTTPS")
    Rel(api, sb, "Enqueues first generation message", "AMQP")

    Rel(funcs, sb, "Starter triggered by ingestion message", "AMQP")
    Rel(funcs, foundry, "Generates images and embeddings", "HTTPS")
    Rel(funcs, pg, "Reads brief; writes pages, characters, embeddings, domain status", "TCP")
    Rel(funcs, blob, "Uploads generated images", "HTTPS")
    Rel(funcs, signalr, "Pushes progress events", "HTTPS")

    UpdateLayoutConfig($c4ShapeInRow="3", $c4BoundaryInRow="1")
```

## Generation flow (Durable Functions orchestration)

Concrete sequence for creating a story: the orchestrator drives the ordered stages and uses
`Task.WhenAll` for the two parallel fan-out/fan-in stages (character reference images, then
per-page images). See [ADR-0004](../adr/0004-durable-functions-for-generation.md)
and [ADR-0005](../adr/0005-idempotency-and-failure-handling.md).

```mermaid
sequenceDiagram
    autonumber
    actor U as Parent (Browser)
    participant API as ASP.NET Core API
    participant SB as Service Bus
    participant O as Durable Orchestrator
    participant ACT as Activity Functions
    participant AI as Microsoft Foundry
    participant DB as PostgreSQL
    participant BL as Blob Storage
    participant SR as SignalR

    U->>API: Chat with agent (gather details)
    API->>AI: Run agent — emit generation brief (JSON: chars, palette, style, page text + prompts)
    API->>DB: Insert Story (status=Generating)
    API->>SB: Enqueue generation brief (validated JSON)
    API-->>U: 202 Accepted
    U->>SR: Subscribe to story {storyId}

    SB->>O: Starter triggers orchestration {brief}

    O->>ACT: CallActivity PersistBrief
    ACT->>DB: Save characters + N Page rows + character_page links (text + prompts)

    O->>ACT: CallActivity EmbedStory (concurrent — needs only text)
    ACT->>AI: Embed story summary + character descriptions
    ACT->>DB: Write STORY / CHARACTER embedding columns (pgvector)

    par Fan-out #1 — characters (Task.WhenAll)
        O->>ACT: CallActivity ResolveRefImage (characterId)
        ACT->>DB: Existing character? reuse (idempotent)
        ACT->>AI: Generate reference image from illustrationPrompt (new characters only)
        ACT->>BL: Upload reference image
        ACT->>DB: Save reference_image_url + seed
    end
    Note over O: Durable awaits all character activities (fan-in)

    par Fan-out #2 — pages (Task.WhenAll)
        O->>ACT: CallActivity GeneratePageImage (pageNo)
        ACT->>DB: Already done? reuse (idempotent)
        ACT->>AI: Generate page image from illustrationPrompt (+ refs + seed)
        ACT->>BL: Upload page image
        ACT->>DB: Page=Complete
        ACT->>SR: page-ready {pageNo, imageUrl}
        SR-->>U: page-ready
    end
    Note over O: Durable awaits all page activities AND the embedding (fan-in)

    O->>ACT: CallActivity CompleteStory
    ACT->>DB: Story=Complete
    ACT->>SR: story-complete {storyId}
    SR-->>U: story-complete
```
