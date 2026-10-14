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

## Nothing here has been applied

The configuration validates (`terraform validate`), but no `plan` or `apply` has been run against a
subscription yet.

## First run, once per subscription

The state lives in a storage account that Terraform cannot create for itself:

```bash
az group create -n rg-storywalker-tfstate -l swedencentral
az storage account create -n <tfstate-account> -g rg-storywalker-tfstate --sku Standard_LRS \
  --allow-blob-public-access false --min-tls-version TLS1_2
az storage container create -n tfstate --account-name <tfstate-account> --auth-mode login
```

Put its name in `environments/<env>.backend.hcl`, and the subscription id in
`environments/<env>.tfvars`. Your account needs **Storage Blob Data Contributor** on it.

## Plan and apply

```bash
cd iac
export TF_VAR_clerk_secret_key=sk_test_...   # never in a committed file
terraform init -backend-config=environments/prod.backend.hcl
terraform plan -var-file=environments/prod.tfvars -out=prod.tfplan
terraform apply prod.tfplan
```

Until CI has pushed images, `api_image` and `ui_image` stay null and both apps run Microsoft's
quickstart placeholder. Afterwards pass them on the command line:
`-var api_image=ghcr.io/<owner>/storywalker-api:<sha>`, with `ghcr_username` and
`TF_VAR_ghcr_pull_token` set.

## Still missing before a real deployment

- **CI/CD** in `../.github/workflows`: build and push both images, deploy the worker package to the
  function app (`function_app_name` output), then apply with the new tags. The UI image is built
  per environment: `NEXT_PUBLIC_CLERK_PUBLISHABLE_KEY` and
  `NEXT_PUBLIC_APPLICATIONINSIGHTS_CONNECTION_STRING` (the `application_insights_connection_string`
  output) are build arguments of `apps/ui/Dockerfile`, inlined at `next build`.
- **Clerk:** add the `ui_url` output to the instance's allowed origins.
- **Image model region:** `gpt-image-1-mini` is offered in few regions and may need access approval;
  move `foundry_location` if the deployment is refused.

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
