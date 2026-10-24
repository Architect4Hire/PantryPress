# Prod: always-on public tier (no cold start on a cache miss), point-in-time restore for 35 days, highly available
# Redis, private endpoints, WAF in prevention mode. Sized for launch; scale settings are the first thing to revisit.

subscription_id = "00000000-0000-0000-0000-000000000000"
environment     = "prod"
location        = "eastus2"

platform = {
  container_apps_subnet_cidr = "10.50.0.0/23" # must match the platform stack's infrastructure_subnet_cidr
}

container_registry = {
  name = "crpantrypress1234" # from the bootstrap stack
}

dns_zone_name = "pantrypress.app"

app_scale = {
  gateway    = { cpu = 0.5, memory = "1Gi", min_replicas = 1, max_replicas = 1 } # one replica until PP19 persists the key ring
  apiservice = { cpu = 0.5, memory = "1Gi", min_replicas = 1, max_replicas = 3 }
  site       = { cpu = 0.5, memory = "1Gi", min_replicas = 1, max_replicas = 5 }
  site-ssr   = { cpu = 0.5, memory = "1Gi", min_replicas = 1, max_replicas = 10 }
  worker     = { cpu = 0.5, memory = "1Gi", min_replicas = 1, max_replicas = 1 }
}

sql = {
  sku_name              = "S1" # 20 DTU, 250 GB included, about $30 a month; S2 is the next step
  max_size_gb           = 250
  backup_retention_days = 35 # OPS-008
  backup_redundancy     = "Geo"
  entra_admin_login     = "you@example.com"
}

redis = {
  sku_name          = "Balanced_B0"
  high_availability = true
}

storage_replication          = "ZRS"
private_endpoints_enabled    = true
private_endpoint_subnet_cidr = "10.50.2.0/27"
operator_ip_cidrs            = ["203.0.113.10/32"] # replace: the address you run Terraform and sqlcmd from

waf = {
  enabled               = true
  mode                  = "Prevention"
  rate_limit_per_minute = 2000
}

budget = {
  amount = 350
  emails = ["you@example.com"]
}
