# AKS migration runbook — design only

This is an **archived design-only** runbook. No AKS deployment or App Service-to-AKS migration occurred. After choosing to end the lab, the existing AIDP App Services and backend were separately **decommissioned on November 4, 2026**; they no longer exist. The steps below are hypothetical and require rebuilding the infrastructure and updating prerequisites before use. See [the completed cleanup record](azure-cleanup-runbook.md).

## Preconditions

1. Confirm current region, quota, VM SKU availability, subnet availability, and pricing.
2. Confirm the Azure DevOps WIF identity can create the temporary subnet, AKS, ACR, managed identity, and federated credential.
3. Configure approval checks for `aidp-aks-apply` and `aidp-aks-destroy`.
4. Confirm the isolated backend key is `aks-lab/dev.tfstate`.
5. Resolve the state-storage firewall access problem for Microsoft-hosted agents.
6. Build and push the API image to ACR.
7. Render the `k8s/aks/` templates with approved non-secret values.

## Intended sequence

```mermaid
sequenceDiagram
  participant P as Azure DevOps
  participant T as aks-lab Terraform
  participant A as AKS
  participant R as ACR
  participant K as Kubernetes
  P->>T: validate
  P->>T: reviewed plan
  P->>T: approved apply
  T->>A: create cluster and subnet
  T->>R: create registry
  P->>R: push API image
  P->>K: apply rendered manifests
  K->>K: readiness/liveness checks
  P->>T: reviewed destroy plan
  P->>T: approved destroy
```

## Not executed

- AKS creation
- ACR creation or image push
- Workload Identity token exchange in Azure
- Runtime RBAC assignments
- Kubernetes deployment to AKS
- App Service cutover or DNS change
- App Service compute removal

## Failure handling

Treat an Apply failure as a partial deployment until a fresh plan proves otherwise. Inspect the isolated state, reconcile the actual Azure inventory, and use the reviewed destroy workflow only after confirming that shared platform resources are outside the temporary state.
