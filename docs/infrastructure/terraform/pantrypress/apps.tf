# The long-running apps and the migration job, on the shared Consumption environment.
#
# Probes are TCP for now: /health and /alive are mapped only in Development (ServiceDefaults), so an HTTP probe would
# get a 404 in Staging and Production. Switch to HTTP probes when PP19.2 maps them for Container Apps.
#
# Images come from var.images. Changing a tag and applying is the deployment: Terraform stays the one description of
# what runs. Run the migration job first whenever the schema changed (OPS-003; see the runbook).

resource "azurerm_container_app" "app" {
  for_each = local.apps

  name                         = local.app_names[each.key]
  resource_group_name          = azurerm_resource_group.pantrypress.name
  container_app_environment_id = data.azurerm_container_app_environment.platform.id
  revision_mode                = "Single"
  workload_profile_name        = "Consumption"
  tags                         = local.tags

  identity {
    type         = "UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.app[each.key].id]
  }

  registry {
    server   = data.azurerm_container_registry.images.login_server
    identity = azurerm_user_assigned_identity.app[each.key].id
  }

  dynamic "secret" {
    for_each = each.value.secrets

    content {
      name                = secret.key
      key_vault_secret_id = secret.value
      identity            = azurerm_user_assigned_identity.app[each.key].id
    }
  }

  dynamic "ingress" {
    for_each = each.value.ingress == null ? [] : [each.value.ingress]

    content {
      external_enabled           = ingress.value.external
      allow_insecure_connections = ingress.value.allow_insecure
      target_port                = local.container_port
      transport                  = "auto"

      traffic_weight {
        latest_revision = true
        percentage      = 100
      }

      # Allow rules only: anything not listed is refused at the environment's edge, before it reaches the app.
      dynamic "ip_security_restriction" {
        for_each = { for i, cidr in ingress.value.allowed_cidrs : i => cidr }

        content {
          name             = "allow-${ip_security_restriction.key}"
          action           = "Allow"
          ip_address_range = ip_security_restriction.value
          description      = ip_security_restriction.value == local.platform.container_apps_subnet_cidr ? "Apps in this environment" : "Azure Front Door"
        }
      }
    }
  }

  template {
    min_replicas = var.app_scale[each.key].min_replicas
    max_replicas = var.app_scale[each.key].max_replicas

    container {
      name   = each.key
      image  = var.images[each.key]
      cpu    = var.app_scale[each.key].cpu
      memory = var.app_scale[each.key].memory

      dynamic "env" {
        for_each = merge(local.base_env, each.value.env, {
          OTEL_SERVICE_NAME = "pantrypress-${each.key}"
          AZURE_CLIENT_ID   = azurerm_user_assigned_identity.app[each.key].client_id
        })

        content {
          name  = env.key
          value = env.value
        }
      }

      dynamic "env" {
        for_each = each.value.secret_env

        content {
          name        = env.key
          secret_name = env.value
        }
      }

      startup_probe {
        transport               = "TCP"
        port                    = local.container_port
        interval_seconds        = 5
        failure_count_threshold = 24
      }

      liveness_probe {
        transport = "TCP"
        port      = local.container_port
      }

      readiness_probe {
        transport = "TCP"
        port      = local.container_port
      }
    }
  }

  lifecycle {
    precondition {
      condition     = length(local.front_door_ipv4_cidrs) > 0
      error_message = "The AzureFrontDoor.Backend service tag returned no addresses; refusing to open ingress without a Front Door allow list."
    }
  }

  depends_on = [time_sleep.rbac_propagation]
}

# The migration service as a manually started job (OPS-003: migrations run explicitly before deployment, never at app
# startup). Start it with `az containerapp job start`; a non-zero exit fails the execution and nothing else moves.
resource "azurerm_container_app_job" "migrations" {
  name                         = local.app_names.migrations
  resource_group_name          = azurerm_resource_group.pantrypress.name
  location                     = azurerm_resource_group.pantrypress.location
  container_app_environment_id = data.azurerm_container_app_environment.platform.id
  workload_profile_name        = "Consumption"
  replica_timeout_in_seconds   = 1800
  replica_retry_limit          = 0
  tags                         = local.tags

  manual_trigger_config {
    parallelism              = 1
    replica_completion_count = 1
  }

  identity {
    type         = "UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.app["migrations"].id]
  }

  registry {
    server   = data.azurerm_container_registry.images.login_server
    identity = azurerm_user_assigned_identity.app["migrations"].id
  }

  secret {
    name                = "platform-admins"
    key_vault_secret_id = azurerm_key_vault_secret.platform_admins.versionless_id
    identity            = azurerm_user_assigned_identity.app["migrations"].id
  }

  template {
    container {
      name   = "migrations"
      image  = var.images["migrations"]
      cpu    = 0.5
      memory = "1Gi"

      dynamic "env" {
        for_each = merge(local.base_env, {
          "ConnectionStrings__pantrypress" = local.sql_connection["migrations"]
          OTEL_SERVICE_NAME                = "pantrypress-migrations"
          AZURE_CLIENT_ID                  = azurerm_user_assigned_identity.app["migrations"].client_id
        })

        content {
          name  = env.key
          value = env.value
        }
      }

      env {
        name        = "Platform__Admins"
        secret_name = "platform-admins"
      }
    }
  }

  depends_on = [time_sleep.rbac_propagation]
}
