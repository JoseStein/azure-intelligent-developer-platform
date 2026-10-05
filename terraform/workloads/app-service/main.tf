data "azurerm_log_analytics_workspace" "aidp_dev" {
  name                = "log-aidp-example-dev"
  resource_group_name = var.resource_group_name
}

resource "azurerm_application_insights" "workload" {
  name                = "appi-${var.application_name}-${var.environment}-scus"
  resource_group_name = var.resource_group_name
  location            = var.location

  application_type = "web"
  workspace_id     = data.azurerm_log_analytics_workspace.aidp_dev.id

  tags = {
    project       = "aidp-public-platform"
    environment   = var.environment
    owner         = "platform-engineering-lab"
    purpose       = "learning-portfolio"
    managed-by    = "terraform"
    application   = var.application_name
    resource-type = "appservice"
  }
}

data "azurerm_monitor_diagnostic_categories" "workload_app_service" {
  resource_id = azurerm_linux_web_app.workload.id
}

locals {
  workload_app_service_log_categories = [
    "AppServiceHTTPLogs",
    "AppServiceConsoleLogs",
    "AppServiceAppLogs",
    "AppServicePlatformLogs",
    "AppServiceAuditLogs",
    "AppServiceIPSecAuditLogs",
    "AppServiceAuthenticationLogs",
  ]
}

resource "azurerm_linux_web_app" "workload" {
  name                = "app-${var.application_name}-${var.environment}-scus"
  resource_group_name = var.resource_group_name
  location            = var.location
  service_plan_id     = var.app_service_plan_id

  https_only                                     = true
  ftp_publish_basic_authentication_enabled       = false
  webdeploy_publish_basic_authentication_enabled = false

  identity {
    type = "SystemAssigned"
  }

  app_settings = {
    "APPLICATIONINSIGHTS_CONNECTION_STRING"       = azurerm_application_insights.workload.connection_string
    "ApplicationInsightsAgent_EXTENSION_VERSION"  = "~3"
    "XDT_MicrosoftApplicationInsights_Mode"       = "recommended"
    "XDT_MicrosoftApplicationInsights_PreemptSdk" = "1"
  }

  site_config {
    always_on           = false
    ftps_state          = "Disabled"
    minimum_tls_version = "1.2"

    application_stack {
      dotnet_version = "10.0"
    }
  }

  tags = {
    project       = "aidp-public-platform"
    environment   = var.environment
    owner         = "platform-engineering-lab"
    purpose       = "learning-portfolio"
    managed-by    = "terraform"
    application   = var.application_name
    resource-type = "appservice"
  }
}

resource "azurerm_monitor_diagnostic_setting" "workload_app_service" {
  name                       = "send-app-service-diagnostics"
  target_resource_id         = azurerm_linux_web_app.workload.id
  log_analytics_workspace_id = data.azurerm_log_analytics_workspace.aidp_dev.id

  dynamic "enabled_log" {
    for_each = [
      for category in local.workload_app_service_log_categories : category
      if contains(data.azurerm_monitor_diagnostic_categories.workload_app_service.log_category_types, category)
    ]

    content {
      category = enabled_log.value
    }
  }

  dynamic "metric" {
    for_each = contains(data.azurerm_monitor_diagnostic_categories.workload_app_service.metrics, "AllMetrics") ? ["AllMetrics"] : []

    content {
      category = metric.value
      enabled  = true
    }
  }
}
