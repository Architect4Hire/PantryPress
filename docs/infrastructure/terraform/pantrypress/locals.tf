resource "random_string" "suffix" {
  length  = 4
  lower   = true
  upper   = false
  numeric = true
  special = false
}

locals {
  env    = var.environment
  suffix = random_string.suffix.result

  platform = {
    resource_group_name            = coalesce(var.platform.resource_group_name, "rg-platform-${local.env}")
    container_app_environment_name = coalesce(var.platform.container_app_environment_name, "cae-platform-${local.env}")
    front_door_profile_name        = coalesce(var.platform.front_door_profile_name, "afd-platform-${local.env}")
    virtual_network_name           = coalesce(var.platform.virtual_network_name, "vnet-platform-${local.env}")
    container_apps_subnet_cidr     = var.platform.container_apps_subnet_cidr
  }

  tags = merge(var.tags, {
    product     = "pantrypress"
    environment = local.env
    stack       = "pantrypress"
    managed-by  = "terraform"
  })

  # Hosts (ADR 0013). The admin app is one host; every blog is a single-level subdomain under the wildcard.
  admin_host      = "app.${var.dns_zone_name}"
  admin_origin    = "https://${local.admin_host}"
  email_domain    = "${var.email_subdomain}.${var.dns_zone_name}"
  aspnet_env      = local.env == "prod" ? "Production" : "Staging"
  sql_server_name = "sql-pantrypress-${local.env}-${local.suffix}"
  key_vault_name  = "kv-pp-${local.env}-${local.suffix}"
  storage_name    = "stpp${local.env}${local.suffix}"
  redis_name      = "redis-pantrypress-${local.env}-${local.suffix}"
  operator_ips    = [for cidr in var.operator_ip_cidrs : trimsuffix(cidr, "/32")]
  allow_azure_sql = !var.private_endpoints_enabled
  network_default = var.private_endpoints_enabled ? "Deny" : "Allow"
  container_port  = 8080
  identity_names  = toset(["gateway", "apiservice", "site", "site-ssr", "worker", "migrations"])
  private_dns_zone_names = {
    sql   = "privatelink.database.windows.net"
    blob  = "privatelink.blob.core.windows.net"
    vault = "privatelink.vaultcore.azure.net"
    redis = "privatelink.redis.azure.net"
  }

  # Container app names are prefixed because the environment is shared with CreatorPantry, and an app's name is its
  # DNS name inside the environment. Aspire's service discovery is pointed at these names with services__* below, so
  # the gateway's YARP destination "http://apiservice" still resolves.
  app_names = {
    gateway    = "pp-gateway"
    apiservice = "pp-apiservice"
    site       = "pp-site"
    site-ssr   = "pp-site-ssr"
    worker     = "pp-worker"
    migrations = "pp-migrations"
  }

  front_door_ipv4_cidrs = data.azurerm_network_service_tags.front_door.ipv4_cidrs

  # Who may say who the client is (Edge:TrustedProxies, PP1.8). Behind Front Door into Container Apps there are two
  # hops: Front Door adds the browser's address to X-Forwarded-For, then the environment's ingress proxy - which
  # connects from the environment subnet - appends Front Door's. ForwardLimit 2 believes exactly those two, and only
  # when each hop is in this list. Front Door's identity is still checked separately with X-Azure-FDID (PP9.3).
  trusted_proxy_networks = concat(
    [local.platform.container_apps_subnet_cidr],
    data.azurerm_network_service_tags.front_door.ipv4_cidrs,
    data.azurerm_network_service_tags.front_door.ipv6_cidrs,
  )
  trusted_proxy_env = merge(
    { "Edge__TrustedProxies__ForwardLimit" = "2" },
    { for i, cidr in local.trusted_proxy_networks : "Edge__TrustedProxies__KnownNetworks__${i}" => cidr },
  )

  # SQL uses Entra ID only (no SQL logins, no password anywhere): each app connects as its own managed identity, which
  # the grant script turns into a database user.
  sql_connection = {
    for k in ["apiservice", "site", "worker", "migrations"] : k => join(";", [
      "Server=tcp:${azurerm_mssql_server.pantrypress.fully_qualified_domain_name},1433",
      "Database=${azurerm_mssql_database.pantrypress.name}",
      "Authentication=Active Directory Managed Identity",
      "User Id=${azurerm_user_assigned_identity.app[k].client_id}",
      "Encrypt=True",
      "TrustServerCertificate=False",
    ])
  }

  # Blob access is by managed identity; the connection string is just the service address (Aspire's blob client reads
  # a URI as "use DefaultAzureCredential", which picks up AZURE_CLIENT_ID).
  blob_endpoint = azurerm_storage_account.pantrypress.primary_blob_endpoint

  # What every app gets. OTEL_SERVICE_NAME is the cloud role name in the shared workspace (ADR 0016).
  base_env = {
    ASPNETCORE_ENVIRONMENT   = local.aspnet_env
    DOTNET_ENVIRONMENT       = local.aspnet_env
    OTEL_RESOURCE_ATTRIBUTES = "service.namespace=pantrypress,deployment.environment.name=${local.env}"
  }

  # The apps. Keep env in step with AppHost.cs: a reference added there needs its counterpart here.
  #   ingress.external      - reachable from outside the environment (only Front Door is let through)
  #   ingress.allow_insecure - accepts plain HTTP from other apps in the environment
  apps = {
    gateway = {
      ingress = {
        external       = true
        allow_insecure = false
        allowed_cidrs  = local.front_door_ipv4_cidrs
      }
      env = merge(local.trusted_proxy_env, {
        "Gateway__Hosts__0"             = local.admin_host
        "Gateway__PublicOrigin"         = local.admin_origin
        "services__apiservice__http__0" = "http://${local.app_names.apiservice}"
        # The redirect needs to know where HTTPS lives once a proxy is trusted (PP1.8 note for PP19).
        "HTTPS_PORT" = "443"
      })
      secret_env = { "Gateway__SharedSecret" = "gateway-secret" }
      secrets    = { "gateway-secret" = azurerm_key_vault_secret.gateway_secret.versionless_id }
    }

    apiservice = {
      # Internal only until the publishing API (PP18, O-01) and the Stripe webhook (PP14.5, O-02) get an edge route.
      ingress = {
        external       = false
        allow_insecure = true
        allowed_cidrs  = []
      }
      env = {
        "ConnectionStrings__pantrypress" = local.sql_connection["apiservice"]
        "ConnectionStrings__blobs"       = local.blob_endpoint
      }
      secret_env = { "Gateway__SharedSecret" = "gateway-secret" }
      secrets    = { "gateway-secret" = azurerm_key_vault_secret.gateway_secret.versionless_id }
    }

    site = {
      # Front Door sends /api/public/* and the feed paths here; the SSR server calls it from inside the environment.
      ingress = {
        external       = true
        allow_insecure = true
        allowed_cidrs  = concat(local.front_door_ipv4_cidrs, [local.platform.container_apps_subnet_cidr])
      }
      env = merge(local.trusted_proxy_env, {
        "ConnectionStrings__pantrypress" = local.sql_connection["site"]
        "ConnectionStrings__blobs"       = local.blob_endpoint
      })
      secret_env = { "ConnectionStrings__redis" = "redis-connection" }
      secrets    = { "redis-connection" = azurerm_key_vault_secret.redis_connection.versionless_id }
    }

    site-ssr = {
      ingress = {
        external       = true
        allow_insecure = false
        allowed_cidrs  = local.front_door_ipv4_cidrs
      }
      env = {
        "PORT"                    = tostring(local.container_port)
        "NODE_ENV"                = "production"
        "services__site__http__0" = "http://${local.app_names.site}"
      }
      secret_env = {}
      secrets    = {}
    }

    worker = {
      ingress = null
      env = {
        "ConnectionStrings__pantrypress" = local.sql_connection["worker"]
        "ConnectionStrings__blobs"       = local.blob_endpoint
        # The Worker sends account email from the outbox (PP1.10b), so the links in it are written here.
        "Accounts__AppBaseUrl" = local.admin_origin
      }
      secret_env = { "ConnectionStrings__redis" = "redis-connection" }
      secrets    = { "redis-connection" = azurerm_key_vault_secret.redis_connection.versionless_id }
    }
  }
}
