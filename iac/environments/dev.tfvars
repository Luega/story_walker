# Non-secret values for the dev environment. Secrets go in environment variables:
#   export TF_VAR_clerk_secret_key=sk_test_...
environment     = "dev"
subscription_id = "00000000-0000-0000-0000-000000000000"

chat_model = {
  name = "gpt-5.6-luna"
}

image_model = {
  name = "gpt-image-1-mini"
}

clerk_authority       = "https://<your-instance>.clerk.accounts.dev"
clerk_publishable_key = "pk_test_..."

# Set by CI once the images exist; null runs a placeholder.
# api_image = "<registry>.azurecr.io/storywalker-api:<sha>"
# ui_image  = "<registry>.azurecr.io/storywalker-ui:<sha>"
