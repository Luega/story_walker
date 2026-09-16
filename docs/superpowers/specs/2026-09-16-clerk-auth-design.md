# Clerk authentication — integration design

- **Date:** 2026-09-16
- **Status:** Approved, not yet implemented
- **Builds on:** [ADR-0002](../../adr/0002-clerk-for-authentication.md) (Clerk as the identity provider)

## Goal

Every story and conversation belongs to the signed-in user, end to end: the Next.js app
authenticates with Clerk, the ASP.NET Core API validates Clerk JWTs, and no endpoint returns
another user's data. ADR-0002 already chose Clerk; this document decides *how* it is wired.

The codebase was written in anticipation of this work — `User.ClerkUserId` exists,
`Story.UserId` exists, `Conversation.UserId` is nullable "until Clerk auth lands", and
`StartGenerationAsync` picks the first user in the database as the owner of every new story.
This design removes every one of those placeholders.

## Token flow

```
Browser                 Next.js (single origin)             API .NET
  │                        │                                   │
  │ cookie __session ─────►│ proxy.ts (clerkMiddleware)         │
  │                        │   └─ unauthenticated → /sign-in    │
  │ fetch /api/stories ───►│ app/api/[...path]/route.ts         │
  │                        │   auth().getToken() ──────────────►│ JwtBearer
  │                        │   Authorization: Bearer <jwt>      │  ├─ Authority = Clerk
  │◄───────── JSON ────────│◄──────────────────────────────────│  ├─ ValidateAudience = false
                                                                  └─ azp ∈ AuthorizedParties
```

The session token never reaches browser JavaScript. `API_URL` stays server-only.

### Decision: a server-side proxy, not a client-side Bearer

The `/api/:path*` rewrite in `next.config.ts` is **removed** and replaced by a catch-all Route
Handler at `app/api/[...path]/route.ts`, which reads the session with `auth()`, calls `getToken()`
and forwards the request with an `Authorization: Bearer` header.

Rejected alternatives:

- *Client-side `useAuth().getToken()`* — every function in `lib/api.ts` would have to accept a
  token, threading it through components that currently call plain async functions, and its test
  suite would be rewritten. The token would also live in browser JavaScript.
- *Fetching from Server Components* — cleaner in the abstract, but the dashboard is a client
  component driven by polling. Adopting it means rewriting the UI, not adding auth to it.

## API

### Authentication

A DI extension in `apps/api/Auth/ClerkAuthentication.cs` registers JWT bearer authentication:

- `Authority` = `Clerk:Authority` (the Clerk instance URL; JWKS is discovered from it).
- `ValidateAudience = false`. Clerk session tokens carry **no `aud` claim** — the v2 default claim
  set is `azp, exp, fva, iat, iss, jti, nbf, sid, sub, v, pla, fea, sts` plus organization/actor
  claims when applicable.
- `OnTokenValidated` rejects the token unless its `azp` claim is one of `Clerk:AuthorizedParties`.
  `azp` is the `Origin` of the front-end that obtained the token; **not** checking it opens a CSRF
  vector, so this check is the audience check's replacement, not an optional extra.

Program fails at startup when `Clerk:Authority` or `Clerk:AuthorizedParties` is missing, following
the existing Foundry/Service Bus guards — and, like them, treats blank as unconfigured, because
docker-compose always defines the variables and an unset one arrives as `""`.

`/health` stays anonymous. The `/stories` group gets `.RequireAuthorization()`.

### Current user and just-in-time provisioning

A scoped `CurrentUser` service in `apps/api/Auth/` resolves the `sub` claim to a local `User` row,
creating it on first sight with the `email` claim. This is a web-layer concern and lives in
`apps/api`; `Domain`, `Application` and `Infrastructure` are untouched apart from the migration.

Two concurrent first requests can both find no row and both insert. The unique index on
`ClerkUserId` settles it: catch `DbUpdateException`, re-read, proceed.

**`email` is not a default claim.** It must be added to the session token from the Clerk dashboard
(Sessions → customize session token) as `"email": "{{user.primary_email_address}}"`, because
`User.Email` is `required`. Without this configuration step provisioning fails at runtime — it is
part of the setup, not an optimisation.

Rejected alternative: *Clerk webhooks* (`user.created`/`user.deleted`). Full sync including
deletions, but it needs a public tunnel in local development, another secret, and ordering/retry
handling. Account deletion not propagating to our database is an accepted MVP gap; revisit before
go-live for GDPR.

### Ownership

| Endpoint | Change |
| --- | --- |
| `GET /stories` | `.Where(s => s.UserId == me)` |
| `GET /stories/{id}` | filtered by owner |
| `PATCH /stories/{id}` | filtered by owner |
| `DELETE /stories/{id}` | filtered by owner |
| `POST /stories/conversations` | `StartGenerationAsync` uses `me`; the "no user exists to own the story" 503 branch disappears |

**Another user's story is a 404, not a 403.** A 403 confirms that the id exists, which is a
disclosure the client has no legitimate use for. Filtering by owner in the same query that loads
the story also makes the rule impossible to forget: there is no separate check to omit.

### Conversation ownership

`IStoryAgent.SendAsync` takes a `Guid userId`. The first turn writes `Conversation.UserId`; later
turns look the conversation up **filtered by that user**, so a stolen conversation id raises
`ConversationNotFoundException` and surfaces as the existing 404 path. The rule lives where the
conversation is persisted rather than as a separate guard in the endpoint.

