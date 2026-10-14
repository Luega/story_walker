output "resource_group_name" {
  value = azurerm_resource_group.main.name
}

output "ui_url" {
  description = "Where the app is served. Add it to the Clerk instance's allowed origins."
  value       = local.ui_origin
}

output "function_app_name" {
  description = "Target of the worker's deployment (az functionapp deployment / Azure/functions-action)."
  value       = azurerm_function_app_flex_consumption.worker.name
}

output "foundry_endpoint" {
  value = local.foundry_endpoint
}

output "postgres_fqdn" {
  value = azurerm_postgresql_flexible_server.main.fqdn
}

output "postgres_connection_string" {
  description = "For running `dotnet ef database update` or psql by hand."
  value       = local.postgres_connection_string
  sensitive   = true
}

output "application_insights_connection_string" {
  description = "The UI image build needs it as NEXT_PUBLIC_APPLICATIONINSIGHTS_CONNECTION_STRING (ADR-0023)."
  value       = azurerm_application_insights.main.connection_string
  sensitive   = true
}

output "clerk_publishable_key" {
  description = "The UI image build needs it as NEXT_PUBLIC_CLERK_PUBLISHABLE_KEY, so prod.tfvars stays its one source."
  value       = var.clerk_publishable_key
}
