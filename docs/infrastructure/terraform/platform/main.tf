# The resources PantryPress shares with CreatorPantry (ADR 0016, PP-D16; D-16 default: owned here). One set per
# environment. Nothing in this stack is product data: no SQL, Redis, storage, Key Vault, or DNS zone for a product
# domain. Each product's own stack - and CreatorPantry's IaC - references these by name as existing resources.

locals {
  suffix = var.environment
  tags = merge(var.tags, {
    environment = var.environment
    stack       = "platform"
    shared-with = "creatorpantry"
    managed-by  = "terraform"
  })
}

resource "azurerm_resource_group" "platform" {
  name     = "rg-platform-${local.suffix}"
  location = var.location
  tags     = local.tags
}

# ---------------------------------------------------------------------------------------------------------------------
# Monitoring: one workspace, separate cloud role names per app (OTEL_SERVICE_NAME is set by each product's stack).
# ---------------------------------------------------------------------------------------------------------------------

resource "azurerm_log_analytics_workspace" "platform" {
  name                = "log-platform-${local.suffix}"
  resource_group_name = azurerm_resource_group.platform.name
  location            = azurerm_resource_group.platform.location
  sku                 = "PerGB2018"
  retention_in_days   = var.log_retention_days
  daily_quota_gb      = var.log_daily_quota_gb
  tags                = local.tags
}

resource "azurerm_application_insights" "platform" {
  name                = "appi-platform-${local.suffix}"
  resource_group_name = azurerm_resource_group.platform.name
  location            = azurerm_resource_group.platform.location
  workspace_id        = azurerm_log_analytics_workspace.platform.id
  application_type    = "web"
  tags                = local.tags
}

# ---------------------------------------------------------------------------------------------------------------------
# Network. A virtual network costs nothing. It gives the environment a known address range (the apps' trusted-proxy
# setting names it) and somewhere for private endpoints to live. Subnets are separate resources, never inline, so each
# product's stack can add its own without this stack seeing drift.
# ---------------------------------------------------------------------------------------------------------------------

resource "azurerm_virtual_network" "platform" {
  name                = "vnet-platform-${local.suffix}"
  resource_group_name = azurerm_resource_group.platform.name
  location            = azurerm_resource_group.platform.location
  address_space       = [var.vnet_address_space]
  tags                = local.tags
}

resource "azurerm_subnet" "container_apps" {
  name                 = "snet-container-apps"
  resource_group_name  = azurerm_resource_group.platform.name
  virtual_network_name = azurerm_virtual_network.platform.name
  address_prefixes     = [var.infrastructure_subnet_cidr]

  delegation {
    name = "container-apps"

    service_delegation {
      name    = "Microsoft.App/environments"
      actions = ["Microsoft.Network/virtualNetworks/subnets/join/action"]
    }
  }
}

# ---------------------------------------------------------------------------------------------------------------------
# Container Apps environment: workload profiles with the Consumption profile only. That has no fixed fee - the
# environment management meter applies only to Dedicated profiles, an environment private endpoint, or planned
# maintenance, and none of the three is used. Apps scale to zero where they can and pay per second when they run.
# ---------------------------------------------------------------------------------------------------------------------

resource "azurerm_container_app_environment" "platform" {
  name                               = "cae-platform-${local.suffix}"
  resource_group_name                = azurerm_resource_group.platform.name
  location                           = azurerm_resource_group.platform.location
  infrastructure_subnet_id           = azurerm_subnet.container_apps.id
  infrastructure_resource_group_name = "rg-platform-${local.suffix}-cae-managed"
  internal_load_balancer_enabled     = false
  zone_redundancy_enabled            = var.zone_redundant
  logs_destination                   = "log-analytics"
  log_analytics_workspace_id         = azurerm_log_analytics_workspace.platform.id
  tags                               = local.tags

  workload_profile {
    name                  = "Consumption"
    workload_profile_type = "Consumption"
  }
}

# The managed OpenTelemetry agent. Every app already exports OTLP when OTEL_EXPORTER_OTLP_ENDPOINT is set
# (ServiceDefaults), and the agent sets it, so traces and logs reach Application Insights with no code change and no
# instrumentation key in any app. azurerm does not model these two properties, hence the azapi patch.
#
# 2026-07-01 is the first stable API version with them (2026-01-01 does not have them). azapi 2.13's built-in schemas
# stop at 2026-01-01, so nothing checks this body before Azure does on apply.
resource "azapi_update_resource" "environment_telemetry" {
  type        = "Microsoft.App/managedEnvironments@2026-07-01"
  resource_id = azurerm_container_app_environment.platform.id

  body = {
    properties = {
      appInsightsConfiguration = {
        connectionString = azurerm_application_insights.platform.connection_string
      }
      openTelemetryConfiguration = {
        tracesConfiguration = {
          destinations = ["appInsights"]
        }
        logsConfiguration = {
          destinations = ["appInsights"]
        }
      }
    }
  }
}
