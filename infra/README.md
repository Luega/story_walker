# infra — Infrastructure as Code

Infrastructure-as-code (Bicep or Terraform) provisioning the Azure resources for Story Walker:

- Azure Service Bus (generation queues)
- Azure Functions (generation worker host)
- Azure SignalR Service
- PostgreSQL Flexible Server (with the `pgvector` extension)
- Azure Blob Storage (image store)
- App hosting for the API and UI
- Microsoft Foundry resources

Provisioning is environment-parameterised (e.g. dev / prod) and consumed by the CI/CD
workflows in [`../.github/workflows`](../.github/workflows).
