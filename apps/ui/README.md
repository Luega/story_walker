# apps/ui — Next.js front-end

The Next.js 16 front-end: authentication (Clerk), the private dashboard, the agent chat for
creating stories, and the story viewer.

Progress comes from **polling**, not SignalR: [ADR-0006](../../docs/adr/0006-azure-signalr-for-realtime.md)
is not built. While any story is `Generating`, the dashboard reloads the list every 5 seconds
(`POLL_MS` in `src/app/dashboard/page.tsx`); it stops once none is.

## Pages

Every route except `/`, `/privacy`, `/sign-in` and `/sign-up` requires a Clerk session
(`src/proxy.ts`).

| Route           | What it does                                                                 |
| --------------- | ---------------------------------------------------------------------------- |
| `/`             | Public landing page: what Story Walker does, and a "Get started" link to sign-up. |
| `/dashboard`    | Story list (`GET /stories`) and the "New story" modal that chats with the agent (`POST /stories/conversations`). When the agent authors the brief (202) the modal closes and the new story appears in the list. The "Conversations" button opens the history sidebar (`GET /stories/conversations`), with an *In progress* and a *Completed* tab, where a conversation can be reopened in the modal, renamed or deleted (ADR-0028). A completed one offers "New story based on this one", which starts a fresh chat from its story (ADR-0029). Under the heading, the stories left this week (`GET /me/quota`); at the limit, when more are possible in local time, with "+ New story" disabled (ADR-0030). |
| `/settings`     | The standing story context (`GET`/`PATCH /me/settings`): four free-text answers — the children, the kind of stories, recurring characters, art style — composed into every new conversation with the agent (ADR-0022). |
| `/sign-in`, `/sign-up` | Clerk's hosted components (ADR-0002). |
| `/privacy`      | Public, **draft** privacy page: what is kept, why, who may see it, how to delete it. Linked from the footer and the chat. To be reviewed before launch. |
| `/stories/[id]` | Story viewer (`GET /stories/{id}`): each page is a card with the illustration on top and the text below it. A desktop (≥1024px with a mouse) shows two pages side by side; phones and tablets get a full-screen reader with one page, a back button, and two always-visible page-turn buttons with page dots; swipe and arrow keys also turn pages (ADR-0033). Only a `Complete` story opens: a generating or failed one sends the reader back to the dashboard, which explains its state, so there is nothing here to poll. |

## Running locally

1. Start the API (see [`apps/api/README.md`](../api/README.md)); by default it listens on
   `http://localhost:5080`.
2. Copy `.env.example` to `.env` and fill in the two Clerk keys from the Clerk dashboard
   (`NEXT_PUBLIC_CLERK_PUBLISHABLE_KEY`, `CLERK_SECRET_KEY`).
3. In this folder:

   ```bash
   npm install
   npm run dev          # http://localhost:3000
   ```

The browser only calls `/api/*`. The Route Handler at `src/app/api/[...path]/route.ts` exchanges
the Clerk session for a JWT and forwards the request to `API_URL` with an `Authorization: Bearer`
header ([ADR-0021](../../docs/adr/0021-clerk-integration.md)). The token never reaches the browser,
and the API needs no CORS policy. With no session the handler answers `401` without calling the
API. `API_URL` is server-only; override it in `.env`.

## Running in a container

`Dockerfile` builds the image Azure runs ([ADR-0034](../../docs/adr/0034-azure-hosting-with-terraform.md)).
The build context is this folder, not the monorepo root. `next.config.ts` sets
`output: "standalone"`, so the image ships `server.js` with only the dependencies it uses, and runs
it as the unprivileged `node` user on port 3000.

```bash
docker build -t storywalker-ui \
  --build-arg NEXT_PUBLIC_CLERK_PUBLISHABLE_KEY=pk_test_... .
docker run --env-file .env -p 3000:3000 storywalker-ui
```

The settings arrive at two different times:

- **At build:** `NEXT_PUBLIC_CLERK_PUBLISHABLE_KEY` and
  `NEXT_PUBLIC_APPLICATIONINSIGHTS_CONNECTION_STRING` (see Telemetry below) are inlined into the
  browser bundle by `next build`. They are `--build-arg`s, and an image is built per environment.
  Both are public by design.
- **At run time:** `API_URL`, `CLERK_SECRET_KEY` and `APPLICATIONINSIGHTS_CONNECTION_STRING` come
  from the container's environment. `.dockerignore` keeps `.env` out of the build context, so the
  secret key never ends up in an image layer.

From the repo root, `docker compose --profile api --profile ui up --build` runs the UI with the API
in containers. It reads `CLERK_PUBLISHABLE_KEY` and `CLERK_SECRET_KEY` from the root `.env`
(`.env.example`), and takes port 3000, so stop `npm run dev` first.

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
