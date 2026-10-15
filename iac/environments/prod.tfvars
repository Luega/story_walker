# Non-secret values for prod, the only environment (ADR-0035). Sizes are the variables' defaults —
# the smallest the code allows: PostgreSQL B_Standard_B1ms with 32 GB and seven days of backups
# (covered by the Azure free account for its first year), Service Bus Basic. Resize here, in place.
# Secrets go in environment variables:
#   export TF_VAR_clerk_secret_key=sk_test_...
environment     = "prod"
subscription_id = "e636536c-8578-4283-8475-4769b8a7071a"

# The Free Trial subscription has no quota for gpt-5.6-luna or any OpenAI image model (ADR-0035);
# these two have it. Back to gpt-5.6-luna and gpt-image-1-mini once quota is granted.
chat_model = {
  name    = "gpt-5-mini"
  version = "2025-08-07"
}

image_model = {
  name    = "FLUX.2-pro"
  format  = "Black Forest Labs"
  version = "1"
  # Requests a minute for FLUX: at the default 1, the six pages of a story are throttled (429) and
  # the story is dead-lettered. FLUX is billed per image, so the whole quota (15) costs nothing.
  capacity = 15
}

# swedencentral had no B1ms capacity for this subscription (RegionalAllocationFailed, twice);
# North Europe (Ireland) is the nearest region that has it. Only the database moves.
postgres_location = "northeurope"

# A handful of users: one replica each is enough, and caps what an unexpected burst can cost.
api_max_replicas = 1
ui_max_replicas  = 1

# In the subscription's billing currency, which is SEK: about $20 a month.
monthly_budget = 200

# 0.15 GB a day stays inside the 5 GB a month Log Analytics ingests for free (ADR-0035).
log_daily_quota_gb = 0.15

# A Clerk production instance needs a domain of our own; until there is one, the development
# instance serves the Container Apps default domain.
clerk_authority       = "https://able-puma-4364.clerk.accounts.dev"
clerk_publishable_key = "pk_test_YWJsZS1wdW1hLTQzNjQuY2xlcmsuYWNjb3VudHMuZGV2JA"

# Set by CI once the images exist; null runs a placeholder.
# api_image = "ghcr.io/<owner>/storywalker-api:<sha>"
# ui_image  = "ghcr.io/<owner>/storywalker-ui:<sha>"
