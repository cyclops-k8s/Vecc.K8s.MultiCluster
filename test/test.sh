#!/bin/bash

DIRECTORY="$(pwd)"
TEMPDIRECTORY=$(mktemp -d)

export reg_name='kind-registry'
export reg_port='5001'
export DIRECTORY
export TEMPDIRECTORY

# shellcheck disable=SC2317
function terminate() {
    set +e

    mkdir -p "$TEMPDIRECTORY/results"
    mv "$TEMPDIRECTORY"/* "$TEMPDIRECTORY"/results 2> /dev/null
    RESULTS="results-$(date +%Y%m%d-%H%M%S).tgz"
    mkdir -p "$TEMPDIRECTORY/results/kubernetes/kind-test1"
    kubectl logs --context kind-test1 -n mcingress-operator deployment/multiclusteringress-dns-server > "$TEMPDIRECTORY/results/kubernetes/kind-test1/operator-dns-server.log"
    kubectl logs --context kind-test1 -n mcingress-operator deployment/multiclusteringress-api-server > "$TEMPDIRECTORY/results/kubernetes/kind-test1/operator-api-server.log"
    kubectl logs --context kind-test1 -n mcingress-operator deployment/multiclusteringress-orchestrator > "$TEMPDIRECTORY/results/kubernetes/kind-test1/operator-orchestrator.log"
    kubectl logs --context kind-test1 -n mcingress-operator deployment/multiclusteringress-operator > "$TEMPDIRECTORY/results/kubernetes/kind-test1/operator-operator.log"
    kubectl get pods --context kind-test1 -A -o wide > "$TEMPDIRECTORY/results/kubernetes/kind-test1/allpods.txt"

    mkdir -p "$TEMPDIRECTORY/results/kubernetes/kind-test2"
    kubectl logs --context kind-test2 -n mcingress-operator deployment/multiclusteringress-dns-server > "$TEMPDIRECTORY/results/kubernetes/kind-test2/operator-dns-server.log"
    kubectl logs --context kind-test2 -n mcingress-operator deployment/multiclusteringress-api-server > "$TEMPDIRECTORY/results/kubernetes/kind-test2/operator-api-server.log"
    kubectl logs --context kind-test2 -n mcingress-operator deployment/multiclusteringress-orchestrator > "$TEMPDIRECTORY/results/kubernetes/kind-test2/operator-orchestrator.log"
    kubectl logs --context kind-test2 -n mcingress-operator deployment/multiclusteringress-operator > "$TEMPDIRECTORY/results/kubernetes/kind-test2/operator-operator.log"
    kubectl get pods --context kind-test2 -A -o wide > "$TEMPDIRECTORY/results/kubernetes/kind-test2/allpods.txt"

    free --mega -hv --total > "$TEMPDIRECTORY/results/memory.txt"
    ps -ef > "$TEMPDIRECTORY/results/ps.txt"
    
    echo_color "${G}Tarring up results to ${Y}${RESULTS}"
    tar -czf "$DIRECTORY/$RESULTS" --transform="s!.*/results!results!" "$TEMPDIRECTORY/results" 1> /dev/null 2> /dev/null
    rm -rf "$DIRECTORY/results"
    cp -r "$TEMPDIRECTORY/results" "$DIRECTORY"
    rm -rf "$TEMPDIRECTORY"
}

trap terminate EXIT

set -e

. ./functions.sh

echo_color "${G}Temp directory is ${Y}${TEMPDIRECTORY}"

echo_color "${G}Downloading kind"

mkdir -p .test
if [ ! -f ./.test/kind ]
then
    curl -Lo ./.test/kind https://kind.sigs.k8s.io/dl/v0.24.0/kind-linux-amd64
    chmod +x ./.test/kind
fi

echo_color "${G}Downloading kustomize"
if [ ! -f ./.test/kustomize ]
then
    curl -s "https://raw.githubusercontent.com/kubernetes-sigs/kustomize/master/hack/install_kustomize.sh"  | bash
    mv ./kustomize .test/kustomize
fi

echo_color "${G}Downloading kubectl"
if [ ! -f ./.test/kubectl ]
then
    curl -L "https://dl.k8s.io/release/$(curl -L -s https://dl.k8s.io/release/stable.txt)/bin/linux/amd64/kubectl" -o ./.test/kubectl
    chmod +x ./.test/kubectl
fi

echo_color "${G}Setting path"
PATH="$(pwd)/.test:$PATH"

echo_color "${G}Setting KUBECONFIG path"
KUBECONFIG_FILE=$(pwd)/.test/cluster.config
export KUBECONFIG="$KUBECONFIG_FILE"

set -e

./create-docker-registry.sh

docker image build --build-arg DEBUG=1 -t localhost:${reg_port}/multicluster:latest ../ --progress=plain
docker image push localhost:${reg_port}/multicluster:latest

docker image build -t localhost:${reg_port}/ingress-operator:latest ingress-operator
docker image push localhost:${reg_port}/ingress-operator:latest

echo_color "${G}Removing old clusters"
kind delete clusters --all

echo_color "${G}Setting up Kind cluster 1"
./create-cluster.sh test1

echo_color "${G}Setting up Kind cluster 2"
./create-cluster.sh test2

use_context 1
echo_color "${G}Kind-Test1"
kubectl get ns

use_context 2
echo_color "${G}Kind-Test2"
kubectl get ns

set +e

echo_color "${G}Waiting for cluster 1 relay to start"
until dig mcingress.test1 @localhost -p 1053 +tcp
do
    sleep 1
done

echo_color "${G}Waiting for cluster 2 relay to start"
until dig mcingress.test2 @localhost -p 1054 +tcp
do
    sleep 1
done

export CLUSTER1IP="192.168.0.1"
export CLUSTER2IP="192.168.0.2"

FAILEDTESTS=()
PASSEDTESTS=()
PASSEDTESTCOUNT=0
FAILEDTESTCOUNT=0
TOTALTESTCOUNT=0
MYDIR=$(pwd)

echo_color "${G}Executing tests"

for TEST in tests/*
do
    (( TOTALTESTCOUNT++ ))
    echo_color "${G}Executing test - $TEST"
    cd "$TEST"
    . ./test.sh
    spinner_test "$TEMPDIRECTORY/results/$TEST"
    RESULT=$?
    cd "$MYDIR"
    if [ $RESULT == 0 ]
    then
        echo_color "${G}✓ Test ${Y}$TEST${G} passed"
        PASSEDTESTS+=("$TEST")
        (( PASSEDTESTCOUNT++ ))
    else
        echo_color "${R}✗ Test ${Y}$TEST${R} failed"
        FAILEDTESTS+=("$TEST")
        (( FAILEDTESTCOUNT++ ))
    fi
    echo_color "${G}-------"
done

echo_color "${G}All tests executed"
echo_color "Passed Tests - $PASSEDTESTCOUNT of $TOTALTESTCOUNT"
for TEST in "${PASSEDTESTS[@]}"
do
    echo_color "${G}✓ Test ${Y}$TEST${G} passed"
done

RESULTCODE=0
echo_color "Failed Tests - $FAILEDTESTCOUNT of $TOTALTESTCOUNT"
for TEST in "${FAILEDTESTS[@]}"
do
    RESULTCODE=1
    echo_color "${R}✗ Test ${Y}$TEST${R} failed"
done

exit $RESULTCODE
