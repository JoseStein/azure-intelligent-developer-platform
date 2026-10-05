# Initial Platform Architecture

Status: Planned — platform resources are not yet deployed.

## Purpose

Provide a governed self-service path for deploying an Azure web
application. Add AI assistance only after the basic platform works.

## First golden path

Deploy Azure Web Application, built progressively from:

- Resource group
- App Service Plan
- App Service
- Managed identity with scoped permissions
- Secure application configuration
- Monitoring
- Explicit networking profile
- Optional storage when needed
- Relevant Azure Policy controls

An inexpensive Terraform exercise may precede this golden path.

## Delivery flow

Developer
→ Developer portal
→ Backend authentication, authorization, and input validation
→ Azure DevOps pipeline
→ Terraform plan
→ Required approval
→ Terraform apply
→ Azure resources
→ Monitoring and deployment status

Azure Policy evaluates applicable resource operations.
The portal submits approved catalog inputs, not arbitrary Terraform.

## Source and execution

- Azure Repos is the source of truth.
- Local Git is used to edit, review, and commit changes.
- Azure DevOps runs deployment pipelines.
- Microsoft-hosted agents execute pipeline jobs.
- Paid parallel jobs remain at zero.
- Terraform remote state will be protected separately from source code.

## Security boundaries

- Authentication identifies the caller.
- Authorization determines what the caller may request.
- Backend validation enforces supported catalog configurations.
- Pipeline controls govern deployment execution and approvals.
- Deployment identities receive only the permissions they need.
- Application identities receive scoped access to required resources.
- Azure Policy enforces applicable governance requirements.
- Secrets and Terraform state are never committed to Git.

## Networking approach

Introduce networking with the first workloads.

For each resource, document inbound and outbound traffic,
public/private access, DNS, and service-to-service communication.

Only expose catalog networking profiles that have been implemented
and verified. Advanced network hardening belongs to Phase 8.

## Future AI extension

Microsoft Foundry will translate natural-language requests into
proposed structured inputs for the existing validation workflow.

The model will not authorize deployments or receive unrestricted
Azure deployment permissions.

RAG will retrieve approved platform documentation to explain rules.
Retrieved content cannot override enforced controls.

A future Platform Intelligence capability will use controlled,
read-only access to authoritative Azure information to help engineers
understand the environment. Potential sources include Azure Resource
Graph, Azure Policy, Azure Monitor, Cost Management, and Microsoft
Purview where appropriate.

The AI may explain environment state, identify governance or security
concerns, and recommend actions. It will not receive authority to
change infrastructure directly. Remediation continues through the
platform's authorization, validation, approval, Azure DevOps, and
Terraform workflow.

## Lab constraints

- Personal Azure subscription.
- Monthly spending target: $20 USD.
- Actual-cost alert: 80% ($16).
- Budget alerts do not impose a spending cap.
- Start with one lab environment and one golden path.
- Review costs before choosing SKUs or deploying resources.
- Remove temporary resources after exercises.

## Agreed naming and tagging conventions

Phase 1 decision: agreed by the user; documentation review completed.
These conventions have not yet been applied to deployed resources.

- Project identifier: `aidp`
- Initial environment: `dev`

| Tag | Agreed value |
|---|---|
| `project` | `aidp-public-platform` |
| `environment` | `dev` |
| `owner` | `platform-engineering-lab` |
| `purpose` | `learning-portfolio` |
| `managed-by` | `terraform` |

The owner value identifies the lab's responsible group without using
a personal name. Apply `managed-by = terraform` only to resources
actually managed by Terraform. Tags are descriptive metadata, not
access permissions or automatic cleanup controls.

Use resource type, project, environment, and region as the basis for
resource names. The conceptual pattern is:

```text
<resource-type>-aidp-dev-<region-code>
```

Adapt separators, length, allowed characters, and any required uniqueness
suffix to each Azure resource type's naming restrictions. Validate those
restrictions when implementing the resource.

## Agreed Azure region

Phase 1 decision: agreed by the user.

- Primary lab region: South Central US
- Region code: `scus`

The primary lab region provides a consistent default for resource
placement and naming. Individual services may use another Azure region
when required by service, SKU, quota, model, or feature availability.

Using the agreed naming convention, the conceptual pattern is now:

```text
<resource-type>-aidp-dev-scus
```

Concrete resource names will be selected and validated as resources
are implemented.

## Agreed initial App Service workload

Phase 1 decision: agreed by the user.

The first golden-path workload will use:

- Azure service: Azure App Service
- Runtime: .NET 10 LTS
- Operating system: Linux
- Default lab App Service Plan SKU: F1 Free
- Environment: `dev`
- Region: South Central US (`scus`)

The F1 Free tier is the default for initial learning and development
because the project is operating under a $20 monthly lab target.

F1 is a lab implementation choice, not the proposed production tier.
When an exercise requires capabilities unavailable on F1, the project
may temporarily use the lowest appropriate paid tier after reviewing
its cost and required features. Temporary paid resources should be
removed or downgraded after the exercise when practical.

