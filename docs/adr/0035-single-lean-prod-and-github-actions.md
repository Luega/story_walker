# ADR 0035 — One lean prod environment, deployed by GitHub Actions

- **Status:** Proposed
- **Date:** 2026-10-14
- **Deciders:** Luca Martinelli

> **Amended (2026-10-15) by [ADR-0036](0036-pull-requests-and-one-required-check.md).** Changes reach
> `main` through pull requests; one CI workflow, `ci.yml`, runs only the component workflows a change
> concerns, on a pull request and before a deploy.

## Context

[ADR-0034](0034-azure-hosting-with-terraform.md) described the Azure resources for two
environments, dev and prod, with prod sized for real traffic: a General Purpose PostgreSQL server,
Service Bus Standard, an Azure Container Registry per environment. Estimated, that is about
$155–240 a month for prod alone before a single story is generated.

The MVP's audience is a handful of people trying it: recruiters and whoever they show it to. It
must be online whenever someone opens the link, and cost as little as possible. The page
illustrations are what the product is, so their quality is not where money is saved.

Nothing has been applied yet, and `.github/workflows/` does not exist, although ADR-0012 (path
filtered workflows), ADR-0017 (`dotnet test` on a Docker-enabled runner) and ADR-0034 (build, push,
deploy, apply) all call for it. Nor does anything create the schema outside Development:
`MigrateAsync()` runs only there.

## Decision

We will run **one environment, `prod`**, at the smallest sizes the code allows, and deploy it with
**GitHub Actions**.

- **Sizes:** PostgreSQL `B_Standard_B1ms` with 32 GB and seven days of backups (the Azure free
  account covers this for twelve months), Service Bus Basic, one replica at most for the API and
  the UI. Resources are resized in place later; no second environment is created.
- **Images in GitHub Container Registry, private**, instead of Azure Container Registry. CI pushes
  with the workflow's `GITHUB_TOKEN`; Container Apps pulls with a classic personal access token
  that has only `read:packages`. Each deploy keeps the last five versions of each image.
- **Spending brakes in Terraform:** a monthly budget on the resource group with e-mail alerts, and
  a daily ingestion cap on the Log Analytics workspace so telemetry stays inside the free 5 GB.
- **CI per component**, as ADR-0012 planned: `api.yml`, `worker.yml`, `ui.yml`, each triggered by
  its own paths (`src/**` triggers both .NET workflows). They build and test; they never touch
  Azure.
- **One CD workflow**, `deploy.yml`, on a push to `main` or by hand. It builds and pushes **both**
  images on every run, tagged with the commit SHA, deploys the worker package, and runs
  `terraform apply` with both tags. Always passing both tags means an apply can never fall back to
  the placeholder image. One deploy runs at a time.
- **GitHub reaches Azure through OIDC:** a federated credential on an Entra app registration trusts
  only this repository's `prod` environment. No Azure secret is stored in GitHub.
- **Models the subscription has quota for.** The Free Trial subscription prod runs in has no quota
  (0) for the GPT-5.6 family or any OpenAI image model, so prod uses `gpt-5-mini` for chat and
  `FLUX.2-pro` for illustrations, both supported without code changes. Prod returns to
  `gpt-5.6-luna` and `gpt-image-1-mini` when quota is granted, by changing `prod.tfvars`.
- **The API creates and updates the schema at startup**, in every environment except `Testing`
  (where the test fixture owns it). EF applies only the migrations missing from
  `__EFMigrationsHistory`; nothing is seeded, so prod starts empty and fills with use.

## Consequences

### Positive
- About $7 a month in the free account's first year (the registry and PostgreSQL cost nothing),
  about $25 after it, plus Foundry per story.
- Every change is built and tested before it reaches `main`, and reaches prod without manual steps.
- No Azure credential lives in GitHub, and none is long-lived.
- A rollback is a manual run of `deploy.yml` on an older commit.

### Negative / trade-offs accepted
- No staging environment: a change is first seen running in prod. CI is the only gate.
- PostgreSQL Burstable gives no performance guarantee under load; acceptable for a handful of users.
- The GHCR pull token is one more secret: it expires, must be renewed in GitHub, and sits in the
  Terraform state with the others.
- GHCR storage for private images is free today; GitHub may start charging, with notice. Keeping
  five versions keeps the footprint small.
- Every deploy builds both images even when one did not change (a few extra runner minutes).
- Migrating at startup is safe only while the API runs a single replica. Raising
  `api_max_replicas` means moving migrations into `deploy.yml` first.

### Things this decision obliges us to build or watch
- Checking the agent and the illustrations on `gpt-5-mini` and `FLUX.2-pro`, which local
  development does not use, and requesting quota for the original models.
- The one-time bootstrap by hand: the state storage account, the app registration and its
  federated credential and roles, the GitHub environment's secrets, the first `apply`.
- Renewing the GHCR pull token before it expires.
- The budget alerts' e-mail address, and acting on them.

## Alternatives considered

### Keep dev and prod
Two of everything doubles the fixed cost, and for a portfolio MVP the second environment would
mostly sit idle.

### Azure Container Registry
About $5 a month per registry, and image pulls through managed identity with no token to renew.
GHCR is free and lives next to the code; the token is the price.

### Public GHCR images
Free with certainty and no pull token at all. Rejected: the images are kept private.

### Migrations as a step in `deploy.yml`
An EF migrations bundle run from the runner, with a PostgreSQL firewall rule opened for its address
and closed afterwards. More controlled, since the schema changes only during a deploy, but it
needs one more permission and one more moving part. It becomes the right choice once the API runs
more than one replica.

### Hosting on a home machine
Cheaper still, but the app is online only while the machine is, and exposing it safely (a tunnel,
a read-only proxy in front of the storage emulator) is more work than the savings justify.
