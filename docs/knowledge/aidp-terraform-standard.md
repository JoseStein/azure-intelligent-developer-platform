---
documentId: aidp-terraform-standard
title: AIDP Terraform Standard
category: terraformStandard
version: 1.0.0
status: active
environmentScopes: [dev]
workloadScopes: [appservice]
lastUpdated: 2026-09-25
owner: AIDP Platform Team
authority: platformStandard
tags: [terraform, plan, apply, state, pipeline]
reviewAfter: 2027-03-25
---

# Plan and apply

- Fact: terraform.saved-plan | requirement | The Terraform Apply stage applies the exact saved plan produced by the approved Plan stage.
- Fact: terraform.state-change | recommendation | A saved Terraform plan can become stale when relevant state changes after the plan was created.
- Fact: terraform.human-approval | requirement | A human approval gate separates Terraform Plan from Terraform Apply in the workload pipeline.

# Safety

- Fact: terraform.no-ai-execution | prohibition | AIDP AI assistants do not execute Terraform or describe Terraform remediation as completed.

Troubleshooting should identify the failing Terraform command and compare supplied plan, state, provider, and authorization evidence without changing infrastructure.
