# OPS-009: alerts at 50%, 80%, and 100% of a monthly budget, plus a forecast warning. It covers this environment's
# PantryPress resource group and the shared platform group, so the Front Door base fee is counted; the platform group
# also carries CreatorPantry's share once it arrives.

resource "azurerm_consumption_budget_subscription" "pantrypress" {
  count = length(var.budget.emails) > 0 ? 1 : 0

  name            = "budget-pantrypress-${local.env}"
  subscription_id = "/subscriptions/${var.subscription_id}"
  amount          = var.budget.amount
  time_grain      = "Monthly"

  time_period {
    start_date = var.budget.start_date
  }

  filter {
    dimension {
      name     = "ResourceGroupName"
      operator = "In"
      values   = [azurerm_resource_group.pantrypress.name, local.platform.resource_group_name]
    }
  }

  dynamic "notification" {
    for_each = toset(["50", "80", "100"])

    content {
      enabled        = true
      threshold      = tonumber(notification.value)
      operator       = "GreaterThanOrEqualTo"
      threshold_type = "Actual"
      contact_emails = var.budget.emails
    }
  }

  notification {
    enabled        = true
    threshold      = 100
    operator       = "GreaterThanOrEqualTo"
    threshold_type = "Forecasted"
    contact_emails = var.budget.emails
  }

  lifecycle {
    # Azure moves the start date forward on its own; a re-apply should not fight it.
    ignore_changes = [time_period]
  }
}
