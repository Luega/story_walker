terraform {
  required_version = ">= 1.9"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 4.40"
    }
    random = {
      source  = "hashicorp/random"
      version = "~> 3.6"
    }
  }

  # State lives in an Azure storage account created by hand, once, before the first `init` — it
  # cannot be created by the configuration whose state it holds. Partial configuration: the values
  # are passed per environment, e.g. `terraform init -backend-config=environments/dev.backend.hcl`.
  backend "azurerm" {}
}

provider "azurerm" {
  features {
    resource_group {
      # A resource created outside Terraform (a portal experiment, a diagnostic setting) must not
      # be destroyed silently with the group.
      prevent_deletion_if_contains_resources = true
    }
  }

  subscription_id = var.subscription_id
}
