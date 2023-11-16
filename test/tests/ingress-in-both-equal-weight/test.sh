#!/bin/bash

. ../../functions.sh

setup() {
    # set -e
    use_context 1
    echo "Applying manifests"
    kubectl apply -f test1.yaml
    RETCODE=$?
    echo "Setting namespace"
    set_namespace test-in-both-equal-weight
    let RETCODE+=$? || true

    use_context 2
    echo "Applying manifests"
    kubectl apply -f test2.yaml
    RETCODE=$?
    echo "Setting namespace"
    set_namespace test-in-both-equal-weight
    let RETCODE+=$? || true

    use_context 1
    echo "Waiting for resource"
    wait_for_resource pod condition=ready app=nginx
    let RETCODE+=$? || true
    echo "Waiting for ingress"
    wait_for_ingress nginx
    let RETCODE+=$? || true


    use_context 2
    echo "Waiting for resource"
    wait_for_resource pod condition=ready app=nginx
    let RETCODE+=$? || true
    echo "Waiting for ingress"
    wait_for_ingress nginx
    let RETCODE+=$? || true

    echo "Giving it a second for the api's to register everything"
    sleep 1
    return $RETCODE
}

assert() {
    RESULT=0
    
    # do this 100 times
    COUNT=0
    COUNTOF1=0
    COUNTOF2=0
    while
        let COUNT+=1

        ACTUAL=`get_ip 1 test-in-both.test`
        [ "$ACTUAL" != "$CLUSTER1IP" ] && [ "$ACTUAL" != "$CLUSTER2IP" ] && echo "Cluster 1 ip mismatch Actual '$ACTUAL' Expected '$CLUSTER1IP' or '$CLUSTER2IP'" && RESULT=1 && break

        ACTUAL=`get_ip 2 test-in-both.test`
        [ "$ACTUAL" != "$CLUSTER1IP" ] && [ "$ACTUAL" != "$CLUSTER2IP" ] && echo "Cluster 2 ip mismatch Actual '$ACTUAL' Expected '$CLUSTER1IP' or '$CLUSTER2IP'" && RESULT=1 && break
    do (( $COUNT < 100 ))
    done

    return $RESULT
}

cleanup() {
    # kubectl delete namespace only-in-test1
    return $?
}