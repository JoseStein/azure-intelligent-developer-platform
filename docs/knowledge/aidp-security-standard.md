---
documentId: aidp-security-standard
title: AIDP Security Standard
category: securityStandard
version: 1.0.0
status: active
environmentScopes: [all]
workloadScopes: [all]
lastUpdated: 2026-09-25
owner: AIDP Platform Team
authority: platformStandard
tags: [managed-identity, rbac, secrets, authentication]
reviewAfter: 2027-03-25
---

# Identity and secrets

- Fact: security.managed-identity | recommendation | Prefer Managed Identity over passwords, client secrets, and access keys for service-to-service authentication.
- Fact: security.least-privilege | requirement | Grant identities only the minimum permissions required at the narrowest practical scope.
- Fact: security.no-browser-ado-credentials | prohibition | Azure DevOps credentials must not be exposed to the browser.

# AI safety boundary

- Fact: ai.advisory-only | requirement | AIDP AI assistants are advisory and read-only and do not deploy, remediate, change permissions, or approve infrastructure.
- Fact: ai.human-control | requirement | Deterministic validation and human approval remain authoritative before infrastructure execution.
