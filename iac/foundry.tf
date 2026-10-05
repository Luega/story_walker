# Microsoft Foundry (ADR-0011): one resource with two deployments, the chat model the agent talks
# through and the image model the worker renders pages with.

resource "azurerm_cognitive_account" "foundry" {
  name                = "ais-${local.name}"
  resource_group_name = azurerm_resource_group.main.name
  location            = var.foundry_location
  kind                = "AIServices"
  sku_name            = "S0"

  # The subdomain is the endpoint (Foundry__Endpoint) and is globally unique.
  custom_subdomain_name = "${local.compact}-foundry"

  # Keys off: the API and the worker authenticate with their managed identities
  # (DefaultAzureCredential, Foundry__ApiKey left empty), so there is no key to store or leak.
  local_auth_enabled = false

  identity {
    type = "SystemAssigned"
  }

  tags = local.tags
}

resource "azurerm_cognitive_deployment" "chat" {
  name                 = var.chat_model.name
  cognitive_account_id = azurerm_cognitive_account.foundry.id

  model {
    format  = "OpenAI"
    name    = var.chat_model.name
    version = var.chat_model.version
  }

  sku {
    name     = var.chat_model.sku
    capacity = var.chat_model.capacity
  }
}

resource "azurerm_cognitive_deployment" "image" {
  name                 = var.image_model.name
  cognitive_account_id = azurerm_cognitive_account.foundry.id

  model {
    format  = var.image_model.format
    name    = var.image_model.name
    version = var.image_model.version
  }

  sku {
    name     = var.image_model.sku
    capacity = var.image_model.capacity
  }

  # One deployment at a time: Foundry rejects concurrent deployment operations on an account.
  depends_on = [azurerm_cognitive_deployment.chat]
}

locals {
  foundry_endpoint = "https://${azurerm_cognitive_account.foundry.custom_subdomain_name}.services.ai.azure.com"
}
