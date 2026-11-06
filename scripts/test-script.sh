#!/usr/bin/env bash
set -euo pipefail

variant=${1:-${VARIANT:-}}
deployment=${2:-${DEPLOYMENT_NAME:-}}
namespace=${3:-${NAMESPACE:-default}}
config=${4:-${CONFIG_PATH:-}}
: "${variant:?Specify variant}"
: "${deployment:?Specify deployment}"
: "${config:?Specify kubeconfig as argument 4 or CONFIG_PATH}"
kube=(kubectl)
kube+=(--kubeconfig "$config")
kube+=(--namespace "$namespace")
newman_args=()
[[ -z "${BASE_URL:-}" ]] || newman_args+=(--env-var "baseUrl=$BASE_URL")

start=$(date +%s)
original_replicas=$("${kube[@]}" get deployment "$deployment" -o 'jsonpath={.spec.replicas}')
cleanup() {
  local status=$?
  trap - EXIT
  "${kube[@]}" scale deployment "$deployment" --replicas="$original_replicas" || true
  printf '\nTotal runtime: %d seconds\n' "$(( $(date +%s) - start ))"
  exit "$status"
}
trap cleanup EXIT

run_folder() {
  newman run --delay-request=100 --folder="$1" \
    --export-environment "$variant/postman/environment.json" \
    --environment "$variant/postman/environment.json" \
    "${newman_args[@]}" "$variant/postman/collection.json"
}

printf '=== Start test scenario ===\n'
run_folder success
for step in 1 2 3 4; do
  replicas=$((1 - step % 2))
  printf '=== Step %d: scale %s to %d ===\n' "$step" "$deployment" "$replicas"
  "${kube[@]}" scale deployment "$deployment" --replicas="$replicas"
  if [[ "$replicas" == 0 ]]; then
    "${kube[@]}" wait pod -l "app.kubernetes.io/name=$deployment" --for=delete --timeout=90s
  else
    "${kube[@]}" rollout status deployment "$deployment" --timeout=180s
  fi
  run_folder "step$step"
done
