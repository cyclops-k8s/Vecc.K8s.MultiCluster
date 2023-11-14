#!/bin/bash

set -e

. ./functions.sh

cat <<EOF | kind create cluster --name "$1" --config=-
kind: Cluster
apiVersion: kind.x-k8s.io/v1alpha4
containerdConfigPatches:
- |-
  [plugins."io.containerd.grpc.v1.cri".registry]
    config_path = "/etc/containerd/certs.d"
EOF

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

echo_color "${G}Downloading nginx manifest"
kustomize build operator/$1 | kubectl apply -f -
curl -o .test/nginx.yaml -L https://raw.githubusercontent.com/kubernetes/ingress-nginx/main/deploy/static/provider/kind/deploy.yaml
sed -i 's/- --publish-status-address=localhost//' .test/nginx.yaml 

echo_color "${G}Labeling node for ingress"
kubectl label node $1-control-plane ingress-ready=true

echo_color "${G}Applying nginx manifest"
kubectl apply -f .test/nginx.yaml


set +e
spinner_wait "${G}Waiting for nginx to get applied" "
T=1
while [ \$T != 0 ]
do
  kubectl wait --namespace ingress-nginx --for=condition=ready pod --selector=app.kubernetes.io/component=controller --timeout=1s 1> /dev/null 2> /dev/null
  T=\$?
  [ \$T != 0 ] && sleep 1
done
"

echo_color "${G}Setting default namespace to mcingress-operator"
kubectl config set-context kind-$1 --namespace=mcingress-operator
