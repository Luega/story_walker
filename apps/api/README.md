# apps/api — ASP.NET Core API

The ASP.NET Core Web API: validates Clerk JWTs, owns the domain data (PostgreSQL + pgvector),
runs the information-gathering agent (Microsoft Foundry), and enqueues generation jobs on
Azure Service Bus (fire-and-forget, returns `202`). References the shared libraries in
[`../../src`](../../src).
