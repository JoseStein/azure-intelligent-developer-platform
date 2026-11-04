# Azure lab cleanup runbook

This is a planning document. It does not confirm that the environment is empty and must not be used without reviewing live Terraform state and Azure inventory.

## Ownership boundaries

1. Temporary AKS resources belong to `terraform/environments/aks-lab/` and state key `aks-lab/dev.tfstate`.
2. Platform resources belong to `terraform/environments/dev/` and state key `aidp-dev.tfstate`.
3. Workload resources belong to their workload state.
4. Terraform backend storage is shared infrastructure and must be handled last.

## Planned order

1. Stop application workloads and capture evidence.
2. Destroy the temporary AKS stack through its reviewed state only.
3. Verify the AKS-managed `MC_*` resource group, VMSS, disks, load balancer, public IPs, NICs, and ACR are gone.
4. Confirm no App Service traffic, deployment, or portal dependency remains.
5. Through the owning platform state, review and remove App Services and App Service Plans.
6. Review and remove Foundry and model deployments after confirming no other workload depends on them.
7. Review and remove Azure AI Search and its indexes.
8. Review and remove application Storage, private endpoints, private DNS links, and lab networking only after dependency checks.
9. Remove Application Insights and Log Analytics after exporting required evidence and checking diagnostic-setting dependencies.
10. Verify Cost Management shows no continuing lab charges.
11. Preserve and back up Terraform state and documentation.
12. Handle the Terraform backend storage account last; never delete it before state retention is no longer required.

## Safety checks

- Review each state’s plan before destroy.
- Confirm shared resources are not owned by the state being destroyed.
- Check private endpoint and DNS dependencies before deleting networking.
- Check diagnostic settings before deleting monitoring resources.
- Search for leftover public IPs, disks, registries, plans, and managed resource groups.
- Record the final resource inventory and Cost Management result.
