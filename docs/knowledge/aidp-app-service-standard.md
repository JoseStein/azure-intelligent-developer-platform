---
documentId: aidp-app-service-standard
title: AIDP App Service Standard
category: architectureStandard
version: 1.0.0
status: active
environmentScopes: [dev]
workloadScopes: [appservice]
lastUpdated: 2026-09-25
owner: AIDP Platform Team
authority: platformStandard
tags: [appservice, https, identity, monitoring]
reviewAfter: 2027-03-25
---

# Application hosting

- Fact: appservice.existing-plan | requirement | Development App Service workloads use the existing AIDP development App Service Plan.
- Fact: appservice.https-only | requirement | Provisioned App Service applications use HTTPS only.
- Fact: appservice.ftps-disabled | requirement | Provisioned App Service applications have FTPS disabled.

# Runtime and identity

- Fact: appservice.runtime | requirement | The supported App Service runtime is DOTNETCORE 10.0.
- Fact: appservice.managed-identity | requirement | Provisioned App Service applications use a system-assigned Managed Identity.

Application Insights may be recommended for observability, but it is not a separately supported workload component in the current provisioning catalog.
