# Local Entra authentication

Queueing remains disabled: POST only validates and stores requests in memory.
There is no Development authentication bypass and no Easy Auth configuration.

## Start locally

1. In `src/Aidp.Portal`, copy `.env.example` to `.env.local` if not already present.
   These values are public registration identifiers, not secrets.
2. Trust the local API certificate if necessary: `dotnet dev-certs https --trust`.
3. Start the API from the repository root:
   `dotnet run --project src/Aidp.Api --launch-profile https`.
4. Start the portal in `src/Aidp.Portal`:
   `npm run dev -- --port 5173 --strictPort`.
5. Open **http://localhost:5173** (the registered redirect URI), sign in with the
   assigned lab user, then submit a request. Authentication redirects may reset
   an unsent form; no request is automatically replayed after sign-in.

The API runs at https://localhost:7227. Development CORS continues to permit only
http://localhost:5173 and http://127.0.0.1:5173; use localhost for Entra redirects.
Restart Vite after changing environment values. Vite embeds these public values
at build time; API settings can be overridden with `AzureAd__...` environment values.
No client secrets or Azure DevOps credentials belong in portal configuration.

## Access rules

- POST /api/requests requires BOTH the exact space-delimited `Requests.Submit`
  delegated scope and the `AIDP.Provisioner` role.
- GET /api/requests/{requestId} requires an authenticated `AIDP.Provisioner`.
  This lab increment does not restrict reads to the request owner.
- /health is anonymous.
- /storage-test and /ado-auth-test have been removed.
- `aidp` is a reserved application name and returns a validation problem.

Microsoft.Identity.Web validates signing keys, token lifetime, issuer and audience.
Only the configured tenant is used. Both the API App ID URI (v1 token audience)
and its client ID (v2 token audience) identify this same API. The portal requests
`api://22222222-2222-2222-2222-222222222222/Requests.Submit`, never an Azure DevOps token.

MSAL uses session storage and redirect sign-in/sign-out. Access tokens are acquired
silently before requests; interaction-required responses start a sign-in redirect.
401 means sign-in is missing/invalid; 403 means the required authorization is absent.
No tokens or claims are displayed or logged by the portal.

## Verification

```sh
dotnet build src/Aidp.Api/Aidp.Api.csproj
dotnet run --project tests/Aidp.Api.AuthChecks/Aidp.Api.AuthChecks.csproj
cd src/Aidp.Portal
npm run build
```

The console checks exercise the real API through an in-memory HTTP server. Only
that test host replaces signing metadata with an ephemeral local key; it does not
change the application's authentication scheme or introduce a runtime bypass.
The checks do not contact Entra, Azure, or Azure DevOps. Complete the interactive
browser sign-in separately to verify tenant consent and the lab user's assignment.
