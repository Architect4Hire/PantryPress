# PantryPress's own logical server (ADR 0016). Entra ID only: there is no SQL login or password anywhere. Apps connect
# as their managed identities; the grant script (outputs.tf) creates those database users once.
#
# DTU tiers, not serverless: the Worker's outbox dispatcher (PP1.10) touches the database all day, so serverless never
# pauses and bills its minimum vCores around the clock - several times the cost of a small DTU database.

resource "azurerm_mssql_server" "pantrypress" {
  name                          = local.sql_server_name
  resource_group_name           = azurerm_resource_group.pantrypress.name
  location                      = azurerm_resource_group.pantrypress.location
  version                       = "12.0"
  minimum_tls_version           = "1.2"
  public_network_access_enabled = true
  tags                          = local.tags

  azuread_administrator {
    login_username              = var.sql.entra_admin_login
    object_id                   = coalesce(var.sql.entra_admin_object_id, data.azurerm_client_config.current.object_id)
    azuread_authentication_only = true
  }
}

resource "azurerm_mssql_database" "pantrypress" {
  name                 = "pantrypress"
  server_id            = azurerm_mssql_server.pantrypress.id
  sku_name             = var.sql.sku_name
  max_size_gb          = var.sql.max_size_gb
  storage_account_type = var.sql.backup_redundancy
  zone_redundant       = false
  tags                 = local.tags

  # Point-in-time restore window (OPS-008: 35 days in prod).
  short_term_retention_policy {
    retention_days = var.sql.backup_retention_days
  }
}

# Without private endpoints (dev), apps reach the public endpoint from Azure. "Allow Azure services" is broad, but every
# connection still has to authenticate as an Entra identity this server knows.
resource "azurerm_mssql_firewall_rule" "azure_services" {
  count = local.allow_azure_sql ? 1 : 0

  name             = "AllowAzureServices"
  server_id        = azurerm_mssql_server.pantrypress.id
  start_ip_address = "0.0.0.0"
  end_ip_address   = "0.0.0.0"
}

resource "azurerm_mssql_firewall_rule" "operator" {
  for_each = toset(var.operator_ip_cidrs)

  name             = "operator-${replace(replace(each.value, ".", "-"), "/", "_")}"
  server_id        = azurerm_mssql_server.pantrypress.id
  start_ip_address = cidrhost(each.value, 0)
  end_ip_address   = cidrhost(each.value, -1)
}
