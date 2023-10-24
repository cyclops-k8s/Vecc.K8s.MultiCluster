#!/bin/bash

export reg_name='kind-registry'
export reg_port='5001'

. functions.sh

echo "Downloading kind"

mkdir -p .test
if [ ! -f ./.test/kind ]
then
    curl -Lo ./.test/kind https://kind.sigs.k8s.io/dl/v0.20.0/kind-linux-amd64
    chmod +x ./.test/kind
fi

echo "Downloading kustomize"
if [ ! -f ./.test/kustomize ]
then
    curl -s "https://raw.githubusercontent.com/kubernetes-sigs/kustomize/master/hack/install_kustomize.sh"  | bash
    mv ./kustomize .test/kustomize
fi

echo "Downloading kubectl"
if [ ! -f ./.test/kubectl ]
then
    curl -L "https://dl.k8s.io/release/$(curl -L -s https://dl.k8s.io/release/stable.txt)/bin/linux/amd64/kubectl" -o ./.test/kubectl
    chmod +x kubectl
fi

echo "Setting path"
PATH="`pwd`/.test:$PATH"

echo "Setting KUBECONFIG path"
export KUBECONFIG=`pwd`/cluster.config

echo "Setting up Kind cluster 1"

./create-docker-registry.sh

docker image build -t localhost:${reg_port}/multicluster:latest ../
docker image push localhost:${reg_port}/multicluster:latest

./create-cluster.sh test1
./create-cluster.sh test2

use_context kind-test1
echo "Kind-Test1"
kubectl get ns

use_context kind-test2
echo "Kind-Test2"
kubectl get ns