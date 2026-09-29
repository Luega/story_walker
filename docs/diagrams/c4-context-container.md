# C4 — System Context & Containers

C4 model (Levels 1 & 2) for Story Walker, in Mermaid. Level 1 shows the system in its
environment; Level 2 shows the deployable containers and how they collaborate. Two sequence
diagrams make the asynchronous generation concrete: the normal run, and what happens to a story
whose message is lost.

> See the ADRs in [`../adr`](../adr) for the rationale behind each container and dependency.

## Level 1 — System Context

```mermaid
C4Context
    title Story Walker — System Context

    Person(parent, "Parent / Guardian", "Plans stories with the agent and reads them with their child")

    System(storyWalker, "Story Walker", "Turns a conversation into an illustrated six-page story for a child")

    System_Ext(clerk, "Clerk", "Authentication & identity (OIDC / JWT)")
    System_Ext(foundry, "Microsoft Foundry", "Chat deployment (agent, brief, prompt rewrites) + image deployment")

    Rel(parent, storyWalker, "Signs in, plans & reads stories", "HTTPS")
    Rel(storyWalker, clerk, "Authenticates users, validates JWTs", "HTTPS/OIDC")
    Rel(storyWalker, foundry, "Runs the agent, renders the illustrations", "HTTPS")

    UpdateLayoutConfig($c4ShapeInRow="2", $c4BoundaryInRow="1")
```

## Level 2 — Containers

Drawn as a `flowchart` in C4 notation (person, containers, external systems) rather than
`C4Container`, whose fixed layout piles the relationship labels on top of each other.

```mermaid
flowchart TB
    parent(["<b>Parent / Guardian</b><br/>[Person]"])

    subgraph sw["Story Walker"]
        web["<b>Web App</b><br/>[Next.js 16 / React]<br/>Dashboard, agent chat, story viewer,<br/>server-side token proxy"]
        api["<b>API</b><br/>[ASP.NET Core]<br/>Runs the agent, owns domain & data,<br/>weekly limit, enqueues generation"]
        sb[["<b>Service Bus</b><br/>[Azure Service Bus]<br/>story-generation + dead-letter queue"]]
        funcs["<b>Generation Worker</b><br/>[Azure Functions, isolated]<br/>GenerateStory<br/>SettleDeadLetteredGeneration<br/>FailStaleGenerations (timer)"]
        pg[("<b>Database</b><br/>[PostgreSQL]<br/>Users, conversations, stories,<br/>pages, finished-story ledger")]
        blob[("<b>Image Store</b><br/>[Azure Blob Storage]<br/>One image per page")]
    end

    clerk["<b>Clerk</b><br/>[External]<br/>Auth & identity"]
    foundry["<b>Microsoft Foundry</b><br/>[External]<br/>Chat + image deployments"]

    parent -->|HTTPS| web
    web -->|"sign-in (OIDC)"| clerk
    web -->|"REST + JWT, polls"| api
    web -->|page images| blob
    api -->|JWKS| clerk
    api -->|domain data| pg
    api -->|agent, brief| foundry
    api -->|brief, AMQP| sb
    sb -->|"brief, dead letters"| funcs
    funcs -->|"images, rewrites, advice"| foundry
    funcs -->|pages, statuses| pg
    funcs -->|uploads images| blob

    classDef person fill:#08427b,stroke:#052e56,color:#fff
    classDef container fill:#1168bd,stroke:#0b4884,color:#fff
    classDef external fill:#999,stroke:#6b6b6b,color:#fff
    class parent person
    class web,api,sb,funcs,pg,blob container
    class clerk,foundry external
    style sw fill:none,stroke:#666,stroke-dasharray:5 5
```

- **Web App → API.** Every call goes through a Next.js Route Handler that adds the Clerk JWT
  server-side ([ADR-0021](../adr/0021-clerk-integration.md)).
- **API → Foundry.** The chat deployment runs the agent and, on confirmation, authors the generation
  brief ([ADR-0013](../adr/0013-agent-authored-generation-brief.md)).
- **API → Service Bus.** One message per story on `story-generation`: the brief as the body, the
  story id as the message id ([ADR-0003](../adr/0003-async-generation-via-service-bus.md)).
