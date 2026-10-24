# Each product's stack (and CreatorPantry's IaC) looks these up as existing resources by name.
output "resource_group_name" {
  value = azurerm_resource_group.platform.name
}

output "container_app_environment_name" {
  value = azurerm_container_app_environment.platform.name
}

output "container_app_environment_default_domain" {
  value = azurerm_container_app_environment.platform.default_domain
}

output "virtual_network_name" {
  value = azurerm_virtual_network.platform.name
}

output "container_apps_subnet_cidr" {
  description = "The apps' trusted-proxy setting names this range: the environment's ingress proxy connects from it."
  value       = var.infrastructure_subnet_cidr
}

output "front_door_profile_name" {
  value = azurerm_cdn_frontdoor_profile.platform.name
}

output "front_door_id" {
  description = "The value Front Door sends in X-Azure-FDID; PP9.3's origin check compares against it."
  value       = azurerm_cdn_frontdoor_profile.platform.resource_guid
}

output "log_analytics_workspace_name" {
  value = azurerm_log_analytics_workspace.platform.name
}

output "application_insights_name" {
  value = azurerm_application_insights.platform.name
}
