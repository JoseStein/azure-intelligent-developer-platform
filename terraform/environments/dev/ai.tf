resource "azurerm_cognitive_account" "aidp_ai" {
  name                = "aif-aidp-example-dev"
  resource_group_name = azurerm_resource_group.aidp_dev.name
  location            = azurerm_resource_group.aidp_dev.location

  kind     = "AIServices"
  sku_name = "S0"

  custom_subdomain_name         = "aif-aidp-example-dev"
  local_auth_enabled            = false
  public_network_access_enabled = false

  tags = local.common_tags
}

resource "azurerm_cognitive_deployment" "aidp_ai_review" {
  name                 = "gpt-5.4-mini"
  cognitive_account_id = azurerm_cognitive_account.aidp_ai.id

  model {
    format  = "OpenAI"
    name    = "gpt-5.4-mini"
    version = "2026-03-17"
  }

  sku {
    name     = "GlobalStandard"
    capacity = 10
  }
}

resource "azurerm_role_assignment" "aidp_api_openai_user" {
  scope                = azurerm_cognitive_account.aidp_ai.id
  role_definition_name = "Cognitive Services OpenAI User"
  principal_id         = azurerm_linux_web_app.aidp_dev.identity[0].principal_id
}
