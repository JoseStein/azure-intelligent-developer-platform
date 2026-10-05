variable "application_name" {
  description = "3–30 lowercase letters, digits or hyphens; no leading/trailing hyphen. aidp is reserved for the platform."
  type        = string

  validation {
    condition = (
      length(var.application_name) >= 3 && length(var.application_name) <= 30 &&
      can(regex("^[a-z0-9][a-z0-9-]*[a-z0-9]$", var.application_name)) &&
      var.application_name != "aidp"
    )
    error_message = "Use a 3–30 character lowercase application name with no leading/trailing hyphen; aidp is reserved."
  }
}

variable "environment" {
  description = "Only dev is supported."
  type        = string

  validation {
    condition     = var.environment == "dev"
    error_message = "Environment must be dev."
  }
}

variable "location" {
  description = "Location of the existing DEV App Service Plan."
  type        = string
  default     = "South Central US"

  validation {
    condition     = contains(["South Central US", "southcentralus"], var.location)
    error_message = "Only South Central US is supported by this workload template."
  }
}

variable "resource_group_name" {
  description = "Existing platform resource group; this template does not manage it."
  type        = string
  default     = "rg-aidp-example-dev"

  validation {
    condition     = var.resource_group_name == "rg-aidp-example-dev"
    error_message = "Resource group must be rg-aidp-example-dev."
  }
}

variable "app_service_plan_id" {
  description = "Resource ID of the existing asp-aidp-example-dev plan. No new plan is created."
  type        = string

  validation {
    condition     = can(regex("(?i)^/subscriptions/[0-9a-f-]+/resourceGroups/rg-aidp-example-dev/providers/Microsoft.Web/serverFarms/asp-aidp-example-dev$", var.app_service_plan_id))
    error_message = "Supply the existing asp-aidp-example-dev plan ID in rg-aidp-example-dev."
  }
}
