#!/usr/bin/env bash
set -euo pipefail

if [[ ! ${APPLICATION_NAME:-} =~ ^[a-z0-9][a-z0-9-]{1,28}[a-z0-9]$ || ${APPLICATION_NAME:-} == aidp ]]; then
  echo 'Application name must be 3–30 lowercase letters, digits or hyphens, without leading/trailing hyphens; aidp is reserved.' >&2
  exit 1
fi
[[ ${WORKLOAD_ENVIRONMENT:-} == dev ]] || { echo 'Environment must be dev.' >&2; exit 1; }
[[ ${RUNTIME:-} == dotnet10 ]] || { echo 'Runtime must be dotnet10.' >&2; exit 1; }
[[ ${RESOURCE_TYPE:-} == appservice ]] || { echo 'Resource type must be appservice.' >&2; exit 1; }
echo 'Workload parameters validated.'
