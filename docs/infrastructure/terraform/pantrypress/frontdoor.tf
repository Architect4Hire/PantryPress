# PantryPress's endpoints, domains, routes, and WAF policies on the shared Front Door Standard profile (ADR 0013,
# PP9.2). Two endpoints - blogs and the admin app - each with its own WAF policy, so a rule tuned for readers never
# touches sign-in, and the reverse.
#
# Caching is deliberately off on every route until PP9.4 defines Cache-Control per response. With a cache block and no
# Cache-Control, Front Door caches for one to three days on its own, which would hide every publish until a purge.

resource "azurerm_cdn_frontdoor_endpoint" "sites" {
  name                     = "pp-sites-${local.env}"
  cdn_frontdoor_profile_id = data.azurerm_cdn_frontdoor_profile.platform.id
  tags                     = local.tags
}

resource "azurerm_cdn_frontdoor_endpoint" "admin" {
  name                     = "pp-app-${local.env}"
  cdn_frontdoor_profile_id = data.azurerm_cdn_frontdoor_profile.platform.id
  tags                     = local.tags
}

# --- Origins ------------------------------------------------------------------------------------------------------------
# One origin per group, so no health probe: Front Door's probes only choose between origins, and a lone origin's probes
# would just be paid-for requests. Front Door connects to each app's own Container Apps address; the browser's host
# arrives in X-Forwarded-Host, which PP9.3 teaches the public tier to trust from Front Door alone.

locals {
  origins = {
    ssr     = "site-ssr"
    site    = "site"
    gateway = "gateway"
  }
}

resource "azurerm_cdn_frontdoor_origin_group" "app" {
  for_each = local.origins

  name                     = "og-${each.key}"
  cdn_frontdoor_profile_id = data.azurerm_cdn_frontdoor_profile.platform.id
  session_affinity_enabled = false

  load_balancing {}
}

resource "azurerm_cdn_frontdoor_origin" "app" {
  for_each = local.origins

  name                           = "origin-${each.key}"
  cdn_frontdoor_origin_group_id  = azurerm_cdn_frontdoor_origin_group.app[each.key].id
  enabled                        = true
  host_name                      = azurerm_container_app.app[each.value].ingress[0].fqdn
  origin_host_header             = azurerm_container_app.app[each.value].ingress[0].fqdn
  https_port                     = 443
  http_port                      = 80
  certificate_name_check_enabled = true
  priority                       = 1
  weight                         = 1000
}

# --- Domains ------------------------------------------------------------------------------------------------------------
# Managed certificates for both. A managed wildcard certificate does not renew by itself: before it expires, re-apply so
# the _dnsauth record picks up the fresh validation token (runbook, "Certificates").

resource "azurerm_cdn_frontdoor_custom_domain" "sites" {
  name                     = "pp-sites-wildcard"
  cdn_frontdoor_profile_id = data.azurerm_cdn_frontdoor_profile.platform.id
  dns_zone_id              = azurerm_dns_zone.pantrypress.id
  host_name                = "*.${var.dns_zone_name}"

  tls {
    certificate_type = "ManagedCertificate"
    minimum_version  = "TLS12"
  }
}

resource "azurerm_cdn_frontdoor_custom_domain" "admin" {
  name                     = "pp-app"
  cdn_frontdoor_profile_id = data.azurerm_cdn_frontdoor_profile.platform.id
  dns_zone_id              = azurerm_dns_zone.pantrypress.id
  host_name                = local.admin_host

  tls {
    certificate_type = "ManagedCertificate"
    minimum_version  = "TLS12"
  }
}

# --- Routes -------------------------------------------------------------------------------------------------------------

resource "azurerm_cdn_frontdoor_route" "pages" {
  name                            = "route-pages"
  cdn_frontdoor_endpoint_id       = azurerm_cdn_frontdoor_endpoint.sites.id
  cdn_frontdoor_origin_group_id   = azurerm_cdn_frontdoor_origin_group.app["ssr"].id
  cdn_frontdoor_origin_ids        = [azurerm_cdn_frontdoor_origin.app["ssr"].id]
  cdn_frontdoor_custom_domain_ids = [azurerm_cdn_frontdoor_custom_domain.sites.id]
  patterns_to_match               = ["/*"]
  supported_protocols             = ["Http", "Https"]
  https_redirect_enabled          = true
  forwarding_protocol             = "HttpsOnly"
  link_to_default_domain          = false
}

