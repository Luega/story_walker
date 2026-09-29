# iac — Infrastructure as Code

Infrastructure-as-code (Bicep or Terraform) provisioning the Azure resources for Story Walker:

- Azure Service Bus (generation ingestion queue — one-hour message TTL, ten deliveries, and
  dead-lettering on expiry, as in `docker/servicebus/Config.json`; ADR-0031 and ADR-0032 depend on
  them)
- Azure Functions on Flex Consumption (generation worker host) + the storage account its host needs
- Azure SignalR Service
- PostgreSQL Flexible Server (with the pgvector extension)
- Azure Blob Storage (image store)
- App hosting for the API and UI
- Microsoft Foundry resources

Provisioning is environment-parameterised (e.g. dev / prod) and consumed by the CI/CD
workflows in [`../.github/workflows`](../.github/workflows).
