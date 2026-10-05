---
documentId: aidp-pipeline-standard
title: AIDP Pipeline Standard
category: pipelineStandard
version: 1.0.0
status: active
environmentScopes: [dev]
workloadScopes: [appservice]
lastUpdated: 2026-09-25
owner: AIDP Platform Team
authority: platformStandard
tags: [azure-devops, terraform, approval, wif]
reviewAfter: 2027-03-25
---

# Pipeline execution

- Fact: pipeline.dedicated-workload | requirement | Validated workload requests queue the dedicated aidp-workload-provisioning pipeline.
- Fact: pipeline.human-approval | requirement | Terraform Plan is followed by a human approval gate before Terraform Apply.
- Fact: pipeline.wif | recommendation | Azure DevOps service connections should use workload identity federation instead of stored client secrets.

# Terraform safety

- Fact: pipeline.no-ai-execution | prohibition | AI assistants do not queue pipelines or execute Terraform.

The platform passes only validated applicationName, environment, runtime, and resourceType template parameters to the dedicated workload pipeline.
