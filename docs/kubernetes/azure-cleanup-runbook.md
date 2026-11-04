# Azure lab decommissioning — completed execution record

**Status:** AIDP Azure teardown completed November 4, 2026. This is an operational record, not a proposed deletion sequence.

## Objective

Retire the temporary Azure Intelligent Developer Platform (AIDP) lab after local Kubernetes learning and AKS architecture planning, eliminating ongoing infrastructure usage while retaining Terraform, pipeline definitions, Kubernetes manifests, troubleshooting documentation, and private backups.

**AKS was never deployed.** There was no AKS cluster, ACR instance, managed `MC_*` group, or live migration to destroy.

## Terraform ownership and scope

| Component | Terraform ownership / Azure resource group | Outcome |
| --- | --- | --- |
| Inventory API | `terraform/workloads/app-service/`, state `workloads/dev/inventory-api.tfstate` | Destroyed |
| Shared AIDP platform | `terraform/environments/dev/`, state `aidp-dev.tfstate`, group `rg-aidp-dev-scus` | Destroyed |
| Proposed AKS | `terraform/environments/aks-lab/`, proposed state `aks-lab/dev.tfstate` | Design only, never deployed |
| State backend | `staidptfstatescus` in `rg-aidp-tfstate-scus`, container `tfstate` | Five state blobs backed up privately; backend deleted last |

Unrelated resource groups, other projects, and shared subscriptions were outside the deletion scope.

## Execution timeline and evidence

1. **Inventory and dependency checks.** Reviewed Terraform state addresses, Azure resource listings, resource-group locks, and backend ownership. Saved evidence and decided not to keep the lab running.
2. **Workload destruction.** Reviewed saved plan (`0 add, 0 change, 3 destroy`); applied successfully (**3 destroyed**).
3. **Platform destruction.** Reviewed a saved plan with **35 resources to destroy**; most were deleted, but the operation ended while purging the Foundry cognitive account, reporting a reset connection.
4. **Reconcile partial apply.** Azure queries showed no active or listed soft-deleted Foundry account. A new Terraform plan showed only one remaining deletion: the platform resource group.
5. **Diagnose the resource-group blocker.** Terraform's `prevent_deletion_if_contains_resources` protection rejected removing the group while the unmanaged `Application Insights Smart Detection` action group remained.
6. **Remediate safely.** Verified that action group's exact resource ID and that it was the group's sole remaining resource. Deleted it individually with `az monitor action-group delete` (without `--yes`, which this CLI version did not support).
7. **Complete platform destroy.** Created and reviewed a **fresh saved** one-resource Terraform destroy plan. Applied it. `az group exists --name rg-aidp-dev-scus` returned `false`.
8. **Retain backend until last.** Confirmed `rg-aidp-tfstate-scus` contained only `staidptfstatescus`. Listed five AIDP state blobs, downloaded them to a private Mac directory, and confirmed five nonempty backups.
9. **Delete backend.** Deleted `staidptfstatescus`, verified its resource group was empty, then deleted `rg-aidp-tfstate-scus`. Azure returned `false` for its existence check.
10. **Final inventory.** A name-based Azure CLI resource query (resource name or resource group containing `aidp`) returned no results.

## Operational lessons

- **Terraform is not a transaction.** A failed final purge did not undo previously deleted resources. Read Azure's actual inventory and refresh state before retrying.
- **Preserve safety guards.** The resource-group protection discovered an unmanaged monitoring action group; disabling the guard would have hidden the dependency.
- **Replan after drift or partial operations.** Inspect the exact delete addresses and apply a fresh saved plan, not a stale one.
- **Treat state as sensitive.** Terraform state may contain credentials and metadata. Back up privately; do not publish `.tfstate` or saved plans.
- **Delete backends last.** Keep remote state available until every root has completed its destroy and the state files are accounted for.

## Read-only verification commands

```bash
az group exists --name rg-aidp-dev-scus
az group exists --name rg-aidp-tfstate-scus

az resource list \
  --query "[?contains(name, 'aidp') || contains(resourceGroup, 'aidp')].{name:name,resourceGroup:resourceGroup,type:type}" \
  --output table
```

Observed final output: `false`, `false`, and no rows in the AIDP-named resource listing.

## Scope, cost, and recovery

This verifies **the resource groups and name-based inventory**, not every resource in the subscription. Cost Management entries can post after deletion; no independently confirmed final $0 invoice is claimed. Existing App Services and other AIDP platform infrastructure were retired **without an AKS migration**. The Terraform backend no longer exists; rebuilding requires a new backend, new Azure infrastructure, valid service connections and access, and a fresh cost assessment. Preserve private state backups securely and never commit them to GitHub.
