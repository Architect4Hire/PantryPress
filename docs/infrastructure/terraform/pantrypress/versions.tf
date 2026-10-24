terraform {
  # 1.11 for write-only arguments: the gateway secret and the platform admin list reach Key Vault without ever being
  # written to state.
  required_version = ">= 1.11"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 5.10"
    }
    random = {
      source  = "hashicorp/random"
      version = "~> 3.9"
    }
    time = {
      source  = "hashicorp/time"
      version = "~> 0.14"
    }
  }

  # Partial configuration: run.ps1 supplies ../backend.hcl plus key = "pantrypress-<env>.tfstate".
  backend "azurerm" {}
}

provider "azurerm" {
  features {
    key_vault {
      # A destroyed dev vault is purged so its name can be reused; prod's vault has purge protection on anyway.
      purge_soft_delete_on_destroy = true
    }
  }
  subscription_id     = var.subscription_id
  storage_use_azuread = true
}
