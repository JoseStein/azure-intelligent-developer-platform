# Operations

The delivery flow builds the API, validates Terraform, creates a reviewed plan, pauses for approval, and applies the approved artifact. State uses Azure AD/OIDC authentication and temporary, job-owned firewall access in the private deployment environment. Public deployments should add environment-specific diagnostics, alerting, network policy, and secret-management controls.
