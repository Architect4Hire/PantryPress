# PantryPress's own Key Vault (ADR 0016). RBAC only; each app can read exactly the secrets it is handed.

resource "azurerm_key_vault" "pantrypress" {
  name                          = local.key_vault_name
  resource_group_name           = azurerm_resource_group.pantrypress.name
  location                      = azurerm_resource_group.pantrypress.location
  tenant_id                     = data.azurerm_client_config.current.tenant_id
  sku_name                      = "standard"
  rbac_authorization_enabled    = true
  purge_protection_enabled      = local.env == "prod"
  soft_delete_retention_days    = local.env == "prod" ? 90 : 7
  public_network_access_enabled = true
  tags                          = local.tags

  # With private endpoints on, only the endpoint and the operator's own addresses get in. Secrets are data-plane
  # writes, so whoever runs Terraform must be in operator_ip_cidrs.
  network_acls {
    bypass         = "AzureServices"
    default_action = local.network_default
    ip_rules       = var.operator_ip_cidrs
  }
}

resource "azurerm_role_assignment" "deployer_key_vault" {
  scope                = azurerm_key_vault.pantrypress.id
  role_definition_name = "Key Vault Administrator"
  principal_id         = data.azurerm_client_config.current.object_id
}

resource "time_sleep" "deployer_key_vault" {
  create_duration = "60s"
  depends_on      = [azurerm_role_assignment.deployer_key_vault]
}

# The secret the gateway proves itself to ApiService with (PP1.6). Generated during apply and written straight to Key
# Vault as a write-only value: it is never in Terraform state. Bump gateway_secret_version to rotate it.
ephemeral "random_password" "gateway_secret" {
  length  = 64
  special = false
}

resource "azurerm_key_vault_secret" "gateway_secret" {
  name             = "gateway-identity-secret"
  key_vault_id     = azurerm_key_vault.pantrypress.id
  value_wo         = ephemeral.random_password.gateway_secret.result
  value_wo_version = var.gateway_secret_version
  content_type     = "shared secret"

  depends_on = [time_sleep.deployer_key_vault]
}

# Who holds PlatformAdmin (ACCT-005), read only by the migration job. Unset, it is ";" - an empty list, which the
# reconciler treats as "no opinion" and leaves platform roles exactly as they are, so a missing value never locks the
# operators out.
resource "azurerm_key_vault_secret" "platform_admins" {
  name             = "platform-admins"
  key_vault_id     = azurerm_key_vault.pantrypress.id
  value_wo         = coalesce(var.platform_admins, ";")
  value_wo_version = var.platform_admins_version
  content_type     = "email list"

  depends_on = [time_sleep.deployer_key_vault]
}

resource "azurerm_key_vault_secret" "redis_connection" {
  name         = "redis-connection"
  key_vault_id = azurerm_key_vault.pantrypress.id
  value = join(",", [
    "${azurerm_managed_redis.pantrypress.hostname}:${azurerm_managed_redis.pantrypress.default_database[0].port}",
    "password=${azurerm_managed_redis.pantrypress.default_database[0].primary_access_key}",
    "ssl=True",
    "abortConnect=False",
  ])
  content_type = "StackExchange.Redis connection string"

  depends_on = [time_sleep.deployer_key_vault]
}

# Protects ApiService's data-protection key ring once PP19 persists it to its blob container (PP1.3b); the Worker reads
# it too, because it makes the links in account email (PP1.10b). Until the code uses it, it costs nothing and changes
# nothing.
resource "azurerm_key_vault_key" "data_protection" {
  name         = "data-protection"
  key_vault_id = azurerm_key_vault.pantrypress.id
  key_type     = "RSA"
  key_size     = 2048
  key_opts     = ["wrapKey", "unwrapKey"]

  depends_on = [time_sleep.deployer_key_vault]
}

# The gateway's own: its session tickets and antiforgery tokens (PP1.6, PP1.7), kept apart from the ring that signs
# reset links (PP1.12, storage.tf).
resource "azurerm_key_vault_key" "data_protection_gateway" {
  name         = "data-protection-gateway"
  key_vault_id = azurerm_key_vault.pantrypress.id
  key_type     = "RSA"
  key_size     = 2048
  key_opts     = ["wrapKey", "unwrapKey"]

  depends_on = [time_sleep.deployer_key_vault]
}

locals {
  # Which app reads which secret. Scoped to the secret, not the vault.
  secret_readers = merge(
    { for app in ["gateway", "apiservice"] : "gateway-secret-${app}" => { app = app, secret = azurerm_key_vault_secret.gateway_secret } },
    { for app in ["apiservice", "site", "worker"] : "redis-${app}" => { app = app, secret = azurerm_key_vault_secret.redis_connection } },
    { "platform-admins-migrations" = { app = "migrations", secret = azurerm_key_vault_secret.platform_admins } },
  )
}

resource "azurerm_role_assignment" "secret_reader" {
  for_each = local.secret_readers

  scope                = each.value.secret.resource_versionless_id
  role_definition_name = "Key Vault Secrets User"
  principal_id         = azurerm_user_assigned_identity.app[each.value.app].principal_id
}

resource "azurerm_role_assignment" "data_protection_key" {
  for_each = {
    gateway    = azurerm_key_vault_key.data_protection_gateway.resource_versionless_id
    apiservice = azurerm_key_vault_key.data_protection.resource_versionless_id
    worker     = azurerm_key_vault_key.data_protection.resource_versionless_id
  }

  scope                = each.value
  role_definition_name = "Key Vault Crypto Service Encryption User"
  principal_id         = azurerm_user_assigned_identity.app[each.key].principal_id
}
