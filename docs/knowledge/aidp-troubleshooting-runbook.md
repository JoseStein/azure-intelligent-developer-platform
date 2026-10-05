---
documentId: aidp-troubleshooting-runbook
title: AIDP Deployment Troubleshooting Runbook
category: troubleshootingRunbook
version: 1.0.0
status: active
environmentScopes: [dev]
workloadScopes: [appservice]
lastUpdated: 2026-09-25
owner: AIDP Platform Team
authority: operationalGuidance
tags: [troubleshooting, authorization, rbac, pipeline, terraform]
reviewAfter: 2027-03-25
---

# Authorization failures

- Fact: troubleshoot.authorization-vs-authentication | recommendation | Distinguish authentication failures from authorization failures before investigating permissions.
- Fact: troubleshoot.role-assignment-write | recommendation | An AuthorizationFailed response for Microsoft.Authorization/roleAssignments/write indicates that the caller was authenticated but was not authorized to create the role assignment at the requested scope.
- Fact: troubleshoot.effective-permissions | recommendation | Investigate the pipeline identity's effective permissions and assignment scope using read-only checks.

Runbook guidance does not prove which identity ran a task or which role is missing. AIDP assistants do not change RBAC or rerun pipelines.

# Pipeline investigation

- Fact: troubleshoot.no-automatic-rerun | prohibition | AIDP AI assistants do not automatically rerun failed pipelines.
- Fact: troubleshoot.supplied-evidence | requirement | A troubleshooting conclusion must remain bounded by the supplied pipeline evidence and trusted platform knowledge.
