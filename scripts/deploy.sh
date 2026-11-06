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
# Keycloak has its own database and persistent credentials.
if ! "${kube[@]}" get secret keycloak-credentials >/dev/null 2>&1; then
  python3 - "$config" "$namespace" <<'PYSECRET'
import json, secrets, subprocess, sys
secret = {'apiVersion': 'v1', 'kind': 'Secret', 'metadata': {'name': 'keycloak-credentials'},
          'type': 'Opaque', 'stringData': {key: secrets.token_urlsafe(32) for key in
          ['db-password', 'admin-password', 'client-secret', 'test-password']}}
subprocess.run(['kubectl', '--kubeconfig', sys.argv[1], '-n', sys.argv[2], 'create', '-f', '-'],
               input=json.dumps(secret), text=True, check=True)
PYSECRET
fi
keycloak_node=$("${kube[@]}" get deployment keycloak-db --ignore-not-found -o 'jsonpath={.spec.template.spec.nodeSelector.kubernetes\.io/hostname}')
keycloak_node=${keycloak_node:-$node_name}
address=$(kubectl --kubeconfig "$config" -n traefik get svc traefik -o jsonpath='{.status.loadBalancer.ingress[0].ip}')
: "${address:?Traefik must have a public LoadBalancer IP}"
public_url="http://$address/auth"
helm upgrade --install keycloak "$root/helm/keycloak" "${helm_args[@]}" \
  --timeout 10m --set-string "nodeName=$keycloak_node" --set-string "publicUrl=$public_url"
for service in loyalty payment reservation gateway; do
  helm upgrade --install "$service-service" "$root/helm/booking-service" \
    "${helm_args[@]}" -f "$root/deploy/$service-service.yaml" --set-string "image.tag=$image_tag" \
    --set-string "env.Authentication__Issuer=$public_url/realms/rsoi" \
    --set-string "env.Authentication__JwksUri=http://keycloak:8080/auth/realms/rsoi/protocol/openid-connect/certs"
done
