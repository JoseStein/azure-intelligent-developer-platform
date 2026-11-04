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

## Planned but not completed

- Provisioning AKS or ACR.
- Pushing an image to ACR.
- Deploying the API to AKS.
- Verifying Workload Identity against Azure services.
- Migrating traffic from App Service.
- Destroying App Service compute after migration.

## Accurate resume bullets

- Containerized a .NET 10 API with a multi-stage Docker build and validated health behavior locally.
- Deployed and operated the API on Docker Desktop Kubernetes using namespaces, ConfigMaps, Services, probes, resource limits, scaling, rollouts, and rollback.
- Built and validated a separate Terraform AKS foundation with isolated state design, Azure CNI Overlay, OIDC federation, and least-privilege identity planning.
- Authored Azure DevOps pipeline controls for manual Terraform validation, plan, approval-gated apply, and reviewed destroy workflows.
- Documented realistic Kubernetes failure modes and recovery procedures using observed local lab results.
