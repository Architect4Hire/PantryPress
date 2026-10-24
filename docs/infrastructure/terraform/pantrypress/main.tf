# PantryPress's own resources for one environment (ADR 0016): everything that holds product data or answers product
# traffic. The shared platform is looked up in data.tf and never changed here, except for this product's own subnet,
# Front Door endpoints, and records in its own DNS zone.

resource "azurerm_resource_group" "pantrypress" {
  name     = "rg-pantrypress-${local.env}"
  location = var.location
  tags     = local.tags
}

# One identity per app (ADR 0016: separate apps and managed identities), so each gets only the access it needs.
resource "azurerm_user_assigned_identity" "app" {
  for_each = local.identity_names

  name                = "id-pp-${each.key}-${local.env}"
  resource_group_name = azurerm_resource_group.pantrypress.name
  location            = azurerm_resource_group.pantrypress.location
  tags                = local.tags
}

resource "azurerm_role_assignment" "acr_pull" {
  for_each = local.identity_names

  scope                = data.azurerm_container_registry.images.id
  role_definition_name = "AcrPull"
  principal_id         = azurerm_user_assigned_identity.app[each.key].principal_id
}

# Role assignments take a minute or two to be honored. Container Apps checks Key Vault references when a revision is
# created, so the apps wait out the propagation rather than fail on the first apply.
resource "time_sleep" "rbac_propagation" {
  create_duration = "90s"

  depends_on = [
    azurerm_role_assignment.acr_pull,
    azurerm_role_assignment.secret_reader,
    azurerm_role_assignment.blob_access,
    azurerm_role_assignment.data_protection_blob,
    azurerm_role_assignment.data_protection_key,
  ]
}

# Prod's irreplaceable data cannot be deleted by a stray destroy. Remove the lock deliberately, never by default.
resource "azurerm_management_lock" "sql" {
  count = local.env == "prod" ? 1 : 0

  name       = "no-delete"
  scope      = azurerm_mssql_server.pantrypress.id
  lock_level = "CanNotDelete"
  notes      = "Holds every site's content. Restore drills use point-in-time restore, not delete."
}

resource "azurerm_management_lock" "storage" {
  count = local.env == "prod" ? 1 : 0

  name       = "no-delete"
  scope      = azurerm_storage_account.pantrypress.id
  lock_level = "CanNotDelete"
  notes      = "Holds media originals, which cannot be rebuilt."
}
