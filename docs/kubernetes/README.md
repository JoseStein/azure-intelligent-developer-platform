# AIDP Kubernetes learning track

This documentation records the containerization and Kubernetes work completed for the Azure Intelligent Developer Platform (AIDP). It deliberately separates hands-on local results from Azure AKS design work that was never provisioned.

## Status at a glance

| Area | Status | Evidence |
| --- | --- | --- |
| .NET API Docker image | Implemented and tested locally | Root `Dockerfile`, `aidp-api:local`, `/health` returned HTTP 200 |
| Docker Desktop Kubernetes | Implemented and tested locally | `k8s/local/`, namespace `aidp-dev` |
| ConfigMap, probes, resources | Implemented and tested locally | Local Deployment rollout and `kubectl exec` verification |
| Scaling, rolling update, rollback | Implemented and tested locally | `docs/kubernetes/troubleshooting-guide.md` |
| Failure diagnosis | Implemented and tested locally | Image pull, readiness, and crash-loop exercises |
| AKS Terraform foundation | Implemented and locally validated | `terraform/environments/aks-lab/`, AzureRM 4.81.0 |
| AKS Azure deployment | Planned, not executed | No AKS, ACR, or AKS managed resource group was provisioned |
| App Service-to-AKS migration | Not completed | Existing App Service infrastructure remains |

## Contents

- [Local deployment](local-deployment.md)
- [AKS architecture](aks-architecture.md)
- [AKS migration runbook](aks-migration-runbook.md)
- [Troubleshooting guide](troubleshooting-guide.md)
- [Portfolio summary](portfolio-summary.md)
- [Azure cleanup runbook](azure-cleanup-runbook.md)
- [Pipeline design](aks-pipeline-design.md)

The AKS pipeline is intentionally manual-only (`trigger: none`, `pr: none`). It was designed but not run against Azure after the remote-state firewall 403 was identified.
