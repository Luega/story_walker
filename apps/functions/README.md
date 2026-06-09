# apps/functions — Azure Functions (generation worker)

The story-generation worker: plain, queue-triggered Azure Functions coordinated by
choreography over Azure Service Bus (see
[ADR-0004](../../docs/adr/0004-plain-azure-functions-choreography.md)).

Pipeline stages, one Function per Service Bus queue:

1. `Fn_GenerateBible` — characters, art style, palette, outline → enqueues `text`.
2. `Fn_GenerateText` — full story in a single Foundry pass, split into `Page` rows → enqueues `refimage`.
3. `Fn_GenerateRefImage` — reference image(s) for new characters → fans out N `pageimage` messages.
4. `Fn_GeneratePageImage` — per-page image (parallel), idempotent, atomic completion counter,
   pushes SignalR progress (see [ADR-0005](../../docs/adr/0005-completion-counter-idempotency-deadletter.md)).

References the shared libraries in [`../../src`](../../src) for the EF Core model and the
Service Bus message contracts.
