# Temporary AKS pipeline design

`azure-pipelines-aks.yml` is a preserved, manual-only pipeline **design** for the temporary `terraform/environments/aks-lab/` root. **It is not currently runnable:** the dedicated Azure Terraform backend and original AIDP resources were deleted on November 4, 2026. To reuse it, an engineer must rebuild and verify the backend, service connection, state isolation, RBAC, firewall access and environment approvals, then assess current costs. The Validate stage passed during the lab; the Plan stage encountered a remote-state firewall HTTP 403, and no AKS apply or destroy ran.

## Safety boundaries

- `trigger: none` and `pr: none`; pushes and pull requests cannot start this pipeline.
- The platform and workload pipelines are unchanged.
- Every Terraform command runs from `terraform/environments/aks-lab/`.
- The pipeline asserts that `backend.tf` uses only `aks-lab/dev.tfstate`.
- Plan files are published as pipeline artifacts and apply consumes the reviewed plan artifact.
- Apply requires the exact parameter confirmation `APPLY-AKS` and the `aidp-aks-apply` environment approval.
- Destroy requires the exact parameter confirmation `DESTROY-AKS` and the separate `aidp-aks-destroy` environment approval.
- No destroy action is available from a normal push, pull request, or an accidental parameter default.

## Operations

- `validate`: backend-disabled formatting/init/validation only.
- `plan`: remote-state plan artifact; no resource changes.
- `apply`: approved plan only.
- `destroy`: reviewed destroy plan followed by explicit approval.

The Azure service connection is `sc-aidp-dev` and uses the existing workload-identity federation convention. The pipeline identity must be verified in Azure before use. Repository code cannot prove that it may create AKS, ACR, subnets, user-assigned identities, federated credentials, or role assignments.

## Required access and bootstrap

The pipeline needs read/write access to the isolated `aks-lab/dev.tfstate` blob and resource permissions for the temporary subnet, AKS, ACR, and identities. Runtime `AcrPull`, Foundry, Search, and Storage assignments remain disabled in Terraform. If the constrained RBAC bootstrap cannot authorize those exact role definitions, perform a separately reviewed bootstrap update; do not grant Owner or unrestricted User Access Administrator.

The API workload still needs `AcrPull` for the AKS kubelet identity and the approved data-plane roles for its user-assigned identity. These are prerequisites, not automatic pipeline side effects.

Before the first plan, configure and verify in Azure DevOps:

1. Register this file as a separate pipeline with no CI/PR trigger.
2. Confirm `sc-aidp-dev` is the intended service connection and uses WIF/OIDC.
3. Grant the pipeline identity only the permissions approved for this temporary root.
4. Create `aidp-aks-apply` and `aidp-aks-destroy` environments with required reviewers/checks. A missing environment causes the deployment job to fail; it does not bypass approval.
5. Confirm the hosted-agent network can reach the Terraform state account. If the state firewall blocks hosted agents, use a separately reviewed, temporary access procedure and always remove it afterward; this pipeline does not silently open the firewall.

**Do not run an Apply or Destroy operation until both environments exist and their approval/check configuration has been verified manually in the Azure DevOps UI.** Referencing an environment in YAML does not itself create an approval check, and an environment without checks is not a safety boundary.

The pipeline artifact is produced and consumed within the same run. Azure DevOps pipeline artifacts are immutable for that run, but the plan can contain sensitive infrastructure details. Restrict pipeline permissions and retention accordingly, and do not publish plan text or credentials to logs.

## Failure and cleanup

If apply fails, inspect the job log and the isolated state before retrying. Do not rerun blindly. Reconcile any partial resources with a new plan. If the lab is abandoned, run the explicit destroy operation only after reviewing its plan and checking the AKS-managed `MC_*` resource group for leftover VMSS, disks, load balancer, public IP, or NIC resources. Shared platform resources and backend state are owned by other roots and must not be destroyed here.

Keep the cluster deployed only for the agreed learning window. Capture evidence, validate the application, and start the destroy operation immediately afterward. After destroy, inventory the subscription and Cost Management for AKS nodes/VMSS, managed disks, load balancer/public IPs, ACR storage, Log Analytics, Application Insights, and any other lab resources. Remove remaining platform resources only through their owning Terraform states, and preserve the remote state account until the final documented cleanup step.