- **Service Bus → Worker.** `GenerateStory` is triggered by the queue; `SettleDeadLetteredGeneration`
  by its dead-letter queue ([ADR-0032](../adr/0032-settle-and-remove-dead-lettered-messages.md)).
  `FailStaleGenerations` runs on a timer ([ADR-0031](../adr/0031-fail-stories-stuck-in-generating.md)).
- **Worker → Foundry.** The image deployment renders the pages; the chat deployment rewrites a
  refused illustration prompt ([ADR-0025](../adr/0025-rewrite-illustration-prompt-after-moderation.md))
  and writes the advice for a failed story
  ([ADR-0027](../adr/0027-failed-story-returns-to-its-conversation.md)).
- **Worker → Database.** Pages, page and story statuses, the finished-story ledger, and failure
  reports appended to the conversation.

Progress reaches the browser by **polling**, not yet by SignalR
([ADR-0006](../adr/0006-azure-signalr-for-realtime.md) is not built): the dashboard reloads the story
list every 5 seconds while any story is `Generating`.

## Generation flow

One Service Bus-triggered function, no orchestrator
([ADR-0020](../adr/0020-plain-service-bus-function-for-generation.md)). The database is the
checkpoint: a redelivered message skips the pages already written and the images already rendered
([ADR-0005](../adr/0005-idempotency-and-failure-handling.md)). Pages render **one at a time**, and
the first page that fails stops the run
([ADR-0026](../adr/0026-one-failed-page-fails-the-story.md)).

```mermaid
sequenceDiagram
    autonumber
    actor U as Parent (Browser)
    participant API as ASP.NET Core API
    participant SB as Service Bus
    participant W as GenerateStory
    participant AI as Microsoft Foundry
    participant DB as PostgreSQL
    participant BL as Blob Storage

    U->>API: Chat with the agent
    API->>AI: Agent turn — on confirmation, author the brief (title, age, style, 6 × text + prompt)
    API->>DB: Insert Story (status=Generating, conversation_id)
    API->>SB: Enqueue brief (message id = story id, TTL 1 h)
    API-->>U: Agent's reply, story appears on the dashboard

    SB->>W: Deliver brief (up to 10 deliveries)
    W->>DB: Insert 6 Page rows, Pending (skipped if already there)

    loop Each page, in order (skipped if it already has an image)
        W->>AI: Render illustration_prompt + art style
        alt Transient error (429, 5xx, network)
            W->>AI: Retry in-process: 3 attempts, 5 s doubling
        else Moderation refusal
            W->>AI: Rewrite this page's illustration prompt (chat), render once more
        end
        W->>BL: Upload image
        W->>DB: Page=Complete, image_url
        Note over W: A page that ends Failed stops the loop:<br/>the pages after it stay Pending
    end

    W->>DB: Story=Complete (every page Complete) + ledger row, else Failed
    opt Story Failed (ADR-0027)
        W->>AI: Agent's advice on the failure, conversation in view
        W->>DB: Append system note + advice, conversation back to Active
    end

    loop Every 5 s while a story is Generating
        U->>API: GET stories
    end
```

## Lost messages

An exception the worker does not catch abandons the message; after ten deliveries Service Bus
dead-letters it. A message not picked up within its one-hour TTL is dead-lettered too. Either way
the story is settled at once from the dead-letter queue, and the message is removed
([ADR-0032](../adr/0032-settle-and-remove-dead-lettered-messages.md)). A timer is the backstop for
anything that loses a message another way
([ADR-0031](../adr/0031-fail-stories-stuck-in-generating.md)).

```mermaid
sequenceDiagram
    autonumber
    participant SB as Service Bus
    participant DLQ as Dead-letter queue
    participant D as SettleDeadLetteredGeneration
    participant T as FailStaleGenerations (every 15 min)
    participant S as LostGenerationSettler
    participant DB as PostgreSQL

    SB->>DLQ: 10th failed delivery, or TTL expired
    DLQ->>D: Deliver dead letter
    D->>S: Settle story (message id)
    S->>DB: If still Generating: report a technical problem to the conversation, then Story=Failed
    D-->>DLQ: Complete — message removed (logs reason, never the body)

    T->>DB: Stories Generating for more than 2 h
    T->>S: Settle each (no-op if already settled)
```
