# PantryPress's own Azure Managed Redis (requirements 5.1). It is a cache, never the source of truth: page data falls
# back to blob, and losing it costs warm-up time, not data. Hence no persistence and, in dev, no high availability.
# (Azure Cache for Redis is not an option for a new build: it no longer takes new instances.)

resource "azurerm_managed_redis" "pantrypress" {
  name                      = local.redis_name
  resource_group_name       = azurerm_resource_group.pantrypress.name
  location                  = azurerm_resource_group.pantrypress.location
  sku_name                  = var.redis.sku_name
  high_availability_enabled = var.redis.high_availability
  public_network_access     = var.private_endpoints_enabled ? "Disabled" : "Enabled"
  tags                      = local.tags

  default_database {
    # Access keys rather than Entra ID for now. Redis has had consumers since PP2.12 - the host map, which the public site
    # reads and ApiService refreshes - through one StackExchange.Redis connection string in Key Vault. The Worker's grant
    # waits for its first use (page data, PP7.12); the AppHost gives it no Redis until then. Moving
    # to Entra ID is a code change to ServiceDefaults' CacheConnection plus a data access policy per app; PP2.14 left it to
    # PP19's hardening, with the other secrets that become identities.
    access_keys_authentication_enabled = true
    client_protocol                    = "Encrypted"
    clustering_policy                  = "EnterpriseCluster"
    eviction_policy                    = "AllKeysLRU"
  }
}
