# Portfolio summary

## Project overview

The AIDP Kubernetes learning track demonstrates how an existing .NET API can be containerized, run locally on Kubernetes, and prepared for a controlled Azure migration without overstating cloud deployment results.

## Implemented and tested

- Multi-stage .NET 10 Docker image.
- Local Docker execution with a healthy `/health` endpoint.
- Docker Desktop Kubernetes Deployment and ClusterIP Service.
- Namespace migration from `default` to `aidp-dev`.
- ConfigMap-based environment configuration.
- CPU and memory requests/limits.
- Readiness and liveness probes.
- Scaling from one to two replicas.
- Rolling update and rollback using image tags.
- Diagnosis and recovery of ImagePullBackOff, readiness-probe failure, and CrashLoopBackOff.
- Troubleshooting documentation based on observed local behavior.

## Implemented and locally validated, not deployed to Azure

- Temporary AKS Terraform root.
- AzureRM provider lock at 4.81.0.
- Azure CNI Overlay configuration.
- ACR, AKS, identity, OIDC federation, and scoped RBAC resource definitions.
- Manual-only Azure DevOps pipeline design.

The AKS Validate stage passed. Terraform Plan did not complete because the Microsoft-hosted agent could not reach the firewall-restricted remote state account and received HTTP 403.

## AKS migration tasks not executed

- Provisioning AKS or ACR.
- Pushing an image to ACR.
- Deploying the API to AKS.
- Verifying Workload Identity against Azure services.
- Migrating traffic from App Service.
- Destroying App Service compute *as part of a migration* (the legacy lab infrastructure was instead destroyed in a separate, completed teardown).

## Accurate resume bullets

- Containerized a .NET 10 API with a multi-stage Docker build and validated health behavior locally.
- Deployed and operated the API on Docker Desktop Kubernetes using namespaces, ConfigMaps, Services, probes, resource limits, scaling, rollouts, and rollback.
- Built and validated a separate Terraform AKS foundation with isolated state design, Azure CNI Overlay, OIDC federation, and least-privilege identity planning.
- Authored Azure DevOps pipeline controls for manual Terraform validation, plan, approval-gated apply, and reviewed destroy workflows.
- Documented realistic Kubernetes failure modes and recovery procedures using observed local lab results.


## Completed: safe Azure lab decommissioning (November 4, 2026)

After deciding not to provision AKS, I retired the existing AIDP lab instead of leaving paid Azure resources running.

- Reviewed and applied the Inventory API workload's Terraform destroy plan: **3 resources destroyed**.
- Reviewed the main platform's Terraform destroy plan: **35 resources targeted**. A Foundry purge connection error caused partial completion, so I compared Azure inventory with refreshed Terraform state instead of blindly retrying.
- Terraform subsequently blocked the resource-group deletion because an unmanaged **Application Insights Smart Detection** action group remained. I verified its exact identity, deleted only this lab resource, generated a new saved plan, and removed the now-empty platform group.
- Kept the remote Terraform state backend until the end; verified its five state blobs and saved private copies on my Mac. Deleted the storage account and backend group after dependency and ownership checks.
- Verified both AIDP resource groups were absent and an Azure CLI query returned **zero AIDP-named resources**.

**Scope clarification:** This is a completed Azure teardown, **not** a completed App Service-to-AKS migration. AKS/ACR were never provisioned. The resource inventory does not establish a settled $0 invoice; Azure Cost Management may lag. Sensitive state backups remain private and must not be committed.

See [Azure cleanup record](azure-cleanup-runbook.md).

## Additional résumé bullet

- Executed a controlled, cost-conscious Azure lab teardown using separate Terraform states, reviewed saved plans, Azure CLI verification, and secure remote-state backup; resolved partial Foundry deletion and unmanaged monitoring-resource blockers.
