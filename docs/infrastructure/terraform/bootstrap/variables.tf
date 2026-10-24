variable "subscription_id" {
  description = "The Azure subscription that hosts PantryPress."
  type        = string
}

variable "location" {
  description = "Region for the state account and the registry."
  type        = string
  default     = "eastus2"
}

variable "state_storage_account_name" {
  description = "Globally unique, 3-24 lowercase letters and digits. Every other stack's backend.hcl names it."
  type        = string

  validation {
    condition     = can(regex("^[a-z0-9]{3,24}$", var.state_storage_account_name))
    error_message = "A storage account name is 3-24 lowercase letters and digits."
  }
}

variable "container_registry_name" {
  description = "Globally unique, 5-50 letters and digits. The pantrypress stack references it by name."
  type        = string

  validation {
    condition     = can(regex("^[a-zA-Z0-9]{5,50}$", var.container_registry_name))
    error_message = "A registry name is 5-50 letters and digits."
  }
}

variable "additional_state_operator_object_ids" {
  description = "Other Entra object ids (people or groups) that may run Terraform and push images. The caller is always included."
  type        = list(string)
  default     = []
}
