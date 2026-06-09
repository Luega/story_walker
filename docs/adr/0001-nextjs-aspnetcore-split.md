# ADR 0001 — Next.js front-end + ASP.NET Core API split

- **Status:** Accepted
- **Date:** 2026-06-09
- **Deciders:** Story Walker team

## Context

Story Walker needs a rich, interactive front-end (auth, dashboard, conversational
story-creation, live progress) and a back-end that orchestrates AI generation, owns the
data model, and integrates with Azure services. We must decide whether to build a single
full-stack application or to separate the client and the API into independent deployables.

## Decision

We will build the front-end as a **Next.js 16** application and the back-end as a separate
**ASP.NET Core** Web API. They communicate over HTTPS (REST + JSON) and over **Azure
SignalR** for realtime updates. They are deployed and scaled independently.

## Consequences

### Positive
- Clear separation of concerns: UI/UX in Next.js, domain logic and Azure integration in .NET.
- Each side uses the ecosystem it is strongest in (React/Next for UX, .NET for the AI
  pipeline, EF Core, and Azure SDKs).
- Independent scaling and deployment cadence.
- The API is reusable by other clients (mobile, etc.) later.

### Negative / trade-offs accepted
- Two codebases, two deploy pipelines, and a network boundary to secure (CORS, JWT validation).
- Shared contracts (DTOs) must be kept in sync across languages.

### Things this decision obliges us to build or watch
- A typed API client on the Next.js side and a stable, versioned API surface on the .NET side.
- CORS and JWT validation configuration on the API (see [ADR-0002](0002-clerk-for-authentication.md)).

## Alternatives considered

### Next.js full-stack (API routes / server actions only)
Simplest deployment and a single language. Rejected: the generation pipeline is heavily
Azure- and .NET-centric (Functions, Service Bus, EF Core, Azure SDKs), and we want the
domain/back-end to be independent of the rendering framework.

### ASP.NET Core serving server-rendered Razor/Blazor UI
One stack, one deploy. Rejected: the team wants the React/Next ecosystem and component
richness for the dashboard and the conversational creation flow.
