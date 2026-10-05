# App Service workload provisioning

`azure-pipelines-workload.yml` is a separate, manual workload pipeline. It is not
connected to the portal/API and does not change `azure-pipelines.yml` or
`terraform/environments/dev`.

## Terraform

`terraform/workloads/app-service` contains versions, provider, partial backend,
variables, one Linux App Service resource, outputs, and a provider lock file.
It accepts `application_name`, `environment`, `location`, `resource_group_name`,
and `app_service_plan_id`. The initial supported configuration is DEV in South
Central US, in `rg-aidp-example-dev`, using the existing `asp-aidp-example-dev` B1 plan.
There are no plan, resource group, role assignment, or other platform resources
in this template. Automatic provider registration is disabled.

The workload uses .NET 10, HTTPS only, system-assigned identity, disabled FTP and
publishing basic authentication, and TLS 1.2 minimum. No application code is
deployed. Outputs expose the resource ID, name, hostname, and identity principal ID.

`inventory-api` becomes `app-inventory-example-dev`. Application names follow
the API's 3–30 character lowercase/digit/hyphen rule. `aidp` is additionally
reserved because it would collide with the existing platform App Service.

## State and authentication

Each job computes `workloads/<environment>/<applicationName>.tfstate`, for example
`workloads/dev/inventory-api.tfstate`. The backend deliberately has no default
key, so it cannot silently use `aidp-dev.tfstate`. It uses the existing
`tfstatepublicexample` account and `tfstate` container with Azure AD data-plane
authentication and OIDC. The pipeline uses `sc-public-azure`; no account keys or
hard-coded secrets are used. The WIF token is passed only through environment
variables, not backend arguments or artifacts.

## Pipeline flow

Parameters are `applicationName` (required), `environment=dev`,
`runtime=dotnet10`, and `resourceType=appservice`. Fixed parameters have YAML
allowed-value lists and the shared shell validator checks exact values again.
Application names are validated before use in state paths or Azure operations.

1. **Validate:** validate parameters, check formatting, initialize without a
   backend, and validate Terraform.
2. **TerraformPlan:** authenticate with WIF, temporarily allow the agent IP,
   initialize the per-application backend, validate, read the existing plan ID,
   and save a binary `tfplan`. Clean up job-owned firewall access and publish
   `workload-terraform-plan` with `tfplan`, its SHA-256 checksum, backend key,
   and provider lock file.
3. **TerraformApply:** wait for checks on `aidp-dev-workload`; download only the
   current run's artifact, verify its backend key, checksum and provider lock,
   initialize the same backend with fresh WIF credentials, and apply the exact
   saved binary plan. No re-plan occurs. A stale plan fails rather than being
   silently regenerated. Both stages use Terraform 1.16.3 and the locked provider.

The workload step template shares the platform pipeline's firewall ownership
pattern between Plan and Apply: discover the public IPv4 address once per job,
check for an exact IP (including `/32`) rule, and record ownership only after
successfully adding a missing rule. Cleanup uses `condition: always()` and
removes only a rule added by that job. Init retries at most five times with
10-second delays. No default firewall action or all-networks access is changed.
As with the existing pattern, a hard agent loss can prevent cleanup, and the
check/add operations are not atomic across concurrent jobs sharing a public IP.

## Manual Azure DevOps setup before execution

- Register a separate YAML pipeline pointing to `azure-pipelines-workload.yml`.
- Create `aidp-dev-workload`, configure its required approval/checks and authorize
  this pipeline. Merely referencing an environment in YAML does not configure
  an approval. Inspect the published plan/run before approving Apply.
- Authorize the new pipeline to use the existing `sc-public-azure` WIF connection.
  Verify its existing permissions cover workload creation in the DEV resource
  group, reading the existing plan, backend blob access, and the existing
  backend firewall add/remove operations. No permission changes are made here.
- Confirm globally unique App Service names and available capacity in the shared
  B1 plan before running. No additional plan is provisioned.

No Azure DevOps registration, environment, approval, service connection, Azure
resource, or permission is created by this repository change.

## Local static validation

From the repository root:

```sh
terraform fmt -check -recursive
terraform -chdir=terraform/workloads/app-service init -backend=false
terraform -chdir=terraform/workloads/app-service validate
ruby -ryaml -e 'ARGV.each { |path| YAML.load_file(path); puts "YAML OK: #{path}" }' azure-pipelines-workload.yml pipelines/templates/*.yml
bash -n scripts/validate-workload-parameters.sh
git diff --check
```

YAML parsing is a syntax check, not Azure DevOps server-side template compilation
or verification of service connection permissions and environment checks.
