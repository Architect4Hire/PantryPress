# Everything this stack uses but does not own: the shared platform (ADR 0016) and the bootstrap registry.

data "azurerm_client_config" "current" {}

data "azurerm_resource_group" "platform" {
  name = local.platform.resource_group_name
}

data "azurerm_container_app_environment" "platform" {
  name                = local.platform.container_app_environment_name
  resource_group_name = local.platform.resource_group_name
}

data "azurerm_cdn_frontdoor_profile" "platform" {
  name                = local.platform.front_door_profile_name
  resource_group_name = local.platform.resource_group_name
}

data "azurerm_virtual_network" "platform" {
  name                = local.platform.virtual_network_name
  resource_group_name = local.platform.resource_group_name
}

data "azurerm_container_registry" "images" {
  name                = var.container_registry.name
  resource_group_name = var.container_registry.resource_group_name
}

data "azurerm_private_dns_zone" "platform" {
  for_each = var.private_endpoints_enabled ? local.private_dns_zone_names : {}

  name                = each.value
  resource_group_name = local.platform.resource_group_name
}

# The addresses Front Door connects to origins from. Every externally reachable app admits only these (plus the
# environment's own subnet where another app calls it), because on an external workload-profile environment inbound
# traffic arrives on a public IP that an NSG cannot filter. The list changes from time to time: re-apply to pick it up.
data "azurerm_network_service_tags" "front_door" {
  location = var.location
  service  = "AzureFrontDoor.Backend"
}
