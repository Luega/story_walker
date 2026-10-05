# The generation ingestion queue (ADR-0003). Its properties mirror docker/servicebus/Config.json:
# the stale-story sweep (ADR-0031) and the dead-letter settler (ADR-0032) are written against a
# one-hour TTL, ten deliveries and dead-lettering on expiry, so change them together or not at all.

resource "azurerm_servicebus_namespace" "main" {
  name                = "sb-${local.name}-${random_string.suffix.result}"
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location
  sku                 = var.servicebus_sku
  minimum_tls_version = "1.2"
  tags                = local.tags
}

resource "azurerm_servicebus_queue" "ingestion" {
  # QueueNames.Ingestion in StoryWalker.Application.
  name         = "story-generation"
  namespace_id = azurerm_servicebus_namespace.main.id

  default_message_ttl                  = "PT1H"
  dead_lettering_on_message_expiration = true
  max_delivery_count                   = 10
  lock_duration                        = "PT1M"
  requires_duplicate_detection         = false
  requires_session                     = false
}

# The API builds its ServiceBusClient from a connection string (AddServiceBusPublishing), so it
# gets one — scoped to sending and nothing else. The worker uses its managed identity instead (see
# functions.tf), which the Functions host supports without any code change.
resource "azurerm_servicebus_namespace_authorization_rule" "api_send" {
  name         = "api-send"
  namespace_id = azurerm_servicebus_namespace.main.id

  send   = true
  listen = false
  manage = false
}
