# The API and the UI on Azure Container Apps. The API already ships as a container
# (apps/api/Dockerfile); the UI needs a Dockerfile of its own before ui_image can be set.
#
# Only the UI is reachable from the internet. The browser never calls the API directly — the UI's
# server-side route forwards /api/* with the Clerk token attached (ADR-0021) — so the API's ingress
# is internal to the environment.

resource "azurerm_container_registry" "main" {
  name                = "${local.compact}acr"
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location
  sku                 = "Basic"
  admin_enabled       = false
  tags                = local.tags
}

resource "azurerm_container_app_environment" "main" {
  name                       = "cae-${local.name}"
  resource_group_name        = azurerm_resource_group.main.name
  location                   = azurerm_resource_group.main.location
  log_analytics_workspace_id = azurerm_log_analytics_workspace.main.id
  tags                       = local.tags
}

# One user-assigned identity per app, created before the app so its AcrPull role is in place by the
# time the first revision pulls an image. A system-assigned identity would only exist after.
resource "azurerm_user_assigned_identity" "api" {
  name                = "id-${local.name}-api"
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location
  tags                = local.tags
}

resource "azurerm_user_assigned_identity" "ui" {
  name                = "id-${local.name}-ui"
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location
  tags                = local.tags
}

locals {
  # Until CI has pushed real images, both apps run Microsoft's quickstart image, which listens on 80.
  placeholder_image = "mcr.microsoft.com/k8se/quickstart:latest"

  api_name = "ca-${local.name}-api"
  ui_name  = "ca-${local.name}-ui"

  # Both addresses are known from the environment's domain before either app exists, which breaks
  # the cycle of the UI needing the API's URL and the API needing the UI's origin.
  api_internal_url = "https://${local.api_name}.internal.${azurerm_container_app_environment.main.default_domain}"
  ui_origin        = "https://${local.ui_name}.${azurerm_container_app_environment.main.default_domain}"
}

resource "azurerm_container_app" "api" {
  name                         = local.api_name
  resource_group_name          = azurerm_resource_group.main.name
  container_app_environment_id = azurerm_container_app_environment.main.id
  revision_mode                = "Single"

  identity {
    type         = "UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.api.id]
  }

  registry {
    server   = azurerm_container_registry.main.login_server
    identity = azurerm_user_assigned_identity.api.id
  }

  secret {
    name  = "postgres-connection-string"
    value = local.postgres_connection_string
  }

  secret {
    name  = "servicebus-connection-string"
    value = azurerm_servicebus_namespace_authorization_rule.api_send.primary_connection_string
  }

  secret {
    name  = "appinsights-connection-string"
    value = azurerm_application_insights.main.connection_string
  }

  ingress {
    external_enabled = false
    target_port      = var.api_image == null ? 80 : 8080
    transport        = "auto"

    traffic_weight {
      latest_revision = true
      percentage      = 100
    }
  }

  template {
    min_replicas = 0
    max_replicas = var.api_max_replicas

    container {
      name   = "api"
      image  = coalesce(var.api_image, local.placeholder_image)
      cpu    = 0.5
      memory = "1Gi"

      env {
        name        = "ConnectionStrings__Postgres"
        secret_name = "postgres-connection-string"
      }

      env {
        name        = "ServiceBus__ConnectionString"
        secret_name = "servicebus-connection-string"
      }

      env {
        name  = "Foundry__Endpoint"
        value = local.foundry_endpoint
      }

      env {
        name  = "Foundry__Deployment"
        value = azurerm_cognitive_deployment.chat.name
      }

      # DefaultAzureCredential needs to be told which user-assigned identity to use.
      env {
        name  = "AZURE_CLIENT_ID"
        value = azurerm_user_assigned_identity.api.client_id
      }

      env {
        name  = "Clerk__Authority"
        value = var.clerk_authority
      }

      env {
        name  = "Clerk__AuthorizedParties"
        value = join(",", concat([local.ui_origin], var.extra_authorized_parties))
      }

      env {
        name        = "APPLICATIONINSIGHTS_CONNECTION_STRING"
        secret_name = "appinsights-connection-string"
      }
    }
  }

  tags = local.tags

  depends_on = [azurerm_role_assignment.api_acr_pull]
}

resource "azurerm_container_app" "ui" {
  name                         = local.ui_name
  resource_group_name          = azurerm_resource_group.main.name
  container_app_environment_id = azurerm_container_app_environment.main.id
  revision_mode                = "Single"

  identity {
    type         = "UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.ui.id]
  }

  registry {
    server   = azurerm_container_registry.main.login_server
    identity = azurerm_user_assigned_identity.ui.id
  }

  secret {
    name  = "clerk-secret-key"
    value = var.clerk_secret_key
  }

  secret {
    name  = "appinsights-connection-string"
    value = azurerm_application_insights.main.connection_string
  }

  ingress {
    external_enabled = true
    target_port      = var.ui_image == null ? 80 : 3000
    transport        = "auto"

    traffic_weight {
      latest_revision = true
      percentage      = 100
    }
  }

  template {
    min_replicas = 0
    max_replicas = var.ui_max_replicas

    container {
      name   = "ui"
      image  = coalesce(var.ui_image, local.placeholder_image)
      cpu    = 0.5
      memory = "1Gi"

      env {
        name  = "API_URL"
        value = local.api_internal_url
      }

      # NEXT_PUBLIC_* values are inlined at `next build`; these runtime copies serve the server
      # side only. The image build must receive the same values as build arguments.
      env {
        name  = "NEXT_PUBLIC_CLERK_PUBLISHABLE_KEY"
        value = var.clerk_publishable_key
      }

      env {
        name        = "CLERK_SECRET_KEY"
        secret_name = "clerk-secret-key"
      }

      env {
        name  = "NEXT_PUBLIC_CLERK_SIGN_IN_URL"
        value = "/sign-in"
      }

      env {
        name  = "NEXT_PUBLIC_CLERK_SIGN_UP_URL"
        value = "/sign-up"
      }

      env {
        name        = "APPLICATIONINSIGHTS_CONNECTION_STRING"
        secret_name = "appinsights-connection-string"
      }
    }
  }

  tags = local.tags

  depends_on = [azurerm_role_assignment.ui_acr_pull]
}
