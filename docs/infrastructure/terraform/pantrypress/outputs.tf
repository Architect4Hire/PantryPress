output "admin_url" {
  value = local.admin_origin
}

output "blog_host_pattern" {
  value = "https://{slug}.${var.dns_zone_name}"
}

output "name_servers" {
  description = "Delegate the zone to these at the registrar, or with NS records in the parent zone."
  value       = azurerm_dns_zone.pantrypress.name_servers
}

output "front_door_endpoints" {
  value = {
    sites = azurerm_cdn_frontdoor_endpoint.sites.host_name
    admin = azurerm_cdn_frontdoor_endpoint.admin.host_name
  }
}

output "certificate_expiry" {
  description = "Managed certificates. The wildcard one does not renew by itself: re-apply ahead of this date."
  value = {
    sites = azurerm_cdn_frontdoor_custom_domain.sites.expiration_date
    admin = azurerm_cdn_frontdoor_custom_domain.admin.expiration_date
  }
}

output "front_door_id" {
  description = "X-Azure-FDID value for PP9.3's origin check."
  value       = data.azurerm_cdn_frontdoor_profile.platform.resource_guid
}

output "container_registry" {
  value = data.azurerm_container_registry.images.login_server
}

output "resource_group_name" {
  value = azurerm_resource_group.pantrypress.name
}

output "migration_job_name" {
  value = azurerm_container_app_job.migrations.name
}

output "sql_server" {
  value = azurerm_mssql_server.pantrypress.fully_qualified_domain_name
}

output "sql_grant_script" {
  description = "Run once after the first apply, signed in as the Entra admin (runbook, \"Database access\")."
  value = templatefile("${path.module}/sql/grant-access.sql.tftpl", {
    owners  = [azurerm_user_assigned_identity.app["migrations"].name]
    writers = [for k in ["apiservice", "site", "worker"] : azurerm_user_assigned_identity.app[k].name]
  })
}

output "email_domain" {
  value = var.email_enabled ? local.email_domain : null
}

output "email_verification_records" {
  description = "What Terraform wrote to the zone for the sending domain. Compare with the portal if verification fails."
  value       = var.email_enabled ? local.email_records : null
}

output "key_vault_name" {
  value = azurerm_key_vault.pantrypress.name
}
