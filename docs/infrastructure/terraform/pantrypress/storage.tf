# PantryPress's own storage account (requirements 5.1, architecture 8.3). Entra ID only - no account keys exist to leak.

resource "azurerm_storage_account" "pantrypress" {
  name                            = local.storage_name
  resource_group_name             = azurerm_resource_group.pantrypress.name
  location                        = azurerm_resource_group.pantrypress.location
  account_kind                    = "StorageV2"
  account_tier                    = "Standard"
  account_replication_type        = var.storage_replication
  access_tier                     = "Hot"
  min_tls_version                 = "TLS1_2"
  shared_access_key_enabled       = false
  default_to_oauth_authentication = true
  allow_nested_items_to_be_public = false
  public_network_access           = "Enabled"
  tags                            = local.tags

  # Soft delete and versioning (OPS-008). The management policy below keeps versioning from doubling the bill.
  blob_properties {
    versioning_enabled = true

    delete_retention_policy {
      days = local.env == "prod" ? 30 : 7
    }

    container_delete_retention_policy {
      days = local.env == "prod" ? 30 : 7
    }
  }

  network_rules {
    default_action = local.network_default
    bypass         = ["AzureServices"]
    ip_rules       = local.operator_ips
  }
}

# media-public is private for now as well: it is served "through Front Door only", and that origin and route are
# still open item O-04. Front Door Standard cannot use Private Link, so when O-04 lands the account (or a separate
# public-media account) must admit Front Door - decide that there.
resource "azurerm_storage_container" "pantrypress" {
  for_each = toset(["media-originals", "media-public", "pagedata", "data-protection"])

  name                  = each.value
  storage_account_id    = azurerm_storage_account.pantrypress.id
  container_access_type = "private"
}

resource "azurerm_storage_management_policy" "pantrypress" {
  storage_account_id = azurerm_storage_account.pantrypress.id

  rule {
    name    = "expire-old-versions"
    enabled = true

    filters {
      blob_types = ["blockBlob"]
    }

    actions {
      version {
        delete_after_days_since_creation = local.env == "prod" ? 30 : 7
      }
    }
  }
}

locals {
  # ApiService stages uploads, the Worker builds derivatives and page data, the Site API only reads page data.
  blob_access = {
    apiservice = "Storage Blob Data Contributor"
    worker     = "Storage Blob Data Contributor"
    site       = "Storage Blob Data Reader"
  }
}

resource "azurerm_role_assignment" "blob_access" {
  for_each = local.blob_access

  scope                = azurerm_storage_account.pantrypress.id
  role_definition_name = each.value
  principal_id         = azurerm_user_assigned_identity.app[each.key].principal_id
}

resource "azurerm_role_assignment" "data_protection_blob" {
  for_each = toset(["gateway", "apiservice"])

  scope                = azurerm_storage_container.pantrypress["data-protection"].id
  role_definition_name = "Storage Blob Data Contributor"
  principal_id         = azurerm_user_assigned_identity.app[each.key].principal_id
}