`Conversation.UserId` becomes non-nullable with a foreign key to `User` (EF migration; local
development data is disposable, and no environment holds data worth preserving yet).

### DbSeeder

Startup seeding is removed from `Program.cs`. With per-user scoping, a story owned by
`user_demo_seed` is invisible to every real account, and a first login lands on an empty dashboard
by design — the user creates a real story by talking to the agent.

`DbSeeder` therefore has one consumer left, the test suite, so it **moves into
`tests/StoryWalker.Api.Tests/`** and merges into `SeedData`, gaining an owner parameter:
`SeedData.SeedDemoStoryAsync(db, ownerUserId)`.

## UI

New dependencies: `@clerk/nextjs` and `@clerk/localizations` (the UI is in Italian → `itIT`).

### `src/proxy.ts`

Next.js 16 renamed `middleware.ts` to `proxy.ts`; `clerkMiddleware()` is used unchanged, only the
filename differs.

- Public: `/`, `/sign-in(.*)`, `/sign-up(.*)`.
- Everything else protected via `auth.protect()`.
- The matcher must include `/__clerk/(.*)` alongside the standard asset-excluding pattern and
  `/(api|trpc)(.*)`, or Clerk's handshake never reaches the middleware.

### Routes

| Before | After | Note |
| --- | --- | --- |
| `app/page.tsx` (dashboard) | `app/dashboard/page.tsx` | unchanged; stays a client component with polling |
| `app/page.test.tsx` | `app/dashboard/page.test.tsx` | assertions unchanged |
| — | `app/page.tsx` | public landing, server component |
| — | `app/sign-in/[[...sign-in]]/page.tsx` | `<SignIn />` |
| — | `app/sign-up/[[...sign-up]]/page.tsx` | `<SignUp />` |
| — | `app/api/[...path]/route.ts` | the proxy above |
| `app/stories/[id]/` | unchanged | |

`layout.tsx` is wrapped in `<ClerkProvider localization={itIT}>`. The header gains
`<SignedIn><UserButton /></SignedIn>` and a `<SignedOut>` pair of sign-in/sign-up links; the
wordmark links to `/dashboard` when signed in and `/` when signed out.

The landing page is a hero (title plus one line on what the app does), three steps on how it works
(talk to the agent → it authors the brief → an illustrated story arrives), and a call to action.
It reuses the existing design tokens — Fraunces via `font-display`, `border-border`, the
`max-w-6xl` grid — and introduces no new assets.

`lib/api.ts` does not change. Its URLs stay relative, MSW keeps intercepting `/api/*`, and its
tests stay green.

### Testing the proxy route handler

The Route Handler is the one new piece of security-sensitive logic, and the existing vitest setup
runs in real Chromium, where a server-side handler cannot execute. A **second vitest project with
`environment: "node"`** covers `src/app/api/**` with `@clerk/nextjs/server` mocked.

Cases: forwards the Bearer token; propagates method, body and status code; and **fails closed** —
when `getToken()` returns null it must not forward the request anonymously.

## Configuration

| Where | Keys |
| --- | --- |
| `apps/ui/.env.example` | `NEXT_PUBLIC_CLERK_PUBLISHABLE_KEY`, `CLERK_SECRET_KEY` (alongside the existing `API_URL`) |
| `apps/api/appsettings.json`, `appsettings.Development.json.example` | `Clerk:Authority`, `Clerk:AuthorizedParties` |
| `.env.example`, `docker-compose.yml` | `CLERK__AUTHORITY`, `CLERK__AUTHORIZEDPARTIES` |

Manual Clerk dashboard steps, which no code can perform:

1. Create the application and copy the publishable and secret keys.
2. Customize the session token to add `"email": "{{user.primary_email_address}}"`.

## API test strategy

`StoryWalkerApiFactory` clears `Authority` (so nothing reaches the network), injects an in-memory
signing key into `JwtBearerOptions.TokenValidationParameters`, and sets `Clerk:AuthorizedParties`
to a test origin. A new `CreateClientAs(clerkUserId, email)` mints a token carrying that origin as
`azp`.

The production JWT middleware is the code under test, `azp` validation included. Rejected
alternatives: a fake authentication scheme (never exercises the real middleware, so an issuer or
JWKS misconfiguration passes unnoticed) and real tokens from a Clerk development instance (adds
network, secrets and expiry to a suite that is offline and deterministic, contrary to ADR-0017's
spirit).

New cases:

- 401 with no token.
- 401 when `azp` is not an authorized party.
- `GET /stories` returns only the caller's stories.
- `GET`/`PATCH`/`DELETE` of another user's story returns 404.
- The first authenticated request creates the `User`; the second does not duplicate it.
- Continuing another user's conversation returns 404.

## Documentation

ADR-0002 stands as written and is not modified. The integration decisions taken here — the
server-side token proxy, JIT provisioning, 404 over 403, and test-signed JWTs — become
**ADR-0021**, which references 0002. The ADR list in `README.md` gains the entry.

## Out of scope

- Clerk webhooks and account-deletion sync.
- Authenticating SignalR connections (SignalR is not yet wired; ADR-0002 notes the obligation).
- Organizations, roles and any authorization beyond "the owner of the row".
- A public marketing site beyond the single landing page described above.
