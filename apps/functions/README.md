# apps/functions — Azure Functions (generation worker)

The story-generation worker: **one Service Bus-triggered function**, `GenerateStory`
([ADR-0020](../../docs/adr/0020-plain-service-bus-function-for-generation.md)). There is no
orchestrator — [ADR-0004](../../docs/adr/0004-durable-functions-for-generation.md) chose Durable
Functions for a five-stage pipeline with two fan-out phases, and that pipeline no longer exists.

All the text and every illustration prompt arrive in the agent-authored brief
([ADR-0013](../../docs/adr/0013-agent-authored-generation-brief.md)); the worker never calls a chat
model. On each message it:

1. persists the six `Page` rows (text + illustration prompt) from the brief, all `Pending` — the
   `Story` row already exists, created by the API at enqueue time;
2. renders one image per page **in order**, uploading each to Blob Storage
   ([ADR-0010](../../docs/adr/0010-azure-blob-storage-for-images.md)); the page gets its `ImageUrl`
   and goes `Complete`, or `Failed` if the model refuses the prompt;
3. settles the story: `Complete` if at least one page rendered, otherwise `Failed`. A story with
   five of six pictures is still readable, so the per-page `Status` is what tells a client which
   illustration is missing.

Rendering is **serial, not parallel**. Six concurrent calls against an image deployment on default
quota produce a 429 storm, and six sequential renders take about the "few minutes" the agent already
promises the user.

## Resumability without an orchestrator

Every step is idempotent ([ADR-0005](../../docs/adr/0005-idempotency-and-failure-handling.md)) and
**the database is the checkpoint**: pages that exist are not written again, pages that already have
an image are not re-rendered. So if the function dies halfway — crash, restart, timeout — Service
Bus redelivers the message (the queue allows ten deliveries) and the next run continues from the
first page without a picture. Duplicate delivery is safe for the same reason.

Transient failures are retried **inside** `PageImageRenderer` with exponential backoff (3 attempts,
5 s doubling) rather than by throwing: an exception abandons the message, which Service Bus makes
available again immediately, so a throttled deployment would burn all ten deliveries in seconds. A
content-filter refusal is permanent, so it is never retried — that page is marked `Failed` and the
story carries on.

`maxConcurrentCalls` is pinned to 1 in `host.json` so two stories cannot compete for the same image
quota.

The function is a thin shell over two services in
[`StoryWalker.Infrastructure/Generation`](../../src/StoryWalker.Infrastructure/Generation) —
`StoryPersister` and `PageImageRenderer` — so the logic is tested against a real database in
`tests/StoryWalker.Api.Tests` (ADR-0017).

## Configuration

Locally, configuration lives in `local.settings.json`. That file is **git-ignored and never
published** (`CopyToPublishDirectory=Never` in the csproj), so it is not in the repository. Start
from the committed template:

```bash
cp apps/functions/local.settings.example.json apps/functions/local.settings.json
```

then fill in the three `Foundry:*` values. Everything else already points at the services in
`docker-compose.yml` and works as-is.

`local.settings.json` is not the Functions counterpart of `appsettings.json`: it emulates the
function app's **Application Settings** in Azure. That matters because triggers and bindings are
resolved by the Functions *host*, before any of our code runs, and the host reads app settings —
not an `appsettings.json` in our worker. In Azure the same keys go in the function app's
Application Settings, with `:` written as `__` (for example `Foundry__ImageDeployment`), exactly
as `docker-compose.yml` already does.

