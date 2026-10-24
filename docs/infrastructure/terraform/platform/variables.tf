variable "subscription_id" {
  type = string
}

variable "environment" {
  description = "dev or prod. Names every resource, so it cannot change after the first apply."
  type        = string

  validation {
    condition     = contains(["dev", "prod"], var.environment)
    error_message = "environment is dev or prod."
  }
}

variable "location" {
  type    = string
  default = "eastus2"
}

variable "vnet_address_space" {
  description = "The shared virtual network. PantryPress and CreatorPantry each add their own subnets inside it."
  type        = string
}

variable "infrastructure_subnet_cidr" {
  description = "Container Apps infrastructure subnet. A workload-profiles environment needs /27 or larger; /23 leaves room for both products."
  type        = string
}

variable "zone_redundant" {
  description = "Spread the environment across availability zones. Free, but fixed at creation and only useful with 2+ replicas per app."
  type        = bool
  default     = false
}

variable "log_retention_days" {
  description = "30 days of analytics retention is included in the ingestion price."
  type        = number
  default     = 30
}

variable "log_daily_quota_gb" {
  description = "Daily ingestion cap in GB, the backstop against a logging bug running up the bill. -1 means no cap."
  type        = number
  default     = -1
}

variable "private_dns_zones_enabled" {
  description = "Create the privatelink DNS zones the products' private endpoints register in. Needed only where private endpoints are used."
  type        = bool
  default     = false
}

variable "tags" {
  type    = map(string)
  default = {}
}
