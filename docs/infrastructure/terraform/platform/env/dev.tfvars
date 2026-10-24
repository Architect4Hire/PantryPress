subscription_id = "00000000-0000-0000-0000-000000000000"
environment     = "dev"
location        = "eastus2"

vnet_address_space         = "10.40.0.0/16"
infrastructure_subnet_cidr = "10.40.0.0/23"

zone_redundant            = false
log_retention_days        = 30
log_daily_quota_gb        = 0.5 # dev noise should never cost more than pennies a day
private_dns_zones_enabled = false
