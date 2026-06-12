# infra — Infrastructure as Code

Infrastructure-as-code (Bicep or Terraform) provisioning the Azure resources for Story Walker:

- Azure Service Bus (generation ingestion queue)
- Azure Durable Functions (generation worker host) + its Azure Storage task hub
- Azure SignalR Service
- PostgreSQL Flexible Server (with the pgvector extension)
- Azure Blob Storage (image store)
- App hosting for the API and UI
- Microsoft Foundry resources

Provisioning is environment-parameterised (e.g. dev / prod) and consumed by the CI/CD
workflows in [`../.github/workflows`](../.github/workflows).
