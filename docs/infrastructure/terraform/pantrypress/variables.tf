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

# --- Shared platform (looked up by name, never managed here) ---------------------------------------------------------

variable "platform" {
  description = "Names of the shared resources the platform stack created. Null fields follow its naming convention."
  type = object({
    resource_group_name            = optional(string)
    container_app_environment_name = optional(string)
    front_door_profile_name        = optional(string)
    virtual_network_name           = optional(string)
    container_apps_subnet_cidr     = string
  })
}

variable "container_registry" {
  description = "The registry the bootstrap stack created. One registry serves both environments."
  type = object({
    name                = string
    resource_group_name = optional(string, "rg-pantrypress-tfstate")
  })
}

# --- Hosts --------------------------------------------------------------------------------------------------------------

variable "dns_zone_name" {
  description = "The zone this environment's hosts live in: blogs at {slug}.<zone>, the admin app at app.<zone>."
  type        = string
}

variable "email_subdomain" {
  description = "Account email is sent from this subdomain of the zone, so the zone's apex keeps its own mail setup."
  type        = string
  default     = "mail"
}

variable "email_enabled" {
  type    = bool
  default = true
}

# --- Apps ---------------------------------------------------------------------------------------------------------------

variable "images" {
  description = "Image per app. The defaults are Microsoft sample images that listen on 8080, so the first apply comes up healthy before PantryPress images exist."
  type        = map(string)
  default = {
    gateway    = "mcr.microsoft.com/dotnet/samples:aspnetapp"
    apiservice = "mcr.microsoft.com/dotnet/samples:aspnetapp"
    site       = "mcr.microsoft.com/dotnet/samples:aspnetapp"
    site-ssr   = "mcr.microsoft.com/dotnet/samples:aspnetapp"
    worker     = "mcr.microsoft.com/dotnet/samples:aspnetapp"
    migrations = "mcr.microsoft.com/dotnet/samples:dotnetapp"
  }

  validation {
    condition     = alltrue([for k in ["gateway", "apiservice", "site", "site-ssr", "worker", "migrations"] : contains(keys(var.images), k)])
    error_message = "images needs an entry for gateway, apiservice, site, site-ssr, worker, and migrations."
  }
}

variable "app_scale" {
  description = "Size and replica range per app. Consumption sizes pair CPU and memory: 0.25/0.5Gi, 0.5/1Gi, 0.75/1.5Gi, 1/2Gi, and so on."
  type = map(object({
    cpu          = number
    memory       = string
    min_replicas = number
    max_replicas = number
  }))

  validation {
    # The gateway's data-protection key ring is neither persisted nor shared yet (PP1.6, PP1.7): a second replica could
    # not read the first one's cookies or antiforgery tokens.
    condition     = var.app_scale["gateway"].max_replicas == 1
    error_message = "The gateway must run one replica until its data-protection key ring is persisted (PP19)."
  }
}

# --- Data ---------------------------------------------------------------------------------------------------------------

variable "sql" {
  type = object({
    sku_name              = string # "Basic" (dev), "S1" (prod) - see the plan for why DTU, not serverless
    max_size_gb           = number
    backup_retention_days = number           # Basic allows at most 7; OPS-008 asks 35 for prod
    backup_redundancy     = string           # "Local" or "Geo"
    entra_admin_login     = string           # a user or group display name / UPN
    entra_admin_object_id = optional(string) # defaults to whoever runs Terraform
  })
}

variable "redis" {
  type = object({
    sku_name          = string # "Balanced_B0" is the smallest Azure Managed Redis size
    high_availability = bool
  })
}

variable "storage_replication" {
  description = "LRS for dev; ZRS for prod keeps originals through a zone outage."
  type        = string
}

# --- Network ------------------------------------------------------------------------------------------------------------

variable "private_endpoints_enabled" {
  description = "Private endpoints for SQL, Redis, Blob, and Key Vault. About $7.30 a month each; the platform stack must have created the privatelink zones."
  type        = bool
  default     = false
}

variable "private_endpoint_subnet_cidr" {
  description = "PantryPress's own subnet in the shared network, used only when private endpoints are on."
  type        = string
  default     = null
}

variable "operator_ip_cidrs" {
  description = "Public addresses that run Terraform and the SQL grant script. Needed wherever SQL, Key Vault, or storage refuse the public internet."
  type        = list(string)
  default     = []
}

# --- Edge ---------------------------------------------------------------------------------------------------------------

variable "waf" {
  type = object({
    enabled               = bool
    mode                  = string # "Detection" or "Prevention"
    rate_limit_per_minute = number # per client address, across every path
  })
}

variable "site_api_paths" {
  description = "Blog-host paths Front Door sends to PantryPress.Site instead of the SSR server. Everything else on a blog host is a page."
  type        = list(string)
  default     = ["/api/public/*", "/robots.txt", "/ads.txt", "/sitemap.xml", "/sitemaps/*", "/feed", "/feed/*"]
}

# --- Secrets ------------------------------------------------------------------------------------------------------------

variable "gateway_secret_version" {
  description = "Bump to rotate the gateway's shared secret. Both apps pick up the new value on their next revision."
  type        = number
  default     = 1
}

variable "platform_admins" {
  description = "Email addresses holding PlatformAdmin (ACCT-005), comma separated. Never written to state. Leave unset to keep what Key Vault has."
  type        = string
  default     = null
  nullable    = true
  sensitive   = true
  ephemeral   = true
}

variable "platform_admins_version" {
  description = "Bump together with TF_VAR_platform_admins to write a new list."
  type        = number
  default     = 1
}

# --- Cost guard ---------------------------------------------------------------------------------------------------------

variable "budget" {
  description = "Monthly budget across this environment's PantryPress and platform resource groups (OPS-009). No emails, no budget."
  type = object({
    amount     = number
    emails     = list(string)
    start_date = optional(string, "2026-10-01T00:00:00Z")
  })
}

variable "tags" {
  type    = map(string)
  default = {}
}
