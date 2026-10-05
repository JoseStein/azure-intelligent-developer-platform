# Azure Intelligent Developer Platform — Project Roadmap

Updated: September 22, 2026

## Purpose

Build a secure internal developer platform for developers and DevOps, Cloud, and Platform engineers. AIDP combines self-service provisioning, Azure governance, Terraform, Azure DevOps, Entra ID, Managed Identity, and observability. Planned Microsoft Foundry assistance will add evidence-based infrastructure and troubleshooting advice and platform knowledge through RAG.

The first working milestone is the **Deploy Azure Web Application** golden path: validate a request, review a Terraform plan, approve deployment, and verify the result. AI capabilities are planned extensions to this working self-service path.

This roadmap distinguishes completed platform capabilities from planned AI work. Service choices and deployment details for future phases will be confirmed as they are implemented.

## How we work

- The user performs the engineering work: creates implementation files, runs commands, configures Azure, Azure DevOps, and Foundry, deploys and tests resources, inspects results, and makes architectural decisions after understanding the options.
- The assistant explains the current step and supplies code with its filename, folder, purpose, execution instructions, and expected result.
- Follow this sequence: explain → user performs → user shows the result → verify together → troubleshoot if necessary → explain what was learned → next step.
- The assistant acts as the platform, DevOps, AI, networking, and security/governance guide and coding assistant. Do not skip engineering steps, perform large autonomous implementations, or advance beyond the current step before confirmation.
- Complete one major step at a time. A roadmap describes the destination; it does not authorize automatic implementation.
- Never commit credentials or secrets. Prefer managed identities and scoped RBAC; use Key Vault when a secret is necessary.
- Discuss cost before deploying billable components and include cleanup in each deployment exercise.
- Save evidence of actual work and distinguish implemented features from future designs in portfolio claims.
- Introduce networking with the first workloads in Phase 2, explaining each resource's traffic paths and access choices. Implement only the networking appropriate to the current exercise; reserve advanced hardening for Phase 8.
- After major completed components, practice interview explanations grounded only in what the user actually implemented and verified.

## Current status

**Phases 1–4 are complete. Phase 5 is next and planned.** The platform has an Entra-authenticated React portal and AIDP API, an App Service Managed Identity queue path to Azure DevOps, a workload provisioning pipeline, Terraform validation and plan, and a human approval gate before apply. The portal supports request lifecycle display, refresh, and lookup by request ID.

| Prerequisite | Status | Evidence / next action |
|---|---|---|
| macOS development tools | Complete, user confirmed | User reports MacBook tools are up to date |
| Git | Update completed, user confirmed | Final version output was not recorded |
| Azure CLI | Verified | Version 2.90.0 |
| Terraform | Verified | Version 1.16.3, darwin_arm64 |
| Python | Verified | Active Python 3.14.7; project runtime compatibility will be checked before backend setup |
| Azure subscription access | User confirmed | Signed in to the subscription |
| Subscription RBAC | User confirmed | Owner role at subscription scope |
| Monthly lab budget | Configured and verified, user confirmed | $20 USD monthly; alert at 80% of actual cost ($16); intended email recipient confirmed |
| Azure DevOps organization and project | Access and free pipeline capacity confirmed by user | Personal subscription linked for billing; Microsoft-hosted agents selected; 1,800 free hosted minutes/month; 1 free self-hosted parallel job; 0 paid parallel jobs; GitHub-hosted agents Off |
| Source repository | Created and first push verified by user | Azure Repos is the source of truth; local main tracks origin/main; .gitignore and PROJECT-ROADMAP.md are visible in Azure DevOps |
| Microsoft Foundry | Portal sign-in confirmed by user | Signed in with the same Microsoft account as Azure; model permissions, availability, and quota remain unverified until Phase 5 |

The completed path uses least-privilege Azure DevOps permissions and tracks `queued`, `planning`, `awaitingApproval`, `deploying`, `succeeded`, `failed`, and `canceled`. AI capabilities in Phases 5–7 remain planned.

