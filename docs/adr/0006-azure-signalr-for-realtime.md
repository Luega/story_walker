# ADR 0006 — Azure SignalR for realtime progress

- **Status:** Accepted
- **Date:** 2026-06-09
- **Deciders:** Story Walker team

## Context

Generation is asynchronous ([ADR-0003](0003-async-generation-via-service-bus.md)), so the
user is told "your story is being generated" and then waits. We want the dashboard to update
**live** — each page appearing as it finishes, and a final "story complete" — without the
browser polling and without the API holding a long-lived request.

## Decision

We will use the **Azure SignalR Service**. The Next.js client subscribes to updates for a
specific `storyId`. The generation Functions push events as work completes:

- `page-ready { storyId, pageNo, imageUrl }` — from `Fn_GeneratePageImage`,
- `story-complete { storyId }` — from the instance that finishes the last page,
- `story-failed { storyId, reason }` — from the dead-letter handler.

SignalR connections are authenticated with the same Clerk JWT
([ADR-0002](0002-clerk-for-authentication.md)); a user only receives events for their own
stories (per-user/per-story groups).

## Consequences

### Positive
- Real-time UX with no polling and no blocked HTTP requests.
- Managed scaling of WebSocket connections (no connection ceiling on the API).
- Works cleanly with the fire-and-forget API and the choreographed Functions.

### Negative / trade-offs accepted
- Another managed service and SDK on both client and server/Functions.
- Must handle missed events (e.g., client offline): the dashboard also reads current state
  from the API on load, so SignalR is an accelerator, not the source of truth.

### Things this decision obliges us to build or watch
- Authn/authz on the hub so users only see their own story groups.
- A reconciliation read on dashboard load (fetch current story/page state) to cover dropped
  connections.

## Alternatives considered

### Client polling the API
Simplest. Rejected: wasteful, laggy, and scales poorly for a live multi-page experience.

### Self-hosted SignalR in the API
No extra service. Rejected: ties WebSocket connection scaling to the API instances and
complicates pushing from out-of-process Functions; the managed service decouples this.
