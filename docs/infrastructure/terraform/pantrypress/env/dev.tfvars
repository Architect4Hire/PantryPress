# Dev: the cheapest shape that still exercises every production path - Front Door, the wildcard host, managed
# identities, Key Vault references, Entra-only SQL. Apps scale to zero; nothing is highly available.

subscription_id = "00000000-0000-0000-0000-000000000000"
environment     = "dev"
location        = "eastus2"

platform = {
  container_apps_subnet_cidr = "10.40.0.0/23" # must match the platform stack's infrastructure_subnet_cidr
}

container_registry = {
  name = "crpantrypress1234" # from the bootstrap stack
}

# A child of pantrypress.app keeps to the one domain you already own: blogs at {slug}.dev.pantrypress.app, the admin
# app at app.dev.pantrypress.app. Reserve the "dev" slug in prod. A separate registrable domain instead (about $15 a
# year) makes dev's blogs and admin app cross-site exactly as prod's are, which the cookie tests prefer.
dns_zone_name = "dev.pantrypress.app"

# Every app except the Worker scales to zero; the first request after an idle spell waits for a cold start.
# The Worker stays at one replica so the outbox keeps draining (PP1.10).
app_scale = {
  gateway    = { cpu = 0.25, memory = "0.5Gi", min_replicas = 0, max_replicas = 1 }
  apiservice = { cpu = 0.25, memory = "0.5Gi", min_replicas = 0, max_replicas = 1 }
  site       = { cpu = 0.25, memory = "0.5Gi", min_replicas = 0, max_replicas = 1 }
  site-ssr   = { cpu = 0.5, memory = "1Gi", min_replicas = 0, max_replicas = 1 }
  worker     = { cpu = 0.25, memory = "0.5Gi", min_replicas = 1, max_replicas = 1 }
}

sql = {
  sku_name              = "Basic" # 5 DTU, 2 GB, about $5 a month
  max_size_gb           = 2
  backup_retention_days = 7
  backup_redundancy     = "Local"
  entra_admin_login     = "you@example.com"
}

redis = {
  sku_name          = "Balanced_B0"
  high_availability = false
}

storage_replication       = "LRS"
private_endpoints_enabled = false
operator_ip_cidrs         = [] # e.g. ["203.0.113.10/32"]; not needed while dev's endpoints are public

waf = {
  enabled               = false # saves the per-policy and per-rule fees; turn on to rehearse PP9.8's rules
  mode                  = "Detection"
  rate_limit_per_minute = 2000
}

budget = {
  amount = 100
  emails = [] # add an address to create the budget alerts
}
