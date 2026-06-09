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

    System(storyWalker, "Story Walker", "Generates consistent illustrated kids' stories and their sequels/prequels")

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
        Container(funcs, "Generation Worker", "Azure Functions (queue-triggered)", "Choreographed pipeline: bible → text → reference images → per-page images")
        ContainerQueue(sb, "Service Bus", "Azure Service Bus", "One queue per pipeline stage; durable, at-least-once")
        Container(signalr, "Realtime Hub", "Azure SignalR Service", "Pushes page-ready / story-complete events to the browser")
        ContainerDb(pg, "Database", "PostgreSQL + pgvector", "Users, series, stories, pages, characters, chunks/embeddings")
        ContainerDb(blob, "Image Store", "Azure Blob Storage", "Page & character reference images")
    }

    Rel(parent, web, "Uses", "HTTPS")
    Rel(web, clerk, "Sign-in / session", "HTTPS/OIDC")
    Rel(web, api, "REST (JWT bearer)", "HTTPS/JSON")
    Rel(web, signalr, "Subscribes to story updates", "WebSocket")
    Rel(web, blob, "Loads images directly", "HTTPS (SAS/CDN)")

    Rel(api, clerk, "Validates JWT (JWKS)", "HTTPS")
    Rel(api, pg, "Reads/writes domain data", "EF Core / TCP")
    Rel(api, foundry, "Runs info-gathering agent chat", "HTTPS")
    Rel(api, sb, "Enqueues first generation message", "AMQP")

    Rel(funcs, sb, "Triggered by; enqueues next stage", "AMQP")
    Rel(funcs, foundry, "Generates bible, text, images, embeddings", "HTTPS")
    Rel(funcs, pg, "Reads brief; writes pages, characters, chunks; atomic completion counter", "TCP")
    Rel(funcs, blob, "Uploads generated images", "HTTPS")
    Rel(funcs, signalr, "Pushes progress events", "HTTPS")

    UpdateLayoutConfig($c4ShapeInRow="3", $c4BoundaryInRow="1")
```

## Generation flow (asynchronous choreography)

Concrete sequence for creating a story, including the parallel image fan-out and the
exactly-once completion. See [ADR-0004](../adr/0004-plain-azure-functions-choreography.md)
and [ADR-0005](../adr/0005-completion-counter-idempotency-deadletter.md).

```mermaid
sequenceDiagram
    autonumber
    actor U as Parent (Browser)
    participant API as ASP.NET Core API
    participant SB as Service Bus
    participant F as Azure Functions
    participant AI as Microsoft Foundry
    participant DB as PostgreSQL
    participant BL as Blob Storage
    participant SR as SignalR

    U->>API: Agent brief complete (create story)
    API->>DB: Insert Story (status=Generating, page_count=N)
    API->>SB: Enqueue "bible" {storyId}
    API-->>U: 202 Accepted
    U->>SR: Subscribe to story {storyId}

    SB->>F: trigger Fn_GenerateBible
    F->>AI: Generate bible (chars, style, outline)
    F->>DB: Save bible / series
    F->>SB: Enqueue "text" {storyId}

    SB->>F: trigger Fn_GenerateText
    F->>AI: Generate full story (single pass)
    F->>DB: Save N Page rows (text)
    F->>SB: Enqueue "refimage" {storyId}

    SB->>F: trigger Fn_GenerateRefImage
    F->>AI: Generate reference image(s) for new characters
    F->>BL: Upload reference image(s)
    F->>DB: Save reference_image_url + seed
    F->>SB: Enqueue N x "pageimage" {storyId, pageNo}

    par Parallel per-page (fan-out)
        SB->>F: trigger Fn_GeneratePageImage (pageNo)
        F->>DB: Already complete? (idempotency guard)
        F->>AI: Generate page image (bible + ref + seed + text)
        F->>BL: Upload page image
        F->>DB: Page=Complete; UPDATE completed_pages+1 RETURNING
        F->>SR: page-ready {pageNo, imageUrl}
        SR-->>U: page-ready
    end

    Note over F,DB: The instance whose RETURNING == N completes the story
    F->>DB: Story=Complete
    F->>SR: story-complete {storyId}
    SR-->>U: story-complete
```
