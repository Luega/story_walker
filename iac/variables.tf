variable "subscription_id" {
  description = "The Azure subscription every resource is created in."
  type        = string
}

variable "environment" {
  description = "Short environment name, part of every resource name, e.g. prod (ADR-0035 keeps only that one)."
  type        = string

  validation {
    condition     = can(regex("^[a-z0-9]{2,6}$", var.environment))
    error_message = "environment must be 2-6 lowercase letters or digits; storage account names have no room for more."
  }
}

variable "location" {
  description = "Region for everything except the Foundry resource."
  type        = string
  default     = "swedencentral"
}

variable "foundry_location" {
  description = "Region of the Foundry resource. Separate because the image models are offered in only a few regions."
  type        = string
  default     = "swedencentral"
}

variable "tags" {
  description = "Extra tags merged onto every resource."
  type        = map(string)
  default     = {}
}

# --- Foundry (ADR-0011) -------------------------------------------------------

variable "chat_model" {
  description = "The chat model the agent and the prompt rewrite use. The deployment is named after the model; that name is what Foundry__Deployment carries. A null version takes the region's default."
  type = object({
    name     = string
    version  = optional(string)
    sku      = optional(string, "GlobalStandard")
    capacity = optional(number, 50) # thousands of tokens per minute
  })
}

variable "image_model" {
  description = "The image model the worker renders pages with. The name picks the route in the worker: a FLUX name goes to the Black Forest Labs route, anything else to the Azure OpenAI images route (FOUNDRY_IMAGE_DEPLOYMENT in .env.example)."
  type = object({
    name     = string
    format   = optional(string, "OpenAI")
    version  = optional(string)
    sku      = optional(string, "GlobalStandard")
    capacity = optional(number, 1)
  })
  default = {
    name = "gpt-image-1-mini"
  }
}

# --- Telemetry (ADR-0023) ----------------------------------------------------

variable "log_daily_quota_gb" {
  description = "Daily ingestion cap on the Log Analytics workspace, Application Insights included; -1 for none. Past it, telemetry is dropped until the next UTC day."
  type        = number
  default     = -1
}

# --- PostgreSQL ---------------------------------------------------------------

variable "postgres_sku" {
  description = "PostgreSQL Flexible Server SKU."
  type        = string
  default     = "B_Standard_B1ms"
}

variable "postgres_storage_mb" {
  description = "PostgreSQL storage, in MB."
  type        = number
  default     = 32768
}

variable "postgres_backup_retention_days" {
  description = "Days of point-in-time restore."
  type        = number
  default     = 7
}

# --- Service Bus (ADR-0003) ---------------------------------------------------

variable "servicebus_sku" {
  description = "Service Bus namespace tier. Basic has everything the queue needs: TTL, delivery count, dead-lettering on expiry."
  type        = string
  default     = "Basic"
}

# --- Function app (ADR-0020) --------------------------------------------------

variable "functions_maximum_instance_count" {
  description = "Upper bound on worker instances. host.json already takes one message per instance; this caps how many briefs render at once, and so the load on the image deployment."
  type        = number
  default     = 10
}

variable "functions_instance_memory_mb" {
  description = "Memory per Flex Consumption instance."
  type        = number
  default     = 2048
}

# --- Container Apps: API and UI -----------------------------------------------

variable "api_image" {
  description = "API image, e.g. ghcr.io/<owner>/storywalker-api:<sha>. Null runs a placeholder until CI has pushed one."
  type        = string
  default     = null
}

variable "ui_image" {
  description = "UI image, e.g. ghcr.io/<owner>/storywalker-ui:<sha>. Null runs a placeholder until CI has pushed one."
  type        = string
  default     = null
}

variable "ghcr_username" {
  description = "The GitHub account the pull token belongs to. Needed once api_image or ui_image is set."
  type        = string
  default     = null
}

variable "ghcr_pull_token" {
  description = "Classic personal access token with only read:packages, for pulling the private images (ADR-0035). Pass it through TF_VAR_ghcr_pull_token, never a committed tfvars file."
  type        = string
  default     = null
  sensitive   = true
}

variable "api_max_replicas" {
  description = "Upper bound on API replicas."
  type        = number
  default     = 3
}

variable "ui_max_replicas" {
  description = "Upper bound on UI replicas."
  type        = number
  default     = 3
}

# --- Clerk (ADR-0002, ADR-0021) -----------------------------------------------

variable "clerk_authority" {
  description = "The Clerk instance URL; the API discovers the JWKS from it."
  type        = string
}

variable "clerk_publishable_key" {
  description = "Clerk publishable key. Public by design."
  type        = string
}

variable "clerk_secret_key" {
  description = "Clerk secret key, read by the UI server. Pass it through TF_VAR_clerk_secret_key, never a committed tfvars file."
  type        = string
  sensitive   = true
}

variable "extra_authorized_parties" {
  description = "Origins allowed to obtain tokens for the API besides the UI's own Container Apps origin, e.g. a custom domain."
  type        = list(string)
  default     = []
}
