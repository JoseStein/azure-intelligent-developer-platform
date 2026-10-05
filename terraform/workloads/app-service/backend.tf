terraform {
  backend "azurerm" {
    storage_account_name = "tfstatepublicexample"
    container_name       = "tfstate"
    use_azuread_auth     = true
    use_oidc             = true
    # Required at init: -backend-config="key=workloads/dev/<application-name>.tfstate"
    # Never use the platform state key aidp-dev.tfstate.
  }
}
