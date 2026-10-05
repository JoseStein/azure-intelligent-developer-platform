resource "azurerm_resource_group" "aidp_dev" {
  name     = "rg-aidp-example-dev"
  location = local.location

  tags = local.common_tags
}

resource "azurerm_service_plan" "aidp_dev" {
  name                = "asp-aidp-example-dev"
  resource_group_name = azurerm_resource_group.aidp_dev.name
  location            = azurerm_resource_group.aidp_dev.location

  os_type  = "Linux"
  sku_name = "B1"

  tags = local.common_tags
}

resource "azurerm_linux_web_app" "aidp_dev" {
  name                = "app-aidp-example-dev"
  resource_group_name = azurerm_resource_group.aidp_dev.name
  location            = azurerm_service_plan.aidp_dev.location
  service_plan_id     = azurerm_service_plan.aidp_dev.id

  https_only                                     = true
  ftp_publish_basic_authentication_enabled       = false
  webdeploy_publish_basic_authentication_enabled = false

  identity {
    type = "SystemAssigned"
  }

  app_settings = {
    "APPLICATIONINSIGHTS_CONNECTION_STRING" = azurerm_application_insights.aidp_dev.connection_string
    "AzureDevOps__Organization"             = "example-org"
    "AzureDevOps__Project"                  = "aidp-public-platform"
    "AzureDevOps__WorkloadPipelineId"       = "3"
    "AzureDevOps__WorkloadBranch"           = "refs/heads/main"
    "AzureDevOps__QueueEnabled"             = "true"
    "AiReview__Provider"                    = "Foundry"
    "AiReview__Endpoint"                    = "https://${azurerm_cognitive_account.aidp_ai.custom_subdomain_name}.openai.azure.com/openai/v1/"
    "AiReview__ModelDeployment"             = azurerm_cognitive_deployment.aidp_ai_review.name
    "AiReview__TimeoutSeconds"              = "30"
  }

  site_config {
    always_on = false

    application_stack {
      dotnet_version = "10.0"
    }
  }

  tags = local.common_tags

  lifecycle {
    ignore_changes = [
      tags["hidden-link: /app-insights-resource-id"],
      virtual_network_subnet_id
    ]
  }
}

resource "azurerm_log_analytics_workspace" "aidp_dev" {
  name                = "log-aidp-example-dev"
  resource_group_name = azurerm_resource_group.aidp_dev.name
  location            = azurerm_resource_group.aidp_dev.location

  sku               = "PerGB2018"
  retention_in_days = 30

  tags = local.common_tags
}

resource "azurerm_application_insights" "aidp_dev" {
  name                = "appi-aidp-example-dev"
  resource_group_name = azurerm_resource_group.aidp_dev.name
  location            = azurerm_resource_group.aidp_dev.location

  application_type = "web"
  workspace_id     = azurerm_log_analytics_workspace.aidp_dev.id

  tags = local.common_tags
}

data "azurerm_monitor_diagnostic_categories" "aidp_dev_app_service" {
  resource_id = azurerm_linux_web_app.aidp_dev.id
}

locals {
  aidp_app_service_log_categories = [
    "AppServiceHTTPLogs",
    "AppServiceConsoleLogs",
    "AppServiceAppLogs",
    "AppServicePlatformLogs",
    "AppServiceAuditLogs",
    "AppServiceIPSecAuditLogs",
    "AppServiceAuthenticationLogs",
  ]
}

resource "azurerm_monitor_diagnostic_setting" "aidp_dev_app_service" {
  name                       = "send-app-service-diagnostics"
  target_resource_id         = azurerm_linux_web_app.aidp_dev.id
  log_analytics_workspace_id = azurerm_log_analytics_workspace.aidp_dev.id

  dynamic "enabled_log" {
    for_each = [
      for category in local.aidp_app_service_log_categories : category
      if contains(data.azurerm_monitor_diagnostic_categories.aidp_dev_app_service.log_category_types, category)
    ]

    content {
      category = enabled_log.value
    }
  }

  dynamic "metric" {
    for_each = contains(data.azurerm_monitor_diagnostic_categories.aidp_dev_app_service.metrics, "AllMetrics") ? ["AllMetrics"] : []

    content {
      category = metric.value
      enabled  = true
    }
  }
}

resource "azurerm_resource_group_policy_assignment" "require_environment_tag" {
  name                 = "require-environment-tag"
  resource_group_id    = azurerm_resource_group.aidp_dev.id
  policy_definition_id = "/providers/Microsoft.Authorization/policyDefinitions/871b6d14-10aa-478d-b590-94f262ecfa99"

  display_name = "Require environment tag on AIDP DEV resources"
  description  = "Requires resources in the AIDP DEV resource group to include the environment tag."

  parameters = jsonencode({
    tagName = {
      value = "environment"
    }
  })
}

resource "azurerm_resource_group_policy_assignment" "require_storage_tls12" {
  name                 = "require-storage-tls12"
  resource_group_id    = azurerm_resource_group.aidp_dev.id
  policy_definition_id = "/providers/Microsoft.Authorization/policyDefinitions/fe83a0eb-a853-422d-aac2-1bffd182c5d0"

  display_name = "Require TLS 1.2 for AIDP DEV storage accounts"
  description  = "Requires storage accounts in the AIDP DEV resource group to use TLS 1.2 as the minimum TLS version."

  parameters = jsonencode({
    effect = {
      value = "Deny"
    }
    minimumTlsVersion = {
      value = "TLS1_2"
    }
  })
}

