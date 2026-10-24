# Bootstrap: the once-per-subscription resources that every other stack depends on.
#
# - The storage account that holds every other stack's Terraform state. This stack cannot keep its own state there
#   (the account does not exist until this stack runs), so its state stays local. It holds no secrets; if it is lost,
#   re-import the four resources below rather than recreating them.
# - The container registry. One registry serves dev and prod, so the image that passed in dev is the exact image that
#   ships to prod, and the Basic tier's fixed fee is paid once.

terraform {
  required_version = ">= 1.11"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 5.10"
    }
  }
}

provider "azurerm" {
  features {}
  subscription_id     = var.subscription_id
  storage_use_azuread = true
}

data "azurerm_client_config" "current" {}

locals {
  tags = {
    product    = "pantrypress"
    stack      = "bootstrap"
    managed-by = "terraform"
  }
}

resource "azurerm_resource_group" "state" {
  name     = "rg-pantrypress-tfstate"
  location = var.location
  tags     = local.tags
}

# Entra ID only: no account keys, no public blobs. Versioning and soft delete keep a mistaken `terraform apply` or a
# corrupted state file recoverable.
resource "azurerm_storage_account" "state" {
  name                            = var.state_storage_account_name
  resource_group_name             = azurerm_resource_group.state.name
  location                        = azurerm_resource_group.state.location
  account_tier                    = "Standard"
  account_replication_type        = "LRS"
  min_tls_version                 = "TLS1_2"
  shared_access_key_enabled       = false
  default_to_oauth_authentication = true
  allow_nested_items_to_be_public = false
  tags                            = local.tags

  blob_properties {
    versioning_enabled = true

    delete_retention_policy {
      days = 30
    }

    container_delete_retention_policy {
      days = 30
    }
  }
}

resource "azurerm_storage_container" "state" {
  name                  = "tfstate"
  storage_account_id    = azurerm_storage_account.state.id
  container_access_type = "private"
}

# Whoever runs Terraform needs to read and write state blobs with their own Entra identity (`use_azuread_auth`).
resource "azurerm_role_assignment" "state_operator" {
  for_each = toset(concat([data.azurerm_client_config.current.object_id], var.additional_state_operator_object_ids))

  scope                = azurerm_storage_account.state.id
  role_definition_name = "Storage Blob Data Contributor"
  principal_id         = each.value
}

# Basic is the cheapest tier and enough for a handful of images. Admin user off: apps pull with their managed identity.
resource "azurerm_container_registry" "images" {
  name                = var.container_registry_name
  resource_group_name = azurerm_resource_group.state.name
  location            = azurerm_resource_group.state.location
  sku                 = "Basic"
  admin_enabled       = false
  tags                = local.tags
}

resource "azurerm_role_assignment" "registry_pusher" {
  for_each = toset(concat([data.azurerm_client_config.current.object_id], var.additional_state_operator_object_ids))

  scope                = azurerm_container_registry.images.id
  role_definition_name = "AcrPush"
  principal_id         = each.value
}
