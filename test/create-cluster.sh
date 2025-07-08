#!/bin/bash

set -eo pipefail

. ./functions.sh

kind create cluster --name "$1" --config=kind-config-$1.yaml

REGISTRY_DIR="/etc/containerd/certs.d/localhost:${reg_port}"
for node in $(kind get nodes -n "$1"); do
  docker exec "${node}" mkdir -p "${REGISTRY_DIR}"
  cat <<EOF | docker exec -i "${node}" cp /dev/stdin "${REGISTRY_DIR}/hosts.toml"
[host."http://${reg_name}:5000"]
EOF
done

if [ "$(docker inspect -f='{{json .NetworkSettings.Networks.kind}}' "${reg_name}")" = 'null' ]; then
  docker network connect "kind" "${reg_name}"
fi

# 5. Document the local registry
# https://github.com/kubernetes/enhancements/tree/master/keps/sig-cluster-lifecycle/generic/1755-communicating-a-local-registry
cat <<EOF | kubectl apply -f -
apiVersion: v1
kind: ConfigMap
metadata:
  name: local-registry-hosting
  namespace: kube-public
data:
  localRegistryHosting.v1: |
    host: "localhost:${reg_port}"
    help: "https://kind.sigs.k8s.io/docs/user/local-registry/"
EOF

echo_color "${G}Applying operators"
kubectl create ns mcingress-operator
kustomize build operator/$1 | kubectl apply -f -
helm upgrade --install mcingress --namespace mcingress-operator ../charts/multicluster-ingress -f operator/$1/values.yaml

set_namespace mcingress-operator

wait_for_resource pod condition=ready component=dns-server 90
wait_for_resource pod condition=ready component=orchestrator 90
wait_for_resource pod condition=ready component=api-server 90
wait_for_resource pod condition=ready component=operator 90

set_namespace ingress-operator
wait_for_resource pod condition=ready operator=ingressoperator 90

set_namespace mcingress-operator

echo_color "${G}Setting default namespace to mcingress-operator"
kubectl config set-context kind-$1 --namespace=mcingress-operator
