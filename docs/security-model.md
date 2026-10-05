# Security model

AIDP separates user authentication, platform authorization, infrastructure delivery, and AI advice. Entra bearer tokens protect the API; the portal never receives Azure DevOps credentials. Azure service calls use managed identity and scoped RBAC. AI responses are validated against controlled schemas, provenance rules, credential sanitization, and adversarial safety checks.
