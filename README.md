# Azure Intelligent Developer Platform (AIDP)

AIDP is a portfolio implementation of a secure self-service Azure platform. It combines Infrastructure as Code, Azure DevOps CI/CD, observability, private networking, least-privilege identity, and safety-bounded AI assistants.

## What it demonstrates

- Terraform-delivered Azure App Service workloads
- Azure DevOps YAML CI/CD with workload identity federation
- System-assigned Managed Identity and scoped RBAC
- Private Link and private DNS for service connectivity
- Azure Monitor, Application Insights, and Log Analytics
- Microsoft Foundry-backed infrastructure review, deployment troubleshooting, and live health analysis
- Trusted platform knowledge and retrieval-grounded responses
- Deterministic evaluation, groundedness, prompt-injection, and safety gates
- Human approval before infrastructure changes; AI remains advisory and read-only

This is a sanitized portfolio representation. It contains no production credentials, tokens, connection strings, or live environment identifiers. Values in Terraform, app settings, and examples are placeholders and must be supplied through a private deployment configuration.

## Repository layout

- `src/Aidp.Api` — authenticated API, deterministic validation, trusted collectors, AI contracts, and safety controls
- `src/Aidp.Portal` — React/Vite portal and MSAL integration
- `terraform` — example platform and workload infrastructure
- `pipelines` and `azure-pipelines*.yml` — CI/CD templates and approval-oriented delivery flow
- `tests` — authentication checks and deterministic AI evaluation suites
- `docs` — architecture, operations, security, and evaluation notes

## Security model

The API validates every request deterministically. AI output is structured, provenance-aware, and advisory. The AI cannot queue pipelines, modify Azure, change RBAC, execute Terraform, or approve deployments. Azure access uses managed identity and fixed allowlists for read-only evidence collection.

## Running locally

Copy `src/Aidp.Portal/.env.example` to `.env.local`, replace the placeholder Entra values with values from a private test registration, and configure private Azure settings outside this repository. Do not commit local environment files or Terraform state.
