# One Front Door Standard profile per environment, shared by the products (ADR 0013, ADR 0016). Each product adds its
# own endpoints, custom domains, routes, and WAF policies in its own stack. The profile is the only thing here with a
# fixed monthly fee ($35 base at the time of writing).
#
# Why not one profile for dev and prod together: Standard allows 10 endpoints and 100 custom domains per profile, and
# DOM-001's opt-in custom domains will want every one of them in prod. Dev also must never be able to break prod's edge.

resource "azurerm_cdn_frontdoor_profile" "platform" {
  name                     = "afd-platform-${local.suffix}"
  resource_group_name      = azurerm_resource_group.platform.name
  sku_name                 = "Standard_AzureFrontDoor"
  response_timeout_seconds = 60
  tags                     = local.tags
}
