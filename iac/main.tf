locals {
  name = "storywalker-${var.environment}"

  # Storage accounts, the registry and the Foundry subdomain are globally unique and allow no
  # dashes. The suffix keeps two subscriptions (or a re-created environment) from colliding.
  compact = "sw${var.environment}${random_string.suffix.result}"

  tags = merge(
    {
      app         = "storywalker"
      environment = var.environment
      managed_by  = "terraform"
    },
    var.tags,
  )
}

resource "random_string" "suffix" {
  length  = 5
  upper   = false
  special = false
}

resource "azurerm_resource_group" "main" {
  name     = "rg-${local.name}"
  location = var.location
  tags     = local.tags
}

# --- Telemetry (ADR-0023) -----------------------------------------------------
# One Application Insights resource for the UI, the API and the worker, so a story's trace runs
# from the browser to the last page rendered.

resource "azurerm_log_analytics_workspace" "main" {
  name                = "log-${local.name}"
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location
  sku                 = "PerGB2018"
  retention_in_days   = 30
  tags                = local.tags
}

resource "azurerm_application_insights" "main" {
  name                = "appi-${local.name}"
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location
  workspace_id        = azurerm_log_analytics_workspace.main.id
  application_type    = "web"
  tags                = local.tags
}
