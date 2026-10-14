# A monthly budget on the resource group, with e-mail alerts (ADR-0035). It stops nothing: it tells
# someone in time. Created only when there is someone to tell.

resource "azurerm_consumption_budget_resource_group" "main" {
  count = length(var.budget_contact_emails) > 0 ? 1 : 0

  name              = "budget-${local.name}"
  resource_group_id = azurerm_resource_group.main.id
  amount            = var.monthly_budget
  time_grain        = "Monthly"

  # A budget starts on the first day of a month. The month it was created in is enough; without the
  # ignore, every later plan would want to move the start to the current month.
  time_period {
    start_date = formatdate("YYYY-MM-01'T'00:00:00Z", timestamp())
  }

  notification {
    threshold      = 50
    threshold_type = "Actual"
    operator       = "GreaterThanOrEqualTo"
    contact_emails = var.budget_contact_emails
  }

  notification {
    threshold      = 80
    threshold_type = "Actual"
    operator       = "GreaterThanOrEqualTo"
    contact_emails = var.budget_contact_emails
  }

  notification {
    threshold      = 100
    threshold_type = "Actual"
    operator       = "GreaterThanOrEqualTo"
    contact_emails = var.budget_contact_emails
  }

  # Warns before the money is spent, while the month is still young.
  notification {
    threshold      = 100
    threshold_type = "Forecasted"
    operator       = "GreaterThanOrEqualTo"
    contact_emails = var.budget_contact_emails
  }

  lifecycle {
    ignore_changes = [time_period]
  }
}
