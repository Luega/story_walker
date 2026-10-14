# Remote state for prod. The account and container are created once, by hand (see iac/README.md).
resource_group_name  = "rg-storywalker-tfstate"
storage_account_name = "storywalkertfstate"
container_name       = "tfstate"
key                  = "prod.tfstate"
use_azuread_auth     = true
