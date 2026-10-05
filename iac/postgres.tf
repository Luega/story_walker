# The application database (ADR-0018). Plain PostgreSQL: the pgvector columns went with ADR-0019,
# so no extension needs allow-listing. The API applies the EF migrations on startup.

resource "random_password" "postgres_admin" {
  length  = 32
  special = false # it is embedded in a connection string; keep it free of ; = and quotes
}

resource "azurerm_postgresql_flexible_server" "main" {
  name                = "psql-${local.name}-${random_string.suffix.result}"
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location

  version    = "17"
  sku_name   = var.postgres_sku
  storage_mb = var.postgres_storage_mb

  backup_retention_days        = var.postgres_backup_retention_days
  geo_redundant_backup_enabled = false

  # Npgsql authenticates with a password today. Entra ID authentication would remove the secret,
  # but needs a token provider in AddInfrastructure first.
  administrator_login    = "storywalker"
  administrator_password = random_password.postgres_admin.result

  authentication {
    password_auth_enabled         = true
    active_directory_auth_enabled = false
  }

  # No VNet: public access, closed by the firewall below to Azure-hosted callers.
  public_network_access_enabled = true

  tags = local.tags

  lifecycle {
    # Azure picks a zone when none is given; without this every plan would want to move it back.
    ignore_changes = [zone]
  }
}

resource "azurerm_postgresql_flexible_server_database" "main" {
  name      = "storywalker"
  server_id = azurerm_postgresql_flexible_server.main.id
  charset   = "UTF8"
  collation = "en_US.utf8"

  lifecycle {
    prevent_destroy = true
  }
}

# 0.0.0.0–0.0.0.0 is Azure's convention for "any Azure service". Neither Container Apps on the
# consumption profile nor Flex Consumption without VNet integration has a fixed outbound address to
# list instead. It admits other tenants' Azure resources too; the password is what keeps them out.
resource "azurerm_postgresql_flexible_server_firewall_rule" "azure_services" {
  name             = "allow-azure-services"
  server_id        = azurerm_postgresql_flexible_server.main.id
  start_ip_address = "0.0.0.0"
  end_ip_address   = "0.0.0.0"
}

locals {
  postgres_connection_string = join(";", [
    "Host=${azurerm_postgresql_flexible_server.main.fqdn}",
    "Port=5432",
    "Database=${azurerm_postgresql_flexible_server_database.main.name}",
    "Username=${azurerm_postgresql_flexible_server.main.administrator_login}",
    "Password=${random_password.postgres_admin.result}",
    "Ssl Mode=Require",
  ])
}
