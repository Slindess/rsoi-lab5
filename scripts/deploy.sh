#!/usr/bin/env bash
set -euo pipefail

# Usage: ./scripts/deploy.sh /absolute/path/to/kubeconfig <image-tag> [namespace]
config=${1:?Specify kubeconfig path}
image_tag=${2:?Specify image tag}
namespace=${3:-test}
root=$(cd "$(dirname "$0")/.." && pwd)
kube=(kubectl --kubeconfig "$config" --namespace "$namespace")
helm_args=(--kubeconfig "$config" --namespace "$namespace" --wait --timeout 5m)

"${kube[@]}" create namespace "$namespace" --dry-run=client -o yaml | "${kube[@]}" apply -f -

# Keep credentials unchanged across deployments: PostgreSQL initializes only once.
if ! "${kube[@]}" get secret booking-database >/dev/null 2>&1; then
  python3 - "$config" "$namespace" <<'PY'
import json, secrets, subprocess, sys
password = secrets.token_hex(24)
data = {'postgres-password': password}
for service, database in [('loyalty', 'loyalties'), ('payment', 'payments'), ('reservation', 'reservations')]:
    data[f'{service}-connection'] = f'Host=postgres;Database={database};Username=program;Password={password}'
secret = {'apiVersion': 'v1', 'kind': 'Secret', 'metadata': {'name': 'booking-database'}, 'type': 'Opaque', 'stringData': data}
subprocess.run(['kubectl', '--kubeconfig', sys.argv[1], '-n', sys.argv[2], 'create', '-f', '-'], input=json.dumps(secret), text=True, check=True)
PY
fi

# Reuse the same worker on every deployment so PostgreSQL keeps its local data.
node_name=$("${kube[@]}" get deployment postgres --ignore-not-found -o 'jsonpath={.spec.template.spec.nodeSelector.kubernetes\.io/hostname}')
if [[ -z "$node_name" ]]; then
  node_name=$("${kube[@]}" get nodes -o json | python3 -c '
import json, sys
for node in json.load(sys.stdin)["items"]:
    spec = node.get("spec", {})
    if spec.get("unschedulable") or any(t["effect"] in ("NoSchedule", "NoExecute") for t in spec.get("taints", [])):
        continue
    if any(c["type"] == "Ready" and c["status"] == "True" for c in node["status"]["conditions"]):
        print(node["metadata"]["labels"]["kubernetes.io/hostname"])
        break
else:
    sys.exit("No schedulable Ready worker found")
')
fi

helm upgrade --install postgres "$root/helm/postgres" "${helm_args[@]}" --set-string "nodeName=$node_name"
for service in loyalty payment reservation gateway; do
  helm upgrade --install "$service-service" "$root/helm/booking-service" \
    "${helm_args[@]}" -f "$root/deploy/$service-service.yaml" --set-string "image.tag=$image_tag"
done
