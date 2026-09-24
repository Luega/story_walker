# apps/ui — Next.js front-end

The Next.js 16 front-end: authentication (Clerk), the private dashboard, the agent chat for
creating stories, and the live story viewer (Azure SignalR).

> Current state: a minimal UI with no authentication and polling instead of SignalR.

## Pages

| Route           | What it does                                                                 |
| --------------- | ---------------------------------------------------------------------------- |
| `/`             | Public landing page: what Story Walker does, and a "Get started" link to sign-up. |
| `/dashboard`    | Story list (`GET /stories`) and the "New story" modal that chats with the agent (`POST /stories/conversations`). When the agent authors the brief (202) it navigates to the new story. |
| `/stories/[id]` | Story viewer (`GET /stories/{id}`): two facing pages at a time, image above and text below. Polls while the story is generating. |

## Running locally

1. Start the API (see [`apps/api/README.md`](../api/README.md)); by default it listens on
   `http://localhost:5080`.
2. In this folder:

   ```bash
   npm install
   npm run dev          # http://localhost:3000
   ```

The browser only calls `/api/*`, which `next.config.ts` rewrites to `API_URL` — so the API needs
no CORS policy. Override the target with `API_URL` (see `.env.example`), e.g. in `.env.local`.

## Telemetry

Off unless configured ([ADR-0023](../../docs/adr/0023-opentelemetry-to-application-insights.md)).
Put the Application Insights connection string in both variables in `.env.local`:

- `APPLICATIONINSIGHTS_CONNECTION_STRING`: the Next server (`src/instrumentation.ts`). It sends
  requests, render spans, and the proxy's call to the API, which carries `traceparent`.
- `NEXT_PUBLIC_APPLICATIONINSIGHTS_CONNECTION_STRING`: the browser
  (`src/instrumentation-client.ts`). It sends page views, unhandled errors, and same-origin
  fetches. It is inlined at **build** time, so a production image needs it set when `next build`
  runs. Setting it also turns on the analytics-cookie banner. The `ai_user` and `ai_session`
  cookies are written only after a visitor accepts, and the choice is kept in `localStorage`.
