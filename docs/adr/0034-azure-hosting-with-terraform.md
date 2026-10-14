# ADR 0034 — Azure hosting, described in Terraform

- **Status:** Proposed
- **Date:** 2026-10-05
- **Deciders:** Luca Martinelli

> **Amended (2026-10-14) by [ADR-0035](0035-single-lean-prod-and-github-actions.md).** One environment,
> `prod`, at the smallest sizes; images in private GitHub Container Registry instead of Azure
> Container Registry; CI/CD in GitHub Actions.

## Context

Everything so far runs on a workstation: `docker-compose.yml` stands in for PostgreSQL, Service
Bus, Blob Storage and the Functions host, and Foundry is a resource created by hand in the portal.
`iac/` held only a README listing what Azure would need. Before the first deployment we need that
list written down as code, reviewable now and applied later, per environment (dev, prod).

The code already fixes several of the choices:

- The API publishes with a Service Bus **connection string** (`AddServiceBusPublishing`), the worker
  stores images with a Blob **connection string** (`AddImageStorage`), and Npgsql logs in with a
  **password**. Changing any of these to identities is code work, not infrastructure.
- Foundry is reached through `DefaultAzureCredential` whenever `Foundry:ApiKey` is empty, so a
  managed identity works with no code change.
- The worker's `functionTimeout` is 30 minutes (ADR-0020), which needs Flex Consumption.
- The queue's TTL, delivery count and dead-lettering on expiry are load-bearing for ADR-0031 and
  ADR-0032.
- Page images are opened by the browser straight from Blob (`PublicAccessType.Blob`).

## Decision

We will describe the Azure resources in **Terraform** (azurerm 4.x) under `iac/`, one state per
environment, with:

- **Azure Container Apps** for the API and the UI, images in an Azure Container Registry. The UI is
  the only public endpoint; the API's ingress is internal, because the browser reaches it only
  through the UI's server-side proxy (ADR-0021).
- **Flex Consumption** for the generation worker.
- **PostgreSQL Flexible Server**, **Service Bus** (one queue, mirroring
  `docker/servicebus/Config.json`), and two **storage accounts**: public page images, private worker
  host storage.
- One **Foundry** (`AIServices`) resource with a chat and an image deployment, its keys disabled.
- One **Application Insights** resource on a Log Analytics workspace (ADR-0023).
- **Managed identities** wherever the code already accepts one (Foundry for the API and the worker,
  the worker's Service Bus trigger, image pulls); connection strings where it does not yet.

## Consequences

### Positive
- The whole environment is in review before anything costs money; `terraform plan` shows exactly
  what an apply would do.
- No Foundry key exists anywhere, and the worker holds no Service Bus key.
- The API is not reachable from the internet.

### Negative / trade-offs accepted
- The Postgres password, the API's send-only Service Bus key and the images account key are app
  settings and container secrets, and sit in the Terraform state. The state account must be treated
  as a secret store.
- PostgreSQL is open to "any Azure service" (the 0.0.0.0 firewall rule): neither Container Apps on
  the consumption profile nor Flex Consumption without a VNet has a fixed outbound address to allow
  instead. A VNet is the fix, at a cost an MVP does not yet justify.
- Until CI pushes images, both container apps run Microsoft's quickstart placeholder.

### Things this decision obliges us to build or watch
- A Dockerfile for the UI, with the `NEXT_PUBLIC_*` values as build arguments — done:
  `apps/ui/Dockerfile`, one image per environment.
- The state storage account, created by hand once per subscription.
- CI workflows that build and push the images, deploy the worker package, and pass the image tags
  to `terraform apply`.
- Identity-based Service Bus, Blob and PostgreSQL access in the code, to retire the remaining
  secrets.
- Image-model regional availability: `foundry_location` must be a region that offers it.

## Alternatives considered

### Bicep
Native to Azure and needs no state file. Terraform was chosen for its plan output and its wider
use outside Azure; the README already allowed either.

### App Service for the API and the UI
Viable, and runs Node without a container. But it bills a plan around the clock, while Container
Apps scales both apps to zero, and the API already ships as a container.

### Static Web Apps for the UI
The UI is not static: its route handler holds the Clerk secret and proxies to the API, which the
managed Next.js support of Static Web Apps covers only partially.