| Key | Read by | Local value | Notes |
|---|---|---|---|
| `FUNCTIONS_WORKER_RUNTIME` | host | `dotnet-isolated` | Fixed. |
| `AzureWebJobsStorage` | host | `UseDevelopmentStorage=true` | Required by the host for any non-HTTP trigger. Points at azurite. |
| `ServiceBusConnection` | host | emulator string, as in the template | The `Connection` name on `[ServiceBusTrigger]` in `GenerateStory.cs`. The emulator key is a public placeholder, not a secret. |
| `ConnectionStrings:Postgres` | `Program.cs` | compose's postgres | Under `Values`, **not** the top-level `ConnectionStrings` block: that block is SqlClient-specific and is not published to Azure. |
| `Foundry:Endpoint` | `Program.cs` | **you fill in** | Your Foundry resource, e.g. `https://<name>.services.ai.azure.com`. Same endpoint as the API. |
| `Foundry:ImageDeployment` | `Program.cs` | **you fill in** | A **second** deployment on that resource — a Black Forest Labs FLUX model: `FLUX.2-pro`, `FLUX.2-flex`, `FLUX.1-Kontext-pro` or `FLUX-1.1-pro`. Anything else stops the worker at startup. It is called through the BFL provider route (`/providers/blackforestlabs/v1/…`), not the OpenAI images route, which Foundry does not serve for FLUX.2. |
| `Foundry:ApiKey` | `Program.cs` | optional | Leave empty to authenticate with your own `az login` (`DefaultAzureCredential`). In Azure, leave it empty and use a managed identity. **Secret** — never commit a real value. |
| `Storage:ConnectionString` | `Program.cs` | `UseDevelopmentStorage=true` | Where page images go (ADR-0010). Same azurite account as `AzureWebJobsStorage`, different container. |
| `Storage:Container` | `Program.cs` | `story-images` | Defaults to `story-images` if omitted. |
| `Storage:PublicBaseUrl` | `Program.cs` | `http://localhost:10000/devstoreaccount1` | Base of the image URL saved on each page. In Azure: `https://<account>.blob.core.windows.net`. |

Every key read by `Program.cs` except `Foundry:ApiKey` and `Storage:Container` is **required**: the
worker refuses to start without it, rather than starting and failing on the first message.

`Storage:PublicBaseUrl` is deliberately separate from the storage connection string: inside compose
the worker uploads through `http://azurite:10000`, a hostname that does not resolve from the browser
that eventually opens the picture. The URL persisted on the page is composed from this setting, not
from the SDK's upload URI.

When you add a setting to `Program.cs`, add it to `local.settings.example.json`, to the table above,
and to the `functions` service in `docker-compose.yml` — the template is the only way someone who
clones the repository learns it exists.

## Inspect the images (Storage Explorer)

`docker compose up` also starts a web UI for Azurite — the storage counterpart of pgAdmin:

- URL — <http://localhost:5055>, already logged in to Azurite (no credentials to type).
- **Blobs → `story-images` → `stories/{storyId}/pages/`** holds one PNG per page, with download and
  delete. Leave the `azure-webjobs-hosts` container alone: it holds the Functions host's locks.
- The UI has no authentication and can delete containers or flip them public. Local development
  only.

It is [`sebagomez/azurestorageexplorer`](https://github.com/sebagomez/azurestorageexplorer), a
community project (MIT), pinned to a version in `docker-compose.yml`.

Prefer the terminal? Azurite's development account key is public and the same everywhere:

```bash
CS='DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;'
az storage blob list --connection-string "$CS" --container-name story-images \
  --query "[].{name:name, bytes:properties.contentLength}" -o table
```

Images live in the `azurite-data` volume, so they survive `docker compose down`; only
`docker compose down -v` wipes them — together with the database, which keeps the two consistent.

## Run and debug locally

The worker needs the Service Bus emulator (+ its MSSQL sidecar) and Azurite; all three are in the
root `docker-compose.yml`.

- **Everything in Docker:** `docker compose --profile worker up` — the worker runs in the `functions`
  container. Watch output with `docker compose logs -f functions`.
- **Debug with breakpoints:** `docker compose up` (no `worker` profile) starts the infra but **not**
  the `functions` container, so nothing competes for the queue. Then run the worker locally with
  [Azure Functions Core Tools](https://learn.microsoft.com/azure/azure-functions/functions-run-local)
  (`func start` in this folder) or F5 in your IDE, after creating `local.settings.json` from the
  template (see [Configuration](#configuration)). It points at the emulator on `localhost:5672` and
  Azurite on `localhost`.
