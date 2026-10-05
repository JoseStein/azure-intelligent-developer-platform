---
documentId: aidp-known-limitations
title: AIDP Known Limitations
category: knownLimitation
version: 1.0.0
status: active
environmentScopes: [all]
workloadScopes: [all]
lastUpdated: 2026-09-25
owner: AIDP Platform Team
authority: knownLimitation
tags: [limitations, storage, ai, retrieval]
reviewAfter: 2027-01-25
---

# Current platform limitations

- Fact: limitation.in-memory-requests | limitation | Application request records are stored only in the API in-memory request store.
- Fact: limitation.single-workload | limitation | The only currently supported provisioning workload is App Service with dotnet10 in dev.
- Fact: limitation.health-supplied-evidence | limitation | The application health assistant analyzes supplied evidence and does not query Azure directly.

Unsupported Azure services remain advisory recommendations only. AI assistants do not perform autonomous remediation.