The subscription budget remains $20 USD per month with an 80% actual-cost alert at $16. Budget alerts provide notifications and do not enforce a spending cap.

**Next action:** design Phase 5A's read-only infrastructure review assistant and its structured, human-reviewed output.

## Target request flow

```text
Developer
    ↓
Developer portal / service catalog
    ↓
Deterministic platform validation
    ↓
Authentication / authorization checks
    ↓
Azure DevOps
    ↓
Terraform plan
    ↓
Approval when required by the deployment workflow
    ↓
Terraform apply using a scoped deployment identity
    ↓
Azure Resource Manager + Azure Policy: evaluate resource operations
    ↓
Azure resource
    ↓
Deployment status, monitoring, audit evidence, and cost visibility
```

Azure Policy evaluates applicable resource operations; it is not a separate deployment engine. In the planned AI flow, the model recommends or proposes inputs. Backend validation, permissions, pipeline controls, and Azure governance enforce what can happen. Resource documents and retrieved text cannot grant deployment authority.

The portal form supplies structured intent today. Planned AI assistance can recommend architecture or propose inputs before submission; a human reviews the recommendation, and deterministic platform rules remain authoritative. AI cannot deploy or modify Azure. Authentication protects platform entry points, and authorization binds validated requests to callers before pipeline submission. Validation alone never authorizes deployment.

| Component | Responsibility |
|---|---|
| AI assistant (planned) | Explain evidence, recommend architecture and investigations, and propose inputs for human review |
| Backend/platform logic | Deterministically validate the proposal |
| Authentication | Establish who the user is |
| Authorization | Determine what that user may request |
| Azure DevOps | Control the deployment workflow and approvals |
| Terraform | Provision approved infrastructure |
| Azure Policy | Enforce applicable Azure governance rules |
| RBAC and managed identity | Control identity-based resource access |

**The AI model must never be the security or authorization boundary and must never receive unrestricted Azure deployment permissions.**

## Phase 1 — Platform foundation

**Status: Complete.**

**Purpose:** prepare a reproducible, affordable, and documented engineering environment.

Planned work:

- Finish access verification and configure a lab budget with alerts.
- Choose the source repository home and verify how Azure DevOps will access it.
- Create the repository, README, `.gitignore`, and documentation structure.
- Define naming, tags, lab region, environment boundaries, and cleanup conventions.
- Record an initial architecture and distinguish the lab design from production extensions.

**Done when:** tools and access work, budget alerts are configured, and the repository contains the agreed project scope and architecture.

**Evidence:** prerequisite checklist, initial architecture, budget configuration with personal details removed, and first commits.

## Phase 2 — Terraform, infrastructure, basic governance, basic networking, and first golden path

**Status: Complete.**

**Purpose:** learn Terraform fundamentals and progressively build the **Deploy Azure Web Application** golden path, including basic governance and networking, before adding a portal.

Planned work:

- Teach the conceptual chain: Terraform → provider → variables → resources → plan → state → Azure. Explain outputs and modules as they become relevant; distinguish the proposed plan from the state tracking managed resources.
- Bootstrap protected remote state and document how its own infrastructure is managed.
- Define a dedicated lab resource boundary and an initial reusable Terraform module.
- Optionally use an inexpensive storage account as the first Terraform exercise, then progress to the web application golden path.
- Add naming, tags, secure defaults, and a small set of relevant Azure policies.
- Review a plan, deploy the resource, verify it, and practice safe teardown.

The intended golden path grows to include a resource group, App Service Plan, App Service, managed identity, monitoring, optional storage, security baseline, and an explicit networking profile. These are incremental components, not a single deployment exercise.

