# PantryPress's own DNS zone (ADR 0013, PP9.1a). Front Door validates the custom domains through the _dnsauth records,
# and the CNAMEs point every blog and the admin app at their endpoints. The zone's apex is left alone: it is for the
# marketing site, not for this stack.
#
# If the zone is a child of another one (dev.pantrypress.app under pantrypress.app), add NS records for it in the
# parent from the name_servers output. If it is a new registrable domain, set those name servers at the registrar.

resource "azurerm_dns_zone" "pantrypress" {
  name                = var.dns_zone_name
  resource_group_name = azurerm_resource_group.pantrypress.name
  tags                = local.tags
}

resource "azurerm_dns_txt_record" "front_door_validation" {
  for_each = {
    # The wildcard's token lives at the zone itself; a subdomain's under its own label.
    "_dnsauth"     = azurerm_cdn_frontdoor_custom_domain.sites.validation_token
    "_dnsauth.app" = azurerm_cdn_frontdoor_custom_domain.admin.validation_token
  }

  name                = each.key
  zone_name           = azurerm_dns_zone.pantrypress.name
  resource_group_name = azurerm_resource_group.pantrypress.name
  ttl                 = 3600

  record {
    value = each.value
  }
}

# The CNAMEs wait for the routes: a domain pointed at an endpoint before it is associated would answer with Front
# Door's not-found page.
resource "azurerm_dns_cname_record" "sites" {
  name                = "*"
  zone_name           = azurerm_dns_zone.pantrypress.name
  resource_group_name = azurerm_resource_group.pantrypress.name
  ttl                 = 3600
  record              = azurerm_cdn_frontdoor_endpoint.sites.host_name

  depends_on = [azurerm_cdn_frontdoor_route.pages, azurerm_cdn_frontdoor_route.site_api]
}

resource "azurerm_dns_cname_record" "admin" {
  name                = "app"
  zone_name           = azurerm_dns_zone.pantrypress.name
  resource_group_name = azurerm_resource_group.pantrypress.name
  ttl                 = 3600
  record              = azurerm_cdn_frontdoor_endpoint.admin.host_name

  depends_on = [azurerm_cdn_frontdoor_route.admin]
}
