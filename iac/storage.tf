# Two storage accounts, on purpose: the page images are public (the browser opens them straight
# from Blob), the worker's host storage is not. Keeping them apart means allowing anonymous blob
# access on one account cannot expose the other's leases, timers and deployment packages.

# --- Page images (ADR-0010) ---------------------------------------------------

resource "azurerm_storage_account" "images" {
  name                = "${local.compact}img"
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location

  account_tier             = "Standard"
  account_replication_type = "LRS"
  account_kind             = "StorageV2"
  min_tls_version          = "TLS1_2"

  # BlobImageStore creates the container with PublicAccessType.Blob: anyone with an image URL can
  # read it, nobody can list the container. The account has to permit that.
  allow_nested_items_to_be_public = true

  # The worker builds its BlobServiceClient from a connection string (AddImageStorage).
  shared_access_key_enabled = true

  tags = local.tags
}

# Created here too, so its access level is declared rather than left to whichever worker run
# happens to create it first. CreateIfNotExistsAsync is then a no-op.
resource "azurerm_storage_container" "images" {
  name                  = "story-images"
  storage_account_id    = azurerm_storage_account.images.id
  container_access_type = "blob"
}

# --- Worker host storage (ADR-0020) -------------------------------------------
# AzureWebJobsStorage (timer schedule and singleton lock, ADR-0031) and the Flex Consumption
# deployment package.

resource "azurerm_storage_account" "functions" {
  name                = "${local.compact}fn"
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location

  account_tier             = "Standard"
  account_replication_type = "LRS"
  account_kind             = "StorageV2"
  min_tls_version          = "TLS1_2"

  allow_nested_items_to_be_public = false

  tags = local.tags
}

resource "azurerm_storage_container" "functions_deployment" {
  name                  = "deployment"
  storage_account_id    = azurerm_storage_account.functions.id
  container_access_type = "private"
}
