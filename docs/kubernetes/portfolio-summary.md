# AIDP engineering case study

### Azure · Terraform · Azure DevOps · Kubernetes · Production-style troubleshooting

[← Project home](../../README.md) · [Kubernetes overview](README.md) · [Azure teardown report](azure-cleanup-runbook.md)

## The opportunity

**Problem:** How do you build a governed Azure self-service developer platform, explore moving a .NET API from App Service toward Kubernetes, and demonstrate the full cloud lifecycle without paying indefinitely for a training environment?

**My role:** Cloud / DevOps engineer responsible for infrastructure design, Terraform changes, pipeline controls, hands-on Kubernetes operations, troubleshooting, resource verification, and teardown. Code assistance was used for repository tasks; documented infrastructure operations and engineering decisions are the focus here.

**Outcome:** Built and troubleshot the API in **local Kubernetes**, prepared and validated an **AKS migration design**, and **removed all identified AIDP-named Azure resources** after saving private Terraform state backups. AKS was **not deployed**.

## Three engineering stories

| Engineering challenge | What I did | What it demonstrates |
| --- | --- | --- |
| **01 · Operate containers** | Built a multi-stage .NET 10 Docker image, deployed it to Docker Desktop Kubernetes with ConfigMap, Service, probes, CPU/memory controls, and two-replica scaling | Containers, Kubernetes primitives, release operations |
| **02 · Diagnose failure** | Intentionally triggered ImagePullBackOff, readiness HTTP 404, and CrashLoopBackOff; used `kubectl describe`, events, logs, endpoints, and rollout undo to recover | Root-cause analysis, healthy traffic routing, rollback |
| **03 · Decommission responsibly** | Reviewed state-scoped Terraform destroy plans; reconciled a failed Foundry purge; isolated and deleted an unmanaged monitoring action group; backed up state and retired its storage account | Change control, state ownership, Azure CLI, cost management discipline |

## Technical results and evidence

| Category | Verified result | Reference |
| --- | --- | --- |
| Container | `GET /health` returned HTTP 200 | [Local deployment](local-deployment.md) |
| Kubernetes | `aidp-dev` namespace, Deployment, Service, ConfigMap, probes, scaling, rolling updates and rollback | [Local deployment](local-deployment.md) |
| Reliability | Reproduced and recovered from three failure types | [Troubleshooting guide](troubleshooting-guide.md) |
| AKS architecture | Terraform root, network/IP design, ACR, OIDC federation and identity/RBAC plan locally validated | [AKS design](aks-architecture.md) |
| Azure DevOps | Manual-only pipeline definition, Validate passed; Plan blocked by backend HTTP 403 | [Pipeline design](aks-pipeline-design.md) |
| Azure cleanup | Workload destroy: **3**; initial platform plan: **35** targets; backed up **5** state blobs; final AIDP-named resource search empty | [Teardown record](azure-cleanup-runbook.md) |

## Example real-world incident: Terraform resource-group deletion blocked

**Symptom:** Terraform reported that a resource group could not be deleted because it contained a resource not managed by the current state.

**Investigation:** Azure inventory showed the remaining `Application Insights Smart Detection` action group, automatically created by Azure. The AzureRM provider's safeguard correctly stopped deletion.

**Resolution:** I verified the action group's scope, deleted that lab-only resource explicitly, generated and inspected a **new saved Terraform destroy plan**, applied the new plan, and confirmed the resource group no longer existed.

**Lesson:** Do not disable infrastructure safeguards just to make a deployment succeed. Determine ownership, reconcile real cloud state, and apply the smallest reviewed change.

## Deliberate limits

- **Local Kubernetes:** completed hands-on.
- **AKS/ACR cloud deployment:** **never provisioned**. The Azure DevOps Validate stage passed, but the Plan stage encountered a firewall-restricted Terraform state backend HTTP 403. No application cutover occurred.
- **Original App Services:** deleted afterward as part of the **lab teardown**, not as the result of a Kubernetes migration.
- **Costs:** resource-group existence checks returned `false`, and a name-based inventory found no AIDP-named resources. This is **not** a confirmation that a final Azure bill has settled at $0.
- **Sensitive files:** private Terraform state backups were **not published**.

## Interview and résumé talking points

> **Kubernetes operations:** Containerized a .NET 10 API and ran it in a local Kubernetes cluster, including ConfigMaps, readiness/liveness checks, scaling, Service routing, rolling updates, and rollback.

> **Troubleshooting:** Reproduced and resolved ImagePullBackOff, unhealthy readiness probes, and CrashLoopBackOff using Pod events, container logs, endpoints, and controlled rollbacks.

> **Infrastructure as Code:** Designed and locally validated an AKS/ACR Terraform architecture with Azure CNI Overlay, Workload Identity federation, and explicit deployment controls; identified a remote-state network blocker before any cloud cluster deployment.

> **Azure lifecycle:** Completed a state-aware Azure lab teardown, reconciled a partially completed Terraform destroy, resolved an unmanaged monitoring dependency, and removed the remote-state backend only after private backups and verification.

---

**Further reading:** [Full Kubernetes portfolio](README.md) · [Architecture overview](../../docs/architecture-overview.md) · [Azure incident / cleanup report](azure-cleanup-runbook.md)
