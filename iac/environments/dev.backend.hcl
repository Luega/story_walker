# Remote state for dev. The account and container are created once, by hand (see iac/README.md).
resource_group_name  = "rg-storywalker-tfstate"
storage_account_name = "<tfstate-account>"
container_name       = "tfstate"
key                  = "dev.tfstate"
use_azuread_auth     = true
