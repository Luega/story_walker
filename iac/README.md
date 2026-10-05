# iac — Infrastructure as Code

Terraform describing the Azure resources for Story Walker, one state per environment (dev, prod).
The choices behind it are in [ADR-0034](../docs/adr/0034-azure-hosting-with-terraform.md).

| File | Resources |
|---|---|
| `main.tf` | Resource group, Log Analytics, Application Insights (ADR-0023) |
| `servicebus.tf` | Namespace, the `story-generation` queue (one-hour TTL, ten deliveries, dead-lettering on expiry, as in `docker/servicebus/Config.json`; ADR-0031 and ADR-0032 depend on them), the API's send-only key |
| `postgres.tf` | PostgreSQL 17 Flexible Server and the `storywalker` database (no extensions: pgvector went with ADR-0019) |
| `storage.tf` | Public page-image account and its `story-images` container (ADR-0010); private worker host storage |
| `foundry.tf` | Foundry `AIServices` resource with a chat and an image deployment, keys disabled (ADR-0011) |
| `functions.tf` | The generation worker on Flex Consumption (ADR-0020) |
| `container_apps.tf` | Container registry, Container Apps environment, the API (internal ingress) and the UI (public) |
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
terraform init -backend-config=environments/dev.backend.hcl
terraform plan -var-file=environments/dev.tfvars -out=dev.tfplan
terraform apply dev.tfplan
```

Until CI has pushed images, `api_image` and `ui_image` stay null and both apps run Microsoft's
quickstart placeholder. Afterwards pass them on the command line:
`-var api_image=<registry>/storywalker-api:<sha>`.

## Still missing before a real deployment

- **A UI Dockerfile.** `NEXT_PUBLIC_CLERK_PUBLISHABLE_KEY` and
  `NEXT_PUBLIC_APPLICATIONINSIGHTS_CONNECTION_STRING` are inlined at `next build`, so they are build
  arguments of the image, not only container settings.
- **CI/CD** in `../.github/workflows`: build and push both images, deploy the worker package to the
  function app (`function_app_name` output), then apply with the new tags.
- **Clerk:** add the `ui_url` output to the instance's allowed origins.
- **Image model region:** `gpt-image-1-mini` is offered in few regions and may need access approval;
  move `foundry_location` if the deployment is refused.

## Secrets that remain

The code does not yet accept identities for these, so they are app settings and container secrets —
and they are in the Terraform state, which is why the state account allows no public access:

- the PostgreSQL admin password (`ConnectionStrings__Postgres`);
- the API's send-only Service Bus key (`ServiceBus__ConnectionString`);
- the page-image account key (`Storage__ConnectionString`);
- the Clerk secret key.

Foundry, the worker's Service Bus trigger and the image pulls already use managed identities.
