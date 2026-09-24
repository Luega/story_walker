# ADR 0023 — OpenTelemetry to Application Insights, end to end

- **Status:** Accepted
- **Date:** 2026-09-24
- **Deciders:** Luca Martinelli

## Context

Story Walker has no observability. The whole codebase holds six `ILogger` calls — three in
`GenerateStory`, three in `PageImageRenderer` — and they go to the console of whichever process
wrote them. The API logs nothing of its own. When a story sits in `Generating`, finding out why means
reading four consoles and lining their timestamps up by hand.

One user action crosses four processes:

1. the **browser** calls a relative `/api/*` URL;
2. the **Next.js server** swaps the Clerk session for a token and forwards the request
   ([ADR-0002](0002-clerk-for-authentication.md));
3. the **API** runs the agent turn against Foundry and, when the interview ends, publishes the brief
   to Service Bus ([ADR-0003](0003-async-generation-via-service-bus.md));
4. the **Functions worker** picks the message up minutes later and renders each page against Foundry
   and Blob Storage ([ADR-0020](0020-plain-service-bus-function-for-generation.md)).

A useful signal therefore has to follow that chain, not only record what each process chose to log.

The constraints that shape the answer:

- The content is about children. Briefs, prompts and chat messages carry names, ages and
  descriptions a parent typed ([ADR-0022](0022-standing-user-context-in-the-agent-prompt.md)).
- There is no Azure infrastructure as code yet (`iac/` is a README), and the local loop and the
  integration tests must keep working with nothing configured.
- Application Insights bills per GB ingested. The first 5 GB a month are free, and after that the
  indicative price is about €2.3–2.8 per GB.

## Decision

We will send **full telemetry** — requests, dependencies, exceptions and logs — from all four
processes to one Application Insights resource through **OpenTelemetry**, with W3C trace context
propagated across every hop so one story is one operation:

| Process | Package | Collects |
|---|---|---|
| API | `Azure.Monitor.OpenTelemetry.AspNetCore` | ASP.NET Core requests, HttpClient, Azure SDK (Service Bus), `ILogger`, plus Npgsql and Microsoft.Extensions.AI spans |
| Worker | `Microsoft.Azure.Functions.Worker.OpenTelemetry` + `Azure.Monitor.OpenTelemetry.Exporter` | Function invocations linked to the Service Bus message, Azure SDK (Blob), HttpClient (Foundry images), `ILogger`, Npgsql |
| Next.js server | `@azure/monitor-opentelemetry` + undici instrumentation | Incoming requests, and the proxy's `fetch` to the API carrying `traceparent` |
| Browser | `@microsoft/applicationinsights-web` | Page views (including App Router navigations), unhandled errors, `fetch` to `/api/*` carrying `traceparent` |

Three sub-decisions carry the design:

1. **Opt-in through the connection string.** Every process registers telemetry only when
   `APPLICATIONINSIGHTS_CONNECTION_STRING` (`NEXT_PUBLIC_…` in the browser) is non-blank. A blank
   string counts as unset, for the reason `apps/api/Program.cs` already gives: compose always defines
   its variables. Nothing is required, so nothing refuses to start.
2. **Identifiers, not content.** Telemetry carries story, page and conversation ids, counts,
   durations and status codes. It never carries titles, prompts, chat messages or brief text.
   Microsoft.Extensions.AI's `EnableSensitiveData` stays off, and the browser SDK does not record
   request or response headers.
3. **Volume is controlled at the source.** The EF Core command log (one line per SQL statement) is
   raised to `Warning`, because the same queries already arrive as Npgsql dependencies. The Functions
   host's own categories are turned down in `host.json`. Sampling stays at the distro default until
   traffic justifies changing it.

## Consequences

### Positive
- One story is one operation. *Transaction search* shows the click, the proxy hop, the API request
  with its Postgres and Service Bus dependencies, and — linked through the message — the worker run
  with every Foundry and Blob call.
- The views Application Insights builds from requests and dependencies (Failures, Performance,
  Application Map) work without writing any code. A 429 from the image deployment or a slow query
  shows up with no `LogWarning` written for it.
- The code change is small: one registration per process, gated on one variable.
- OpenTelemetry is the path Microsoft now recommends, and it keeps the instrumentation portable: a
  different backend is a different exporter, not a rewrite.

### Negative / trade-offs accepted
- **More data than logs alone, so it can cost more.** At today's traffic it stays well inside the
  free 5 GB. At about a thousand daily users the estimate is 10–15 GB a month, roughly €15–30.
- **The browser connection string is public.** It ships in the client bundle by design. It is an
  ingestion key, so someone could send fake telemetry but not read any. That is the accepted
  trade-off of client-side monitoring.
- Four SDKs on two stacks to keep current, and the Microsoft.Extensions.AI source name is still
  `Experimental.` prefixed, so it may move.

### Things this decision obliges us to build or watch
- **The no-content rule is a code-review rule.** Nothing enforces it. A structured log parameter
  named `{Title}` or `{Prompt}` is the thing to catch.
- A **daily cap** on the Log Analytics workspace when the resource is created, so a logging loop
  cannot become a bill.
- The resource itself, and handing its connection string to the hosted apps, belong to the IaC work
  when `iac/` gets written.

## Alternatives considered

### Logs only
Forwarding `ILogger` output as `traces` and nothing else. It is viable and ingests less. It was
rejected because it records only what someone thought to log, and today that is almost nothing. It
also cannot correlate the four processes, and the saving is zero while traffic sits inside the free
tier.

### The classic Application Insights SDKs
`Microsoft.ApplicationInsights.AspNetCore` and the classic Functions integration. These are mature,
but Microsoft is moving them toward maintenance in favour of the OpenTelemetry distro. Starting on
them now means migrating later.

### A self-hosted OpenTelemetry collector
Collector, Grafana, Tempo and Loki. It is portable and has no per-GB bill, but it is infrastructure
to run and secure with no IaC to put it in. Everything else here already lives in Azure.
