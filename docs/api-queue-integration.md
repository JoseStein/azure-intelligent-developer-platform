# API to workload pipeline queueing

Queueing is **disabled by default** in appsettings.json and in the options class.
With `AzureDevOps:QueueEnabled=false`, authenticated POST /api/requests still only
validates, stores the request with status `validated`, and returns HTTP 201.
No Azure DevOps token is requested and no queue HTTP call is made in that path.

When explicitly enabled later, the server acquires an Azure DevOps token using
DefaultAzureCredential and scope `https://app.vssps.visualstudio.com/.default`.
In Azure this uses the platform API App Service's system-assigned identity.
The browser's API token is never forwarded to Azure DevOps. No PAT or client
secret is needed. HTTP client logging is disabled for the queue client, redirects
are disabled, and the queue POST has no retry policy.

The server constructs only the four validated template parameters and the
configured repository branch. Options require pipeline 1 and refs/heads/main;
request JSON cannot select a pipeline, branch, YAML, variable, or stage override.
POST authorization and validation precede creation and queueing.

## Outcomes

- Accepted response with positive run ID and matching pipeline ID: `queued`,
  `PipelineRunId`, `QueuedAt`, and `QueueOutcome=accepted`; return HTTP 201.
- Explicit 4xx rejection (except timeout 408): `failed`, sanitized error code,
  `QueueOutcome=rejected`; return HTTP 502 ProblemDetails with request ID.
- Token acquisition failure before sending: `failed`, `queueAuthenticationFailed`.
- Timeout, connection loss, 408, 5xx, redirects, or unusable success response:
  retain `validated`, record `QueueOutcome=unknown`, and return HTTP 502 stating
  that a run may exist and the user must check before resubmitting.

Raw upstream error bodies, access tokens, and authorization headers are never
returned or stored. Validated does not mean the queue outcome is known: inspect
QueueOutcome and QueueErrorCode after a failure. No automatic retries occur.

The in-memory store atomically claims each request before the queue attempt and
atomically records its result. A claimed/completed request cannot be claimed
again. This is NOT durable idempotency: another POST creates another record, and
restarts lose all records. If the server stops after Azure DevOps accepts a run,
manual reconciliation is required. Do not automatically resubmit ambiguous
requests. Durable storage, idempotency, and lifecycle monitoring are future work.

## App Service settings for a later authorized deployment

No Azure settings have been changed by this implementation. Add the following
to the **platform API** App Service through its managed deployment configuration:

```text
AzureDevOps__Organization=example-org
AzureDevOps__Project=aidp-public-platform
AzureDevOps__WorkloadPipelineId=3
AzureDevOps__WorkloadBranch=refs/heads/main
AzureDevOps__QueueEnabled=false
```

Keep QueueEnabled false until deployment, authorization, pipeline approval checks,
and a controlled live test have been explicitly authorized. The configured
identity must have View build pipeline, View builds, and Queue builds on the
workload pipeline. Existing environment approval remains responsible for Apply.
No Terraform/pipeline definition changes are required for this increment.

## Local verification

```sh
dotnet build src/Aidp.Api/Aidp.Api.csproj
dotnet run --project tests/Aidp.Api.AuthChecks/Aidp.Api.AuthChecks.csproj
```

The existing console test host now also tests the real queue client with an
injected fake TokenCredential and fake HTTP handler. It exercises disabled,
accepted, rejected, timeout, connection-loss, malformed/incomplete/wrong-pipeline
responses, 5xx, exact payload restrictions, invalid/unauthorized requests, and
single-attempt atomic ownership. It never queues a real pipeline.
