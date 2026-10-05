---
documentId: aidp-health-runbook
title: AIDP Application Health Runbook
category: healthRunbook
version: 1.0.0
status: active
environmentScopes: [dev]
workloadScopes: [appservice]
lastUpdated: 2026-09-25
owner: AIDP Platform Team
authority: operationalGuidance
tags: [health, http5xx, latency, availability, dependency]
reviewAfter: 2027-03-25
---

# Evidence interpretation

- Fact: health.http5xx-symptom | recommendation | Elevated HTTP 5xx responses are a health symptom and do not by themselves establish a root cause.
- Fact: health.dependency-investigation | recommendation | When latency is elevated without corresponding CPU pressure, investigate supplied dependency evidence before attributing the issue to compute capacity.
- Fact: health.direct-evidence | requirement | Application health conclusions require direct supplied evidence and cannot be established from runbook guidance alone.

Use bounded read-only checks to compare availability, request failures, dependency behavior, and application logs. Runbook guidance does not provide observed metric values.

# Safety

- Fact: health.no-remediation | prohibition | AIDP health analysis does not restart, scale, reconfigure, or otherwise remediate an application.
