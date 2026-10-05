# Non-secret values for the prod environment. Secrets go in environment variables:
#   export TF_VAR_clerk_secret_key=sk_live_...
environment     = "prod"
subscription_id = "00000000-0000-0000-0000-000000000000"

chat_model = {
  name     = "gpt-5.6-luna"
  capacity = 100
}

image_model = {
  name     = "gpt-image-1-mini"
  capacity = 2
}

postgres_sku                   = "GP_Standard_D2ds_v5"
postgres_backup_retention_days = 35
servicebus_sku                 = "Standard"

clerk_authority       = "https://clerk.<your-domain>"
clerk_publishable_key = "pk_live_..."
