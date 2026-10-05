# Who may do what. Every identity gets the narrowest built-in role that covers what its code calls.

# --- Pulling images -----------------------------------------------------------

resource "azurerm_role_assignment" "api_acr_pull" {
  scope                = azurerm_container_registry.main.id
  role_definition_name = "AcrPull"
  principal_id         = azurerm_user_assigned_identity.api.principal_id
}

resource "azurerm_role_assignment" "ui_acr_pull" {
  scope                = azurerm_container_registry.main.id
  role_definition_name = "AcrPull"
  principal_id         = azurerm_user_assigned_identity.ui.principal_id
}

# --- Foundry: chat for the API, chat and images for the worker ----------------
# "Cognitive Services OpenAI User" covers inference on the deployments and nothing else; it cannot
# create, change or delete them.

resource "azurerm_role_assignment" "api_foundry" {
  scope                = azurerm_cognitive_account.foundry.id
  role_definition_name = "Cognitive Services OpenAI User"
  principal_id         = azurerm_user_assigned_identity.api.principal_id
}

resource "azurerm_role_assignment" "worker_foundry" {
  scope                = azurerm_cognitive_account.foundry.id
  role_definition_name = "Cognitive Services OpenAI User"
  principal_id         = azurerm_function_app_flex_consumption.worker.identity[0].principal_id
}

# The FLUX models are served on the Black Forest Labs provider route, outside the OpenAI data
# actions the role above grants; "Cognitive Services User" covers it. Granted only for a FLUX model.
resource "azurerm_role_assignment" "worker_foundry_flux" {
  count = startswith(lower(var.image_model.name), "flux") ? 1 : 0

  scope                = azurerm_cognitive_account.foundry.id
  role_definition_name = "Cognitive Services User"
  principal_id         = azurerm_function_app_flex_consumption.worker.identity[0].principal_id
}

# --- Service Bus: the worker receives from the queue and its dead-letter queue -

resource "azurerm_role_assignment" "worker_servicebus" {
  scope                = azurerm_servicebus_queue.ingestion.id
  role_definition_name = "Azure Service Bus Data Receiver"
  principal_id         = azurerm_function_app_flex_consumption.worker.identity[0].principal_id
}
