output "app_service_id" {
  value = azurerm_linux_web_app.workload.id
}

output "app_service_name" {
  value = azurerm_linux_web_app.workload.name
}

output "default_hostname" {
  value = azurerm_linux_web_app.workload.default_hostname
}

output "principal_id" {
  value = azurerm_linux_web_app.workload.identity[0].principal_id
}

output "application_insights_id" {
  value = azurerm_application_insights.workload.id
}

output "application_insights_name" {
  value = azurerm_application_insights.workload.name
}
