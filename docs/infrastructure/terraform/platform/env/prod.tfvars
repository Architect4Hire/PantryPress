subscription_id = "00000000-0000-0000-0000-000000000000"
environment     = "prod"
location        = "eastus2"

vnet_address_space         = "10.50.0.0/16"
infrastructure_subnet_cidr = "10.50.0.0/23"

zone_redundant            = false # decide before the first apply: it cannot be changed later
log_retention_days        = 30
log_daily_quota_gb        = -1 # no cap: an incident is when logs matter most
private_dns_zones_enabled = true
