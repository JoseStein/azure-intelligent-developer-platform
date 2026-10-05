terraform {
  backend "azurerm" {
    storage_account_name = "tfstatepublicexample"
    container_name       = "tfstate"
    key                  = "aidp-dev.tfstate"

    use_azuread_auth = true
  }
}
