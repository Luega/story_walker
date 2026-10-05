# The generation worker (ADR-0020) on Flex Consumption: functionTimeout in host.json is 30 minutes,
# which the legacy Consumption plan cannot give. The app settings are the keys of
# apps/functions/local.settings.example.json, with ':' written as '__'.

resource "azurerm_service_plan" "functions" {
  name                = "asp-${local.name}-functions"
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location
  os_type             = "Linux"
  sku_name            = "FC1"
  tags                = local.tags
}

resource "azurerm_function_app_flex_consumption" "worker" {
  name                = "func-${local.name}-${random_string.suffix.result}"
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location
  service_plan_id     = azurerm_service_plan.functions.id

  runtime_name    = "dotnet-isolated"
  runtime_version = "10.0"

  # The provider also writes AzureWebJobsStorage from these.
  storage_container_type      = "blobContainer"
  storage_container_endpoint  = "${azurerm_storage_account.functions.primary_blob_endpoint}${azurerm_storage_container.functions_deployment.name}"
  storage_authentication_type = "StorageAccountConnectionString"
  storage_access_key          = azurerm_storage_account.functions.primary_access_key

  maximum_instance_count = var.functions_maximum_instance_count
  instance_memory_in_mb  = var.functions_instance_memory_mb

  # Foundry and Service Bus are reached with this identity; see roles.tf.
  identity {
    type = "SystemAssigned"
  }

  site_config {
    # Also sets APPLICATIONINSIGHTS_CONNECTION_STRING, which Program.cs reads (ADR-0023).
    application_insights_connection_string = azurerm_application_insights.main.connection_string
  }

  app_settings = {
    # Identity-based trigger connection: the [ServiceBusTrigger] Connection name with the
    # fullyQualifiedNamespace suffix. No SAS key exists for the worker.
    "ServiceBusConnection__fullyQualifiedNamespace" = "${azurerm_servicebus_namespace.main.name}.servicebus.windows.net"

    "ConnectionStrings__Postgres" = local.postgres_connection_string

    # Foundry__ApiKey is deliberately absent: DefaultAzureCredential picks up the managed identity.
    "Foundry__Endpoint"        = local.foundry_endpoint
    "Foundry__Deployment"      = azurerm_cognitive_deployment.chat.name
    "Foundry__ImageDeployment" = azurerm_cognitive_deployment.image.name

    "Storage__ConnectionString" = azurerm_storage_account.images.primary_blob_connection_string
    "Storage__Container"        = azurerm_storage_container.images.name
    "Storage__PublicBaseUrl"    = trimsuffix(azurerm_storage_account.images.primary_blob_endpoint, "/")
  }

  tags = local.tags
}