resource "azurerm_cdn_frontdoor_route" "site_api" {
  name                            = "route-site-api"
  cdn_frontdoor_endpoint_id       = azurerm_cdn_frontdoor_endpoint.sites.id
  cdn_frontdoor_origin_group_id   = azurerm_cdn_frontdoor_origin_group.app["site"].id
  cdn_frontdoor_origin_ids        = [azurerm_cdn_frontdoor_origin.app["site"].id]
  cdn_frontdoor_custom_domain_ids = [azurerm_cdn_frontdoor_custom_domain.sites.id]
  patterns_to_match               = var.site_api_paths
  supported_protocols             = ["Http", "Https"]
  https_redirect_enabled          = true
  forwarding_protocol             = "HttpsOnly"
  link_to_default_domain          = false
}

resource "azurerm_cdn_frontdoor_route" "admin" {
  name                            = "route-admin"
  cdn_frontdoor_endpoint_id       = azurerm_cdn_frontdoor_endpoint.admin.id
  cdn_frontdoor_origin_group_id   = azurerm_cdn_frontdoor_origin_group.app["gateway"].id
  cdn_frontdoor_origin_ids        = [azurerm_cdn_frontdoor_origin.app["gateway"].id]
  cdn_frontdoor_custom_domain_ids = [azurerm_cdn_frontdoor_custom_domain.admin.id]
  patterns_to_match               = ["/*"]
  supported_protocols             = ["Http", "Https"]
  https_redirect_enabled          = true
  forwarding_protocol             = "HttpsOnly"
  link_to_default_domain          = false
}

# --- WAF (Standard: custom rules only; PP9.8 tunes them) ----------------------------------------------------------------

locals {
  waf_policies = var.waf.enabled ? {
    sites = azurerm_cdn_frontdoor_custom_domain.sites.id
    admin = azurerm_cdn_frontdoor_custom_domain.admin.id
  } : {}
}

resource "azurerm_cdn_frontdoor_firewall_policy" "pantrypress" {
  for_each = local.waf_policies

  name                              = "wafpp${each.key}${local.env}"
  resource_group_name               = azurerm_resource_group.pantrypress.name
  sku_name                          = "Standard_AzureFrontDoor"
  enabled                           = true
  mode                              = var.waf.mode
  custom_block_response_status_code = 403
  tags                              = local.tags

  # A ceiling per client address. Cached pages are cheap, so this is set for abuse, not for a viral spike.
  custom_rule {
    name                           = "RateLimitPerClient"
    type                           = "RateLimitRule"
    action                         = "Block"
    priority                       = 100
    rate_limit_duration_in_minutes = 1
    rate_limit_threshold           = var.waf.rate_limit_per_minute

    match_condition {
      match_variable = "RemoteAddr"
      operator       = "IPMatch"
      match_values   = ["0.0.0.0/0", "::/0"]
    }
  }

  # Scanner paths PantryPress never serves. /wp-content is deliberately absent: a blog imported from WordPress (PP20)
  # still has old image links that should reach the redirect table, not a block page.
  custom_rule {
    name     = "BlockScannerPaths"
    type     = "MatchRule"
    action   = "Block"
    priority = 200

    match_condition {
      match_variable = "RequestUri"
      operator       = "Contains"
      transforms     = ["Lowercase"]
      match_values   = ["/wp-login.php", "/xmlrpc.php", "/.env", "/.git/"]
    }
  }
}

resource "azurerm_cdn_frontdoor_security_policy" "pantrypress" {
  for_each = local.waf_policies

  name                     = "secpol-pp-${each.key}-${local.env}"
  cdn_frontdoor_profile_id = data.azurerm_cdn_frontdoor_profile.platform.id

  security_policies {
    firewall {
      cdn_frontdoor_firewall_policy_id = azurerm_cdn_frontdoor_firewall_policy.pantrypress[each.key].id

      association {
        patterns_to_match = ["/*"]

        domain {
          cdn_frontdoor_domain_id = each.value
        }
      }
    }
  }
}
