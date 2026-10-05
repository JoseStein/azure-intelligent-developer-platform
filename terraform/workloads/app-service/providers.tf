provider "azurerm" {
  features {}

  storage_use_azuread             = true
  resource_provider_registrations = "none"
}
