# Azure Intelligent Developer Platform (AIDP)

AIDP is a secure self-service Azure platform that combines **Infrastructure as Code, CI/CD, private networking, observability, and safety-bounded AI assistants**.

It was built as a hands-on Cloud / DevOps / Platform Engineering project to demonstrate how Azure infrastructure delivery and AI-assisted operations can be designed with strong security boundaries, least privilege, and human approval.

> **Portfolio note:** this repository is a sanitized public representation. It contains no live subscription identifiers, credentials, Terraform state, private endpoints, or environment-specific secrets.

## Why this project matters

Modern platform teams are expected to make cloud delivery faster without giving up governance. AIDP demonstrates that balance by combining:

- self-service workload provisioning;
- Terraform-based Azure infrastructure;
- Azure DevOps YAML pipelines with approvals;
- Workload Identity Federation and Managed Identity;
- scoped RBAC and least-privilege design;
- Private Link and Private DNS;
- Azure Monitor, Application Insights, and Log Analytics;
- Microsoft Foundry-backed AI assistants;
- trusted RAG and provenance-aware evidence;
- deterministic evaluation and AI safety gates.

## What the platform can do

AIDP supports a controlled developer workflow where an authenticated user can request supported infrastructure, have the request validated, and route deployment through Azure DevOps and Terraform with human approval.

The AI layer can:

- review infrastructure configurations;
- troubleshoot deployment failures;
- analyze application health;
- use trusted platform knowledge through RAG;
- incorporate bounded read-only evidence from Azure Monitor, Application Insights, Resource Health, and deployment metadata.

The AI **cannot** deploy infrastructure, change RBAC, restart workloads, approve releases, execute Terraform, or perform autonomous remediation.

## Architecture

```mermaid
flowchart LR
    U[Engineer / Developer] --> P[AIDP Portal]
    P --> E[Microsoft Entra ID]
    P --> API[AIDP API]
    API --> V[Deterministic Validation & Authorization]
    V --> ADO[Azure DevOps Pipeline]
    ADO --> TF[Terraform Plan / Apply]
    TF --> AZ[Azure Workloads]

    API --> AI[Microsoft Foundry AI Assistants]
    AI --> RAG[Trusted Platform Knowledge / RAG]
    AI --> MON[Read-only Azure Evidence]

    MON --> AM[Azure Monitor]
    MON --> APPI[Application Insights]
    MON --> RH[Resource Health]
    MON --> DM[Deployment Metadata]

    H[Human Approval] --> ADO
    AZ --> PE[Private Endpoints / Private DNS]
    AZ --> OBS[Diagnostics / Log Analytics]
```

See [`docs/architecture-overview.md`](docs/architecture-overview.md) for a concise architecture walkthrough.

## Key engineering decisions

### Identity before secrets

Azure access is designed around **Managed Identity** and **Workload Identity Federation** instead of long-lived credentials wherever possible.

### Human approval remains authoritative

AI recommendations never bypass deterministic validation, deployment controls, or human approval.

### Read-only AI evidence

Operational AI analysis uses bounded, allowlisted Azure evidence rather than arbitrary resource browsing, arbitrary KQL, or model-selected write operations.

### Private service connectivity

The project demonstrates Private Endpoints, Private DNS, and default-deny networking patterns for selected Azure services.

### Observability as a platform capability

Diagnostic settings, Log Analytics, Application Insights, Azure Monitor, and Resource Health are treated as part of the platform rather than an afterthought.

## Security & AI safety

The security model separates authentication, authorization, deployment authority, and AI advice.

Key controls include:

- Microsoft Entra ID authentication;
- deterministic backend validation;
- scoped RBAC;
- Managed Identity;
- Workload Identity Federation;
- private networking patterns;
- structured AI outputs;
- provenance-aware evidence;
- credential sanitization;
- groundedness checks;
- prompt-injection and adversarial evaluation;
- human approval before infrastructure changes.

More detail:

- [`docs/security-model.md`](docs/security-model.md)
- [`docs/ai-safety.md`](docs/ai-safety.md)
- [`docs/evaluation.md`](docs/evaluation.md)

## Repository layout

```text
src/Aidp.Api/              Authenticated API, collectors, AI services, validation, and safety controls
src/Aidp.Portal/           React/Vite portal and MSAL integration
terraform/                 Sanitized example platform and workload infrastructure
pipelines/                 Azure DevOps pipeline templates
azure-pipelines*.yml       Platform and workload delivery flows
tests/Aidp.Api.Evals/      Deterministic AI evaluation suites
tests/Aidp.Api.AuthChecks/ Authentication and integration checks
docs/                      Architecture, security, operations, and AI safety documentation
```

## Technologies demonstrated

**Azure:** App Service, Storage, Microsoft Entra ID, Managed Identity, RBAC, Private Link, Private DNS, Azure Monitor, Application Insights, Log Analytics, Resource Health, Microsoft Foundry

**DevOps / IaC:** Terraform, Azure DevOps, YAML pipelines, Git, approvals, Workload Identity Federation

**AI platform engineering:** trusted RAG, structured outputs, provenance, safety validation, deterministic evaluations, bounded read-only Azure evidence

**Application stack:** .NET, React, Vite, MSAL

## Current project status

Core platform capabilities are implemented in the private lab environment and represented here in sanitized form, including:

- Terraform-based Azure infrastructure;
- platform and workload CI/CD flows;
- Entra authentication;
- self-service workload provisioning;
- AI infrastructure review;
- deployment troubleshooting;
- health analysis;
- trusted RAG;
- live Azure telemetry evidence;
- AI safety/evaluation gates;
- diagnostic settings and private networking controls.

Current work focuses on additional security hardening, observability, cost controls, and portfolio polish.

## Running locally

Copy:

```text
src/Aidp.Portal/.env.example
```

to `.env.local`, replace the placeholder Entra values with a private test registration, and configure Azure settings outside this repository.

Do not commit local environment files, Terraform state, deployment artifacts, or credentials.

## Author

**Andres Stein**  
Cloud / DevOps / Platform Engineer
