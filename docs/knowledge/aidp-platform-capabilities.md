---
documentId: aidp-platform-capabilities
title: AIDP Platform Capabilities
category: platformCapability
version: 1.0.0
status: active
environmentScopes: [dev]
workloadScopes: [appservice]
lastUpdated: 2026-09-25
owner: AIDP Platform Team
authority: platformCapability
tags: [capability, appservice, dotnet10]
reviewAfter: 2027-03-25
---

# Supported provisioning capability

- Fact: provision.appservice-dotnet10-dev | capability | AIDP can provision an Azure App Service application with resourceType appservice, runtime dotnet10, and environment dev.

The deterministic provisioning workflow currently supports only this workload combination. Other Azure services may appear as advisory recommendations, but they are not currently provisionable by AIDP.

# Authority

- Fact: provision.request-authoritative | requirement | The validated provisioning request remains authoritative and AI recommendations cannot change its application name, runtime, environment, or resource type.