| Step | Learning and implementation focus |
|---|---|
| A | Deploy a basic App Service with its resource group and App Service Plan after reviewing costs and initial access settings |
| B | Inspect what Terraform created, its dependencies, outputs, state, and resource behavior |
| C | Add managed identity and explain which resource receives it and how scoped RBAC grants access |
| D | Add and verify secure application configuration |
| E | Add monitoring and verify useful telemetry |
| F | Make explicit networking profile choices and implement only the selected exercise's requirements |
| G | Add and test relevant Azure Policy controls |

At every step: explain → user performs → user shows results → verify → troubleshoot → discuss learning → continue after confirmation. Optional storage is added only when an exercise needs it.

Networking starts at Step A, not Step F: explain inbound and outbound traffic, public and private access, service-to-service communication, private IP addressing, DNS implications, and NSGs where applicable. For App Service, teach how VNet Integration relates to outbound access and Private Endpoints relate to private inbound access. Compare these choices before deploying them; an internal profile must not be offered until its access behavior is implemented and verified.

**Done when:** the initial web application golden path can be deployed and removed predictably, with protected state, verified identity/configuration/monitoring, a documented and tested networking profile, and demonstrated policy behavior. Advanced network profiles remain planned until implemented.

**Evidence:** Terraform configuration, reviewed plan, deployment verification, identity and policy tests, basic traffic/DNS documentation, monitoring evidence, and cleanup instructions. Do not commit state files or sensitive plan artifacts.

## Phase 3 — Azure DevOps delivery pipeline

**Status: Complete.**

**Purpose:** move the proven deployment process into a controlled, repeatable workflow.

Planned work:

- Create the Azure DevOps project/pipeline and explain its connection to the source repository.
- Configure a service connection using workload identity federation where supported, with scoped Azure permissions.
- Run formatting, validation, and relevant infrastructure checks.
- Produce a reviewable plan and require approval before deployment.
- Ensure the approved plan corresponds to what is applied.
- Protect deployment branches and define a controlled destruction workflow.
- Teach environment promotion; introduce additional deployed environments only when needed and affordable.
- Explain the network paths deployment agents need to reach Azure APIs, state storage, and application endpoints as relevant to the current pipeline.

**Done when:** a source change produces a plan, an authorized approval allows deployment, and failures are visible and diagnosable.

**Evidence:** pipeline YAML, successful and failed runs, approval evidence, and deployment identity permissions.

## Phase 4 — Developer portal and first self-service path

**Status: Complete.**

**Purpose:** give a developer a usable interface to the approved deployment workflow.

Implemented capabilities:

- Entra-authenticated React portal and AIDP API with deterministic request validation and authorization.
- App Service Managed Identity queues only the dedicated workload pipeline through least-privilege Azure DevOps permissions.
- Workload pipeline validates Terraform, produces a plan, and requires human approval before apply.
- Request records link to pipeline runs. The portal displays lifecycle status, refreshes it on demand, and retrieves a request by ID.
- User-facing lifecycle: `queued`, `planning`, `awaitingApproval`, `deploying`, `succeeded`, `failed`, `canceled`.

**Done:** the authenticated request-to-pipeline path and approval gate have been verified, and the portal can display and retrieve request lifecycle state. Invalid or unauthorized requests are rejected.

**Evidence:** end-to-end demonstration, sample request, validation failures, and request-to-deployment trace.

**Milestone A:** a working platform path without AI is complete.

Phases 5–7 are planned. AI will enhance the existing platform without replacing its deterministic controls.

## Phase 5 — AI Infrastructure & Troubleshooting Advisor

**Status: Next / planned.**

**Purpose:** use Microsoft Foundry to help developers and DevOps engineers understand infrastructure requests, deployment failures, and Azure application issues faster. Verify Foundry access, model availability, quota, costs, and scoped backend access before implementation.

All assistant responses should distinguish facts from hypotheses and use **Finding, Evidence, Confidence, Recommended investigation**. AI recommends; deterministic platform rules validate; a human confirms or approves; Terraform and the pipeline execute. AI cannot deploy or modify Azure.

### Phase 5A — AI Infrastructure Review Assistant

