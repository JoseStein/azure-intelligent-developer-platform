# Azure Intelligent Developer Platform (AIDP) — Portfolio Roadmap

This roadmap summarizes the major engineering milestones completed in AIDP and the remaining portfolio-focused work. It is intentionally concise and recruiter-facing rather than a step-by-step development diary.

## Project goal

Build a secure Azure developer platform that combines:

- self-service infrastructure provisioning;
- Terraform-based Infrastructure as Code;
- Azure DevOps CI/CD;
- Microsoft Entra ID authentication;
- Managed Identity and least-privilege RBAC;
- private networking and DNS;
- centralized observability;
- Microsoft Foundry-backed AI assistants;
- trusted RAG;
- deterministic AI safety and evaluation controls.

The platform is designed so AI can explain, review, troubleshoot, and analyze evidence, while deployment authority remains with deterministic validation, Azure DevOps, Terraform, RBAC, Azure Policy, and human approval.

## Current status

### Complete — Platform foundation

- Azure lab architecture and cost controls defined.
- Terraform remote state and state-security model implemented.
- Naming, tagging, environment, and governance conventions established.
- Azure Repos private engineering workflow and separate sanitized public GitHub portfolio created.

### Complete — Terraform and golden-path infrastructure

- App Service platform and workload infrastructure delivered through Terraform.
- Managed Identity and scoped RBAC implemented.
- Networking architecture includes VNet integration, Private Endpoints, and Private DNS.
- Storage hardening includes Entra-based access patterns and default-deny network rules.
- Microsoft Foundry private connectivity implemented.
- Diagnostic settings centralized into Log Analytics.

### Complete — Azure DevOps delivery

- YAML-based platform and workload pipelines implemented.
- Workload Identity Federation used instead of long-lived service principal secrets.
- Terraform plan/apply flow protected by approval gates.
- Terraform state access and temporary hosted-agent firewall access are controlled.
- Deployment and infrastructure responsibilities are separated between platform and workload pipelines.

### Complete — Self-service developer portal

- React/Vite portal integrated with Microsoft Entra ID.
- Authenticated API supports structured workload requests.
- Request lifecycle is tracked through planning, approval, deployment, success, failure, or cancellation.
- Portal can display and retrieve request status.

### Complete — AI-assisted platform capabilities

AIDP includes security-bounded assistants for:

- infrastructure review;
- deployment troubleshooting;
- application health analysis.

The assistants use structured outputs, deterministic validation, bounded evidence, and human-reviewed recommendations.

### Complete — Trusted platform knowledge / RAG

- Curated platform documentation is used as trusted retrieval context.
- Retrieved content explains standards and operational guidance but cannot override authorization or deployment controls.
- Missing or insufficient evidence is handled explicitly instead of being invented.

### Complete — AI evaluation and safety

The project includes deterministic evaluation suites covering:

- groundedness;
- hallucination resistance;
- prompt injection;
- provenance manipulation;
- false tool/action claims;
- confidence calibration;
- unsupported causal conclusions;
- output schema enforcement.

AI remains advisory and read-only.

### Complete — Controlled live Azure evidence

The health-analysis path can use trusted, read-only evidence from:

- Azure Monitor metrics;
- Application Insights;
- Azure Resource Health;
- App Service deployment metadata.

The system uses fixed resource catalogs and bounded queries rather than arbitrary Azure resource IDs or arbitrary KQL supplied by the model.

### In progress — Advanced security and networking hardening

Completed hardening includes:

- private Foundry connectivity;
- private storage connectivity;
- default-deny storage network access;
- Entra-only access where supported;
- Managed Identity;
- scoped RBAC;
- Azure DevOps Workload Identity Federation;
- restricted pipeline permissions;
- centralized diagnostic settings.

Remaining evaluation areas include:

- final App Service inbound-access posture;
- additional production-grade ingress patterns;
- further cost/security trade-off analysis for private networking and monitoring.

### Next — Operations, observability, and cost governance

Planned portfolio enhancements include:

- clearer correlation across requests, pipeline runs, deployments, and telemetry;
- additional alerting and operational dashboards;
- cost-governance review and optimization;
- clearer production-vs-lab architecture decisions.

### Final portfolio phase

The final phase focuses on recruiter and interview readiness:

- polished README and architecture diagrams;
- sanitized screenshots;
- concise security and AI-safety documentation;
- GitHub Featured placement on LinkedIn;
- interview-ready explanations of architecture, trade-offs, troubleshooting, and security decisions.

## Key engineering principles

1. **Security before convenience** — Managed Identity, WIF, scoped RBAC, private networking, and deterministic validation are preferred over shared secrets or unrestricted access.
2. **AI is not an authorization boundary** — AI can recommend and explain, but it cannot deploy infrastructure, alter RBAC, rerun pipelines, restart resources, or approve changes.
3. **Human approval remains in the deployment path** — Terraform changes are reviewed through controlled pipelines and approval gates.
4. **Trusted evidence over model guessing** — live Azure analysis uses bounded, read-only, provenance-aware evidence.
5. **Portfolio transparency** — this public repository is sanitized and contains no live credentials, private environment identifiers, Terraform state, or operational secrets.

## Recruiter takeaway

AIDP demonstrates hands-on experience across Azure Cloud Engineering, DevOps, Platform Engineering, DevSecOps, observability, private networking, Infrastructure as Code, and secure AI platform integration in one end-to-end project.
