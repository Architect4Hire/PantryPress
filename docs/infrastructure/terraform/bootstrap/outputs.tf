output "backend_hcl" {
  description = "Paste into ../backend.hcl. run.ps1 adds the per-stack key."
  value       = <<-EOT
    storage_account_name = "${azurerm_storage_account.state.name}"
    container_name       = "${azurerm_storage_container.state.name}"
    use_azuread_auth     = true
  EOT
}

output "container_registry_login_server" {
  value = azurerm_container_registry.images.login_server
}

output "container_registry_name" {
  value = azurerm_container_registry.images.name
}

output "container_registry_resource_group" {
  value = azurerm_resource_group.state.name
}
