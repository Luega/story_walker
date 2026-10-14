# iac — Infrastructure as Code

Terraform describing the Azure resources for Story Walker, in one environment, `prod`, sized as
small as the code allows. The choices behind it are in
[ADR-0034](../docs/adr/0034-azure-hosting-with-terraform.md) and
[ADR-0035](../docs/adr/0035-single-lean-prod-and-github-actions.md).

| File | Resources |
|---|---|
| `main.tf` | Resource group, Log Analytics, Application Insights (ADR-0023) |
| `servicebus.tf` | Namespace, the `story-generation` queue (one-hour TTL, ten deliveries, dead-lettering on expiry, as in `docker/servicebus/Config.json`; ADR-0031 and ADR-0032 depend on them), the API's send-only key |
| `postgres.tf` | PostgreSQL 17 Flexible Server and the `storywalker` database (no extensions: pgvector went with ADR-0019) |
| `storage.tf` | Public page-image account and its `story-images` container (ADR-0010); private worker host storage |
| `foundry.tf` | Foundry `AIServices` resource with a chat and an image deployment, keys disabled (ADR-0011) |
| `functions.tf` | The generation worker on Flex Consumption (ADR-0020) |
| `container_apps.tf` | Container Apps environment, the API (internal ingress) and the UI (public), pulling private images from GitHub Container Registry |
| `roles.tf` | Role assignments for the managed identities |
| `budget.tf` | Monthly budget on the resource group, with e-mail alerts (ADR-0035) |

## Nothing here has been applied

The configuration validates (`terraform validate`), but no `plan` or `apply` has been run against a
subscription yet.

## How a deploy works

`.github/workflows/deploy.yml` deploys prod on every push to `main` that touches `apps/`, `src/`,
`tests/`, `iac/` or the workflows, and on demand from the Actions tab (ADR-0035):

1. it runs the CI workflows (`api.yml`, `worker.yml`, `ui.yml`) and stops if any fails;
2. signs in to Azure with OIDC: no Azure secret is stored in GitHub;
3. reads three Terraform outputs: the Application Insights connection string and the Clerk
   publishable key, which `apps/ui/Dockerfile` inlines at `next build`, and the function app name;
4. builds both images and pushes them to GitHub Container Registry, private, tagged with the commit
   SHA;
5. deploys the worker package to the Flex Consumption app;
6. runs `terraform apply` with both tags, so Container Apps starts a new revision of each app and
   moves traffic once it answers its probe. Anything changed under `iac/` is applied in the same run;
7. deletes all but the last five versions of each image.

On startup the API applies the migrations the database lacks, so the first deploy creates the empty
schema and later ones apply only new migrations. Nothing is seeded.

To roll back, run the workflow by hand on a branch or tag that points at the earlier commit.

## Bootstrap, once

Everything below is done by hand, once, in this order. `<sub>` is the subscription id.

### 1. Terraform state

The state lives in a storage account that Terraform cannot create for itself:

```bash
az login
az group create -n rg-storywalker-tfstate -l swedencentral
az storage account create -n <tfstate-account> -g rg-storywalker-tfstate --sku Standard_LRS \
  --allow-blob-public-access false --min-tls-version TLS1_2
az storage container create -n tfstate --account-name <tfstate-account> --auth-mode login
```

Put its name in `environments/prod.backend.hcl`. Your account needs **Storage Blob Data
Contributor** on it.

### 2. Values in `environments/prod.tfvars`

Set `subscription_id`, `clerk_authority` and `clerk_publishable_key`. None of them is a secret, and
the deploy reads the publishable key back from Terraform rather than keeping a second copy.

### 3. First apply, from your machine

It creates every resource, with both apps on Microsoft's quickstart placeholder until the first
deploy. Your account needs **Owner** (or Contributor plus Role Based Access Control Administrator),
because the configuration assigns roles.

```bash
cd iac
export TF_VAR_clerk_secret_key=sk_test_...                # never in a committed file
export TF_VAR_budget_contact_emails='["you@example.com"]'  # optional: no budget without it
terraform init -backend-config=environments/prod.backend.hcl
terraform plan -var-file=environments/prod.tfvars -out=prod.tfplan
terraform apply prod.tfplan
```

### 4. An identity for GitHub Actions

An app registration that trusts only this repository's `prod` environment:

```bash
app_id=$(az ad app create --display-name storywalker-github-deploy --query appId -o tsv)
az ad sp create --id "$app_id"
az ad app federated-credential create --id "$app_id" --parameters '{
  "name": "github-prod",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:Luega/story_walker:environment:prod",
  "audiences": ["api://AzureADTokenExchange"]
}'
az role assignment create --assignee "$app_id" --role Contributor --scope /subscriptions/<sub>
az role assignment create --assignee "$app_id" --role "Role Based Access Control Administrator" \
  --scope /subscriptions/<sub>
az role assignment create --assignee "$app_id" --role "Storage Blob Data Contributor" \
  --scope "$(az storage account show -n <tfstate-account> --query id -o tsv)"
```

Role Based Access Control Administrator is there because the apply manages the role assignments in
`roles.tf`.

### 5. A token to pull the images

GitHub → Settings → Developer settings → Personal access tokens → **Tokens (classic)**, with only
the `read:packages` scope. GitHub Container Registry does not accept fine-grained tokens. Give it an
expiry you will remember: when it expires, the apps keep running but can no longer start a new
revision. Renewing means a new token in the `GHCR_PULL_TOKEN` secret and a deploy.

### 6. The GitHub environment

Repository → Settings → Environments → **New environment** `prod`, with these secrets:

| Secret | Value |
|---|---|
| `AZURE_CLIENT_ID` | `$app_id` from step 4 |
| `AZURE_TENANT_ID` | `az account show --query tenantId -o tsv` |
| `AZURE_SUBSCRIPTION_ID` | `<sub>` |
| `CLERK_SECRET_KEY` | the Clerk secret key |
| `GHCR_PULL_TOKEN` | the token from step 5 |

And optionally the variable `BUDGET_CONTACT_EMAILS`, a JSON list such as `["you@example.com"]`.
Adding yourself as a required reviewer makes every deploy wait for your approval.

### 7. First deploy

Actions → **Deploy** → Run workflow. Then sign in once at the `ui_url` output: that starts the API,
which creates the schema. Until then the worker's timer (ADR-0031) logs that the tables do not exist.

Clerk needs no change for the address: Terraform already puts `ui_url` in the API's
`Clerk__AuthorizedParties`, and a Clerk development instance is not tied to a domain.

## Still to watch

- **Image model region:** `gpt-image-1-mini` is offered in few regions and may need access approval;
  move `foundry_location` if the deployment is refused.
- **The pull token's expiry** (step 5).

## Secrets that remain

The code does not yet accept identities for these, so they are app settings and container secrets —
and they are in the Terraform state, which is why the state account allows no public access:

- the PostgreSQL admin password (`ConnectionStrings__Postgres`);
- the API's send-only Service Bus key (`ServiceBus__ConnectionString`);
- the page-image account key (`Storage__ConnectionString`);
- the Clerk secret key;
- the GitHub token that pulls the private images (`ghcr_pull_token`, ADR-0035), which GitHub
  Container Registry cannot replace with an Azure identity.

Foundry and the worker's Service Bus trigger already use managed identities.