resource "azurerm_storage_account" "aidp_dev" {
  name                     = "staidpexampledev"
  resource_group_name      = azurerm_resource_group.aidp_dev.name
  location                 = azurerm_resource_group.aidp_dev.location
  account_tier             = "Standard"
  account_replication_type = "LRS"
  min_tls_version          = "TLS1_2"

  shared_access_key_enabled       = false
  default_to_oauth_authentication = true
  allow_nested_items_to_be_public = false

  network_rules {
    default_action = "Deny"
    bypass         = ["AzureServices"]
  }

  tags = local.common_tags
}

resource "azurerm_storage_container" "aidp_dev" {
  name                  = "app-data"
  storage_account_id    = azurerm_storage_account.aidp_dev.id
  container_access_type = "private"
}

resource "azurerm_role_assignment" "aidp_app_blob_reader" {
  scope                = azurerm_storage_container.aidp_dev.id
  role_definition_name = "Storage Blob Data Reader"
  principal_id         = azurerm_linux_web_app.aidp_dev.identity[0].principal_id
}

resource "azurerm_virtual_network" "aidp_dev" {
  name                = "vnet-aidp-example-dev"
  resource_group_name = azurerm_resource_group.aidp_dev.name
  location            = azurerm_resource_group.aidp_dev.location
  address_space       = ["10.20.0.0/16"]

  tags = local.common_tags
}

resource "azurerm_subnet" "app_integration" {
  name                 = "snet-app-integration"
  resource_group_name  = azurerm_resource_group.aidp_dev.name
  virtual_network_name = azurerm_virtual_network.aidp_dev.name
  address_prefixes     = ["10.20.1.0/24"]

  delegation {
    name = "app-service-delegation"

    service_delegation {
      name = "Microsoft.Web/serverFarms"

      actions = [
        "Microsoft.Network/virtualNetworks/subnets/action",
      ]
    }
  }
}

resource "azurerm_subnet" "private_endpoints" {
  name                 = "snet-private-endpoints"
  resource_group_name  = azurerm_resource_group.aidp_dev.name
  virtual_network_name = azurerm_virtual_network.aidp_dev.name
  address_prefixes     = ["10.20.2.0/24"]
}

resource "azurerm_private_endpoint" "aidp_storage_blob" {
  name                = "pe-aidp-storage-example"
  resource_group_name = azurerm_resource_group.aidp_dev.name
  location            = azurerm_resource_group.aidp_dev.location
  subnet_id           = azurerm_subnet.private_endpoints.id

  private_service_connection {
    name                           = "psc-aidp-storage-blob-dev-scus"
    private_connection_resource_id = azurerm_storage_account.aidp_dev.id
    subresource_names              = ["blob"]
    is_manual_connection           = false
  }

  private_dns_zone_group {
    name = "storage-blob-dns"

    private_dns_zone_ids = [
      azurerm_private_dns_zone.storage_blob.id
    ]
  }

  tags = local.common_tags
}

resource "azurerm_private_endpoint" "aidp_foundry" {
  name                = "pe-aidp-foundry-example"
  resource_group_name = azurerm_resource_group.aidp_dev.name
  location            = azurerm_resource_group.aidp_dev.location
  subnet_id           = azurerm_subnet.private_endpoints.id

  private_service_connection {
    name                           = "psc-aidp-foundry-example"
    private_connection_resource_id = azurerm_cognitive_account.aidp_ai.id
    subresource_names              = ["account"]
    is_manual_connection           = false
  }

  private_dns_zone_group {
    name = "foundry-openai-dns"

    private_dns_zone_ids = [
      azurerm_private_dns_zone.foundry_openai.id
    ]
  }

  tags = local.common_tags
}

resource "azurerm_private_dns_zone" "storage_blob" {
  name                = "privatelink.blob.core.windows.net"
  resource_group_name = azurerm_resource_group.aidp_dev.name

  tags = local.common_tags
}

resource "azurerm_private_dns_zone" "foundry_openai" {
  name                = "privatelink.openai.azure.com"
  resource_group_name = azurerm_resource_group.aidp_dev.name

  tags = local.common_tags
}

resource "azurerm_private_dns_zone_virtual_network_link" "storage_blob" {
  name                  = "link-aidp-dev-storage-blob"
  resource_group_name   = azurerm_resource_group.aidp_dev.name
  private_dns_zone_name = azurerm_private_dns_zone.storage_blob.name
  virtual_network_id    = azurerm_virtual_network.aidp_dev.id

  registration_enabled = false

  tags = local.common_tags
}

resource "azurerm_private_dns_zone_virtual_network_link" "foundry_openai" {
  name                  = "link-aidp-dev-foundry-openai"
  resource_group_name   = azurerm_resource_group.aidp_dev.name
  private_dns_zone_name = azurerm_private_dns_zone.foundry_openai.name
  virtual_network_id    = azurerm_virtual_network.aidp_dev.id

  registration_enabled = false

  tags = local.common_tags
}

resource "azurerm_app_service_virtual_network_swift_connection" "aidp_dev" {
  app_service_id = azurerm_linux_web_app.aidp_dev.id
  subnet_id      = azurerm_subnet.app_integration.id
}
