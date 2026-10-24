# privatelink zones are shared by nature: a virtual network can link only one zone of a given name, so if each product
# created its own, the second would fail to link. They live here and both products register their endpoints in them.

locals {
  private_dns_zones = var.private_dns_zones_enabled ? toset([
    "privatelink.database.windows.net",
    "privatelink.blob.core.windows.net",
    "privatelink.vaultcore.azure.net",
    "privatelink.redis.azure.net",
  ]) : toset([])
}

resource "azurerm_private_dns_zone" "platform" {
  for_each = local.private_dns_zones

  name                = each.value
  resource_group_name = azurerm_resource_group.platform.name
  tags                = local.tags
}

resource "azurerm_private_dns_zone_virtual_network_link" "platform" {
  for_each = local.private_dns_zones

  name                 = "link-${azurerm_virtual_network.platform.name}"
  private_dns_zone_id  = azurerm_private_dns_zone.platform[each.key].id
  virtual_network_id   = azurerm_virtual_network.platform.id
  registration_enabled = false
  tags                 = local.tags
}
