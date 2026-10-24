# Private endpoints for SQL, Blob, Key Vault, and Redis (requirements 5.1: "where the environment supports them").
# Off in dev - four endpoints cost about $29 a month and dev holds no reader data. When on, the apps resolve each
# service to a private address through the platform's privatelink zones, and the public sides close to everything
# but the operator's addresses (Key Vault, storage, SQL) or entirely (Redis).

resource "azurerm_subnet" "private_endpoints" {
  count = var.private_endpoints_enabled ? 1 : 0

  name                              = "snet-pe-pantrypress"
  resource_group_name               = local.platform.resource_group_name
  virtual_network_name              = data.azurerm_virtual_network.platform.name
  address_prefixes                  = [var.private_endpoint_subnet_cidr]
  private_endpoint_network_policies = "Disabled"

  lifecycle {
    precondition {
      condition     = var.private_endpoint_subnet_cidr != null
      error_message = "private_endpoint_subnet_cidr is required when private endpoints are on."
    }
  }
}

locals {
  private_endpoints = var.private_endpoints_enabled ? {
    sql   = { resource_id = azurerm_mssql_server.pantrypress.id, subresource = "sqlServer" }
    blob  = { resource_id = azurerm_storage_account.pantrypress.id, subresource = "blob" }
    vault = { resource_id = azurerm_key_vault.pantrypress.id, subresource = "vault" }
    redis = { resource_id = azurerm_managed_redis.pantrypress.id, subresource = "redisEnterprise" }
  } : {}
}

resource "azurerm_private_endpoint" "pantrypress" {
  for_each = local.private_endpoints

  name                = "pe-pp-${each.key}-${local.env}"
  resource_group_name = azurerm_resource_group.pantrypress.name
  location            = azurerm_resource_group.pantrypress.location
  subnet_id           = azurerm_subnet.private_endpoints[0].id
  tags                = local.tags

  private_service_connection {
    name                           = "psc-pp-${each.key}-${local.env}"
    private_connection_resource_id = each.value.resource_id
    subresource_names              = [each.value.subresource]
    is_manual_connection           = false
  }

  private_dns_zone_group {
    name                 = "default"
    private_dns_zone_ids = [data.azurerm_private_dns_zone.platform[each.key].id]
  }
}