Production App Service sizing will remain requirements-driven rather
than being based on the lab's F1 selection.

## Agreed initial networking profile

Phase 1 decision: agreed by the user.

The initial golden-path networking profile is `public-lab`.

For the first App Service deployment:

- Inbound access: public App Service endpoint
- Outbound access: default App Service outbound connectivity
- VNet Integration: not enabled initially
- Private Endpoint: not enabled initially
- Private DNS: not required initially

This profile provides a simple networking baseline for the first
Terraform deployment. It is a lab learning configuration, not the
proposed production networking architecture.

Networking will be introduced progressively after the baseline
deployment works. Later exercises will add and test VNet Integration,
Private Endpoints, Private DNS, routing, and access restrictions as
appropriate.

The platform should expose only networking profiles that have been
implemented and verified. Advanced production networking and security
hardening remain part of Phase 8.

## Agreed Terraform state storage and access

Phase 1 decision: agreed by the user.

Terraform will use a remote backend hosted in Azure Blob Storage.

The planned backend architecture is:

- Backend type: Azure Storage / Blob Storage
- State location: private blob container
- Initial environment state: `aidp-dev.tfstate`
- Authentication: Microsoft Entra ID
- Authorization: Azure RBAC
- Storage account keys: avoid where practical
- Terraform state files: never committed to Git

The remote backend will provide a shared source of truth for Terraform
operations performed locally and, later, by Azure DevOps pipelines.

Conceptually:

```text
Local Terraform ───────┐
                       │
                       v
                Azure Blob Storage
                └── Terraform state
                       ^
                       │
Azure DevOps ──────────┘


Record decisions as they are made. Distinguish implemented
lab features from proposed production extensions.

## Agreed portal/backend hosting and authentication approach

Phase 1 decision: agreed by the user.

The AIDP portal and backend will follow a local-first development
approach. The application will be developed and tested locally before
a permanent Azure hosting service is selected.

The initial architecture is:

- Development approach: local-first
- Authentication: Microsoft Entra ID
- Authorization: enforced by backend/platform logic
- Azure hosting service: deferred until portal/backend implementation
- AI authorization authority: none

Microsoft Entra ID will identify authenticated users. Authentication
alone does not grant permission to deploy infrastructure.

The backend will determine which platform operations an authenticated
user is authorized to request. Supported catalog inputs will also be
validated deterministically before any deployment workflow is started.

Conceptually:

```text
Developer
    |
    v
AIDP Portal
    |
    v
Microsoft Entra ID
    |
    v
AIDP Backend
    |
    +-- Authentication context
    +-- Authorization
    +-- Input validation
    |
    v
Azure DevOps
    |
    v
Terraform
    |
    v
Azure

## Agreed monitoring and retention baseline

Phase 1 decision: agreed by the user.

The initial golden-path workload will include a basic observability
baseline so deployments can be monitored and troubleshot after they
are created.

The initial monitoring architecture is:

- Azure Monitor: overall monitoring platform
- Application Insights: application telemetry
- Log Analytics Workspace: centralized log and query destination
- Diagnostic settings: configured for supported and useful resource
  logs and metrics
- Initial retention target: 30 days
- Query language: KQL
- Infrastructure configuration: managed through Terraform where
  applicable

The initial architecture is conceptually:

```text
                    App Service
                  .NET 10 / Linux
                        |
            +-----------+-----------+
            |                       |
            v                       v
   Application Insights      Diagnostic Settings
            |                       |
            +-----------+-----------+
                        |
                        v
               Log Analytics Workspace
                        |
                   30-day retention
                        |
                        v
                Azure Monitor / KQL
```

Monitoring configuration will be implemented progressively. The
platform will enable telemetry that provides useful operational
evidence rather than enabling every available diagnostic category
without a defined purpose.

Initial alerting will remain intentionally small and will focus on
useful workload signals such as:

- Application availability or failures
- HTTP 5xx errors
- Response time or latency when appropriate

Alert thresholds and additional monitoring rules will be selected and
tested during implementation rather than assumed during architecture
planning.

The 30-day retention target is intended for the cost-conscious
development lab. Production retention requirements would be selected
based on operational, security, compliance, and business requirements.

Monitoring data may later become a controlled read-only evidence
source for the Platform Intelligence capability. AI-generated
explanations or recommendations must remain grounded in authorized
monitoring data and will not grant the AI authority to modify
infrastructure.

## Phase 1 architecture decision status

The initial Phase 1 architecture decisions documented in this file
are complete.

Agreed decisions include:

- Naming and tagging conventions
- Primary Azure lab region
- Initial App Service workload baseline
- Initial networking profile
- Terraform remote state architecture
- Portal/backend hosting and authentication approach
- Monitoring and retention baseline

Implementation and verification of these decisions will occur
progressively in later phases. A documented architecture decision
does not imply that the corresponding Azure resource or capability
has already been deployed.