# Azure Communication Services Email, PantryPress's own resource and sending domain (requirements 5.1). Pay per message;
# nothing here has a fixed fee. The app does not send through it yet - PP13.1 adds the sender and its settings.
#
# Terraform writes the verification records into the zone. Asking Azure to check them is one command per record type
# (runbook, "Email domain"), because verification is an action, not a resource.

resource "azurerm_email_communication_service" "pantrypress" {
  count = var.email_enabled ? 1 : 0

  name                = "acs-email-pantrypress-${local.env}"
  resource_group_name = azurerm_resource_group.pantrypress.name
  data_location       = "United States"
  tags                = local.tags
}

resource "azurerm_email_communication_service_domain" "pantrypress" {
  count = var.email_enabled ? 1 : 0

  name                             = local.email_domain
  email_service_id                 = azurerm_email_communication_service.pantrypress[0].id
  domain_management                = "CustomerManaged"
  user_engagement_tracking_enabled = false
  tags                             = local.tags
}

resource "azurerm_communication_service" "pantrypress" {
  count = var.email_enabled ? 1 : 0

  name                = "acs-pantrypress-${local.env}-${local.suffix}"
  resource_group_name = azurerm_resource_group.pantrypress.name
  data_location       = "United States"
  tags                = local.tags
}

resource "azurerm_communication_service_email_domain_association" "pantrypress" {
  count = var.email_enabled ? 1 : 0

  communication_service_id = azurerm_communication_service.pantrypress[0].id
  email_service_domain_id  = azurerm_email_communication_service_domain.pantrypress[0].id
}

locals {
  email_records = var.email_enabled ? azurerm_email_communication_service_domain.pantrypress[0].verification_records[0] : null

  # The service reports record names either fully qualified or relative to the sending domain; the zone wants them
  # relative to itself.
  email_record_name = var.email_enabled ? {
    for kind in ["domain", "spf", "dkim", "dkim2"] : kind => (
      local.email_records[kind][0].name == "@" || local.email_records[kind][0].name == local.email_domain || local.email_records[kind][0].name == var.email_subdomain
      ? var.email_subdomain
      : endswith(local.email_records[kind][0].name, ".${var.dns_zone_name}")
      ? trimsuffix(local.email_records[kind][0].name, ".${var.dns_zone_name}")
      : "${local.email_records[kind][0].name}.${var.email_subdomain}"
    )
  } : {}
}

# Ownership and SPF share one name, so they share one TXT record set.
resource "azurerm_dns_txt_record" "email_domain" {
  count = var.email_enabled ? 1 : 0

  name                = local.email_record_name["domain"]
  zone_name           = azurerm_dns_zone.pantrypress.name
  resource_group_name = azurerm_resource_group.pantrypress.name
  ttl                 = 3600

  record {
    value = local.email_records.domain[0].value
  }

  record {
    value = local.email_records.spf[0].value
  }

  lifecycle {
    precondition {
      condition     = local.email_record_name["domain"] == local.email_record_name["spf"]
      error_message = "The email service put its ownership and SPF records under different names; split this record set."
    }
  }
}

resource "azurerm_dns_cname_record" "email_dkim" {
  for_each = var.email_enabled ? toset(["dkim", "dkim2"]) : toset([])

  name                = local.email_record_name[each.key]
  zone_name           = azurerm_dns_zone.pantrypress.name
  resource_group_name = azurerm_resource_group.pantrypress.name
  ttl                 = 3600
  record              = local.email_records[each.key][0].value
}

# Monitor-only DMARC for the sending subdomain. Tighten to quarantine once reports show only Azure sending for it.
resource "azurerm_dns_txt_record" "email_dmarc" {
  count = var.email_enabled ? 1 : 0

  name                = "_dmarc.${var.email_subdomain}"
  zone_name           = azurerm_dns_zone.pantrypress.name
  resource_group_name = azurerm_resource_group.pantrypress.name
  ttl                 = 3600

  record {
    value = "v=DMARC1; p=none; adkim=s; aspf=s"
  }
}
