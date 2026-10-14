# Non-secret values for prod, the only environment (ADR-0035). Sizes are the variables' defaults —
# the smallest the code allows: PostgreSQL B_Standard_B1ms with 32 GB and seven days of backups
# (covered by the Azure free account for its first year), Service Bus Basic. Resize here, in place.
# Secrets go in environment variables:
#   export TF_VAR_clerk_secret_key=sk_test_...
environment     = "prod"
subscription_id = "00000000-0000-0000-0000-000000000000"

chat_model = {
  name = "gpt-5.6-luna"
}

image_model = {
  name = "gpt-image-1-mini"
}

# A handful of users: one replica each is enough, and caps what an unexpected burst can cost.
api_max_replicas = 1
ui_max_replicas  = 1

# 0.15 GB a day stays inside the 5 GB a month Log Analytics ingests for free (ADR-0035).
log_daily_quota_gb = 0.15

# A Clerk production instance needs a domain of our own; until there is one, the development
# instance serves the Container Apps default domain.
clerk_authority       = "https://<your-instance>.clerk.accounts.dev"
clerk_publishable_key = "pk_test_..."

# Set by CI once the images exist; null runs a placeholder.
# api_image = "ghcr.io/<owner>/storywalker-api:<sha>"
# ui_image  = "ghcr.io/<owner>/storywalker-ui:<sha>"