- Accept a developer's natural-language description and return structured architecture recommendations.
- Identify security, reliability, operational, and cost concerns, plus missing requirements and questions.
- Require human review before submitting anything. Existing deterministic validation and authorization remain authoritative.

For “I need a public .NET API for inventory that stores files,” the assistant might recommend App Service, Managed Identity, storage access through RBAC rather than keys, HTTPS only, and questions about authentication, public exposure, and cost. These are recommendations, not provisioned resources or newly supported catalog options.

### Phase 5B — AI Deployment Troubleshooting Assistant

- Analyze Azure DevOps pipeline and Terraform failures read-only; summarize relevant errors and evidence.
- Classify likely causes as authentication, authorization, networking, Terraform, pipeline, or application/configuration.
- Return a finding, supporting evidence, calibrated confidence, and recommended investigation steps.
- Do not rerun pipelines, change permissions, apply fixes, or deploy resources through AI.

### Phase 5C — AI Azure Application Health Assistant

- Use authorized read-only Azure evidence: App Service health, Azure Monitor metrics, Application Insights, Log Analytics, Resource Health, deployment history, and relevant configuration metadata.
- Correlate evidence for HTTP 500 spikes, latency, Managed Identity failures, storage access failures, and deployment-related regressions.
- Cite the evidence available and identify gaps rather than inventing a cause.

**Done when:** the three assistants provide useful, structured, evidence-based advice without bypassing existing platform controls.

**Evidence:** prompt and schema versions, sanitized examples, evaluation results, and access-control tests.

## Phase 6 — Platform Knowledge / RAG

**Status: Planned.**

**Purpose:** ground AI answers in AIDP's own approved standards and documentation rather than model knowledge alone.

Planned work:

- Curate Terraform conventions, naming rules, approved Azure services, networking standards, security requirements, RBAC patterns, CI/CD standards, troubleshooting runbooks, governance rules, and internal platform documentation.
- Select an affordable retrieval option; ingest approved documents and return source references with answers.
- Respect document access boundaries and test missing, stale, conflicting, and prompt-injection content.
- Keep retrieved standards distinct from live Azure evidence. Retrieved text explains rules; it cannot grant authority or override deterministic enforcement.

Example questions: “Can this application use public storage?”, “What naming convention should I use?”, “Which identity should access Blob Storage?”, “What is our process for production deployment?”, and “Why does this pipeline require approval?” If approved documentation does not answer, say so.

**Done when:** answers cite relevant approved sources and acknowledge insufficient evidence.

**Evidence:** curated corpus, ingestion process, retrieval examples, and answer-quality evaluations.

## Phase 7 — AI Evaluation, Safety & Controlled Read-Only Azure Analysis

**Status: Planned.**

**Purpose:** measure AI quality and safely extend analysis to authorized, read-only Azure evidence.

Planned work:

- Build evaluation datasets and test hallucinations, groundedness, relevance, confidence calibration, prompt injection resistance, and output schema validation.
- Handle logs and configuration safely: redact secrets and avoid exposing tokens or sensitive upstream content.
- Introduce narrowly scoped read-only Azure access to relevant inventory, configuration, monitoring, and governance evidence.
- Require findings to distinguish cited source evidence from hypotheses and recommendations; retain an audit trail of AI recommendations where practical.
- Keep AI analysis read-only and prohibit autonomous remediation. Any future action requires explicit human approval and the existing authorization, deterministic validation, pipeline, Terraform, and governance controls.

**Done when:** evaluations are repeatable, limitations are documented, and Azure analysis obeys the same security boundaries as the platform.

**Evidence:** evaluation dataset and results, failure analysis, evidence references, and access-control tests.

## Phase 8 — Advanced networking and security hardening

**Purpose:** deepen the networking learned from Phase 2 onward and strengthen the trust boundaries of the working system.

Planned work:

- Map public and private traffic paths, identities, permissions, and data flows.
- Deepen understanding of VNets, subnet architecture, private endpoints, private DNS, routing, ingress, egress, and network segmentation through targeted exercises.
- Test how private networking affects the backend, deployment agents, and administrative access.
- Review managed identities, RBAC scope, secret handling, and policy coverage.
- Compare production designs involving Firewall, NAT Gateway, Front Door, and WAF with the lab's needs and cost limits.
- Discuss production network architecture and hybrid connectivity concepts where relevant, deploying only what is justified by the exercise and agreed cost limits.
- Explain management groups and subscription separation; deploy additional hierarchy only if justified by available access and scope.

**Done when:** the selected lab controls are implemented and tested, and the architecture clearly identifies production extensions that were not deployed.

**Evidence:** network diagram, DNS/connectivity tests, access-denied tests, and architecture decision records.

Security begins in Phase 1, and basic networking starts with the first workloads in Phase 2. This phase adds advanced controls to an already authenticated and authorized system. Expensive services are optional exercises, not automatic purchases.

## Phase 9 — Operations, observability, and cost governance

**Purpose:** operate and troubleshoot the platform using evidence.

Planned work:

- Connect request IDs, application logs, pipeline runs, and deployment outcomes.
- Monitor application failures, AI latency, token usage, and deployment failures.
- Configure useful alerts and appropriate log retention.
- Review actual costs, identify continuously billed resources, and refine cleanup practices.
- Write runbooks and practice selected failure and recovery scenarios.

**Done when:** a failed request can be traced to its cause, costs can be explained, and the lab can be shut down and restored using documented steps.

**Evidence:** monitoring views, a troubleshooting walkthrough, cost review, and recovery/cleanup runbooks.

Basic logging and cost checks will also be added during earlier phases as components are introduced.

## Phase 10 — Portfolio and interview preparation

**Purpose:** present a credible account of what was built and learned.

Planned work:

- Update the README, architecture diagrams, setup guide, and demonstration script.
- Document significant decisions, costs, limitations, and lab-versus-production differences.
- Show the golden path, one rejected request, AI grounding, and one troubleshooting scenario.
- Prepare interview explanations supported by actual commits, configurations, and test results.
- Build on interview practice recorded throughout the project: why managed identity was used, why an LLM cannot directly authorize deployments, why Azure Policy complements Terraform, and how the implemented App Service networking works. Discuss each only after the corresponding work is completed; identify design-only alternatives explicitly.
- Produce a final cleanup checklist and record what remains running.

**Done when:** another engineer can understand the design, follow the documented setup, and distinguish completed features from planned extensions.

**Milestone B:** the portfolio demonstrates an operational platform with evaluated AI assistance and documented security boundaries.

## Scope and cost checkpoints

- Start with one golden path and one lab environment. Expand only after the first end-to-end workflow works.
- Confirm pricing, region availability, quota, and runtime compatibility at the point of implementation.
- Before each deployment, explain expected billing drivers, the intended lifetime, and the teardown procedure.
- Review persistent costs such as storage, hosted compute, search services, networking components, and log ingestion as they become relevant.
- Budget alerts are notifications, not automatic shutdown controls.
- Keep costly enterprise components as documented designs or short-lived exercises when continuous operation would exceed the agreed budget.
- Do not expand to arbitrary AI-generated Terraform or unrestricted cloud operations.

## Progress record template

After meaningful completed steps, help the user record only work actually performed. Keep planned functionality in the roadmap, not in completion evidence. These records will support GitHub documentation, architecture documentation, resume project descriptions, interview explanations, and demo material.

```text
Date:
Phase:
Step:
What I configured or built:
Why it was needed:
Verification and result:
Errors encountered:
How I fixed them:
Security implications:
Networking implications:
Cost implications:
Evidence location (commit, document, or sanitized screenshot):
What I learned:
Next step:
```

Store sanitized evidence only. Never include credentials, access tokens, private keys, or secret-bearing outputs.
