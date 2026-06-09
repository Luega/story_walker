# ADR 0002 — Clerk for authentication

- **Status:** Accepted
- **Date:** 2026-06-09
- **Deciders:** Story Walker team

## Context

The app requires user authentication: sign-up/sign-in, session management, and a private
per-user dashboard. The front-end is Next.js and the back-end is an ASP.NET Core API that
must authorize requests on a per-user basis. We want minimal auth code to own, strong
Next.js integration, and standards-based tokens the .NET API can validate.

## Decision

We will use **Clerk** as the identity provider. The Next.js app uses Clerk's SDK and
prebuilt components for sign-in/up and session handling. The ASP.NET Core API validates
Clerk-issued **JWTs** as a standard OIDC/JWT bearer resource server. Each authenticated
user maps to a local `User` row keyed by their Clerk user id.

## Consequences

### Positive
- First-class Next.js integration (middleware, components, hooks) → fast to build the
  auth UI and route protection.
- No password storage, session, or MFA code to own and secure.
- Standards-based JWTs validated by the .NET API with the standard JWT bearer middleware.

### Negative / trade-offs accepted
- Vendor dependency and per-MAU pricing.
- User identity lives outside our DB; we keep only a mirror (`clerk_user_id`) and must
  handle account deletion/sync.

### Things this decision obliges us to build or watch
- A local `User` record provisioned on first authenticated request (or via Clerk webhooks).
- JWT validation config (issuer, audience, JWKS) on the API; CORS for the Next.js origin.
- SignalR connections authenticated with the same Clerk token.

## Alternatives considered

### Auth0
Mature, enterprise-grade, flexible. Rejected for now: heavier than needed and its Next.js
DX is less seamless than Clerk's for our stack. Reconsider if enterprise SSO/compliance
requirements emerge.

### Roll our own (ASP.NET Core Identity)
Full control, no vendor cost. Rejected: auth is security-critical undifferentiated work;
owning it adds risk and slows delivery.
