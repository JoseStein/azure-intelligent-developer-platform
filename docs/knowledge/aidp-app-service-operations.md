---
documentId: aidp-app-service-operations
title: AIDP App Service Operations Guidance
category: operationalProcedure
version: 1.0.0
status: active
environmentScopes: [dev]
workloadScopes: [appservice]
lastUpdated: 2026-09-25
owner: AIDP Platform Team
authority: operationalGuidance
tags: [appservice, deployment, correlation, logs, resourcehealth]
reviewAfter: 2027-03-25
---

# Deployment correlation

- Fact: operations.deployment-correlation | recommendation | Deployment timing alone does not prove that a deployment caused a health change.
- Fact: operations.deployment-evidence | recommendation | Compare supplied deployment metadata with bounded request, dependency, and application evidence before treating deployment timing as causal.

# Read-only investigation

- Fact: operations.read-only-health | requirement | AIDP health analysis recommends read-only investigation and does not claim that Azure was queried unless trusted API code supplied the result.
- Fact: operations.no-automatic-change | prohibition | AIDP health analysis does not automatically restart, scale, or change App Service configuration.
