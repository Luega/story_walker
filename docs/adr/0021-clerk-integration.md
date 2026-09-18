# ADR 0021 — Clerk integration: server-side token proxy and JIT provisioning

- **Status:** Accepted
- **Date:** 2026-09-16
- **Deciders:** Luca Martinelli

## Context

[ADR-0002](0002-clerk-for-authentication.md) chose Clerk for authentication but left open how it is
wired into this system. The codebase had been written in anticipation: `User.ClerkUserId` and
`Story.UserId` existed, `Conversation.UserId` was nullable "until Clerk auth lands", and every new
story was owned by whichever user happened to be first in the database — in practice the demo user
seeded at startup.

The constraints that shape the wiring:

- The browser talks to a single origin. `/api/*` was a Next.js rewrite to the ASP.NET Core API,
  so the API needs no CORS policy, and `lib/api.ts` calls plain relative URLs.
- The dashboard is a client component that polls; the UI is not built on Server Components.
- Clerk session tokens carry **no `aud` claim**. The `azp` claim — the origin of the front end that
  obtained the token — is what ties a token to this app, and not checking it is a CSRF vector.
- The API test suite is offline and deterministic ([ADR-0017](0017-testcontainers-for-integration-tests.md)).

The full design, with the token-flow diagram, is in
[`docs/superpowers/specs/2026-09-16-clerk-auth-design.md`](../superpowers/specs/2026-09-16-clerk-auth-design.md).

## Decision

We will wire Clerk with four decisions:

1. **A server-side token proxy.** A catch-all Route Handler at `app/api/[...path]/route.ts`
   replaces the rewrite: it reads the Clerk session with `auth()`, calls `getToken()`, and forwards
   the request with an `Authorization: Bearer` header. It fails closed — no session, no request
   leaves the app. The API validates the JWT with the standard `JwtBearer` middleware,
   `ValidateAudience = false`, and rejects any token whose `azp` is not in
   `Clerk:AuthorizedParties`.
2. **Just-in-time user provisioning.** A scoped `CurrentUser` service resolves the `sub` claim to a
   local `User` row, creating it on first sight. Two concurrent first requests are settled by the
   unique index on `ClerkUserId`.
3. **404, not 403, for another user's row.** The owner filter sits inside the query that loads the
   story or conversation, so no handler can forget it and a stranger's id is indistinguishable from
   one that does not exist.
4. **Test-signed JWTs.** The test factory injects an in-memory signing key and mints tokens with a
   test `azp`, so the suite exercises the production middleware — `azp` check included — offline.

## Consequences

### Positive
- The session token never enters browser JavaScript, and `API_URL` never leaves the server.
- `lib/api.ts` and its tests are unchanged: the UI still calls relative URLs.
- Ownership is enforced where rows are loaded, not by a check each endpoint must remember. The
  conversation rule lives in `StoryAgent.LoadConversationAsync` itself.
- The auth tests would catch an issuer, key or `azp` misconfiguration, because they run the real
  middleware rather than a fake scheme.

### Negative / trade-offs accepted
- **Deleting a Clerk account leaves our `User` row behind**, with its stories and conversations.
  Nothing tells us the account is gone. Accepted for the MVP; it must be closed before go-live, for
  GDPR if nothing else.
- Every API call takes an extra hop through the Next.js server.
- Startup seeding is gone. A demo story owned by a demo user would be invisible to every real
  account, so a first sign-in lands on an empty dashboard by design. The seeder lives on in the test
  project, its only remaining consumer.
- Conversations that predated ownership were deleted by the `ConversationOwner` migration: they had
  nobody to attribute them to.

### Things this decision obliges us to build or watch
- **The Clerk session token must carry a custom `email` claim** (Clerk dashboard → Sessions →
  customize session token: `"email": "{{user.primary_email_address}}"`). `email` is not a default
  claim and `User.Email` is required, so without it provisioning fails at runtime on every first
  sign-in. It is configuration, not code, so no test can catch its absence.
- **SignalR must authenticate with the same token** once it is wired; it is not today.
- **Account-deletion sync** (see above) before go-live.
- **Clerk's component API moves fast.** The integration was planned against the pre-Core-3 API and
  built on `@clerk/nextjs` 7 (Core 3), which removed `<SignedIn>`/`<SignedOut>` in favour of
  `<Show when="…">` and moved `<ClerkProvider>` inside `<body>`. The removed components still export
  and typecheck but throw when rendered, and the component tests never render the layout — so a
  Clerk upgrade needs a real render of the layout, not just a green suite.

## Alternatives considered

### Client-side `useAuth().getToken()` with a Bearer header from the browser
Viable and the most common pattern. Rejected because every function in `lib/api.ts` would have to
accept a token, threading it through components that today call plain async functions, and its test
suite would be rewritten — and the token would live in browser JavaScript.

### Fetching from Server Components
Cleaner in the abstract, since the token would never leave the server either. Rejected because the
dashboard is a client component driven by polling: adopting it means rewriting the UI rather than
adding authentication to it.

### Clerk webhooks (`user.created` / `user.deleted`) instead of JIT provisioning
Gives full sync, deletions included — the gap JIT provisioning accepts. Rejected for now because
it needs a public tunnel in local development, another secret, and ordering and retry handling.
Worth revisiting when account deletion has to propagate.

### 403 for another user's row
The conventional status for "exists but not yours". Rejected because it confirms that the id
exists, a disclosure the client has no legitimate use for, and a separate ownership check is one
that some future handler can omit.

### A fake authentication scheme, or live tokens from a Clerk development instance, in tests
A fake scheme never exercises the real middleware, so an issuer, JWKS or `azp` misconfiguration
would pass unnoticed. Live tokens add network, secrets and expiry to a suite that is offline and
deterministic by design (ADR-0017).

## Implements

[ADR-0002 — Clerk for authentication](0002-clerk-for-authentication.md), which stands as written.
