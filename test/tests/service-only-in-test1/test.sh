#!/bin/bash

. ../../functions.sh

setup() {
    # set -e
    use_context 1

    echo "Applying manifests"
    kubectl apply -f test.yaml
    RETCODE=$?
    sleep 1

    echo "Setting namespace"
    set_namespace only-in-test1
    let RETCODE+=$? || true

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
    EXPECTED=$CLUSTER1IP

    while
        let COUNT+=1

        ACTUAL=`get_ip 1 only-in-test1.test1`
        [ "$ACTUAL" != "$EXPECTED" ] && echo "Cluster 1 ip mismatch Actual '$ACTUAL' Expected '$EXPECTED'" && RESULT=1 && break

        ACTUAL=`get_ip 2 only-in-test1.test1`
        [ "$ACTUAL" != "$EXPECTED" ] && echo "Cluster 2 ip mismatch Actual '$ACTUAL' Expected '$EXPECTED'" && RESULT=1 && break
    do (( $COUNT < 100 ))
    done

    return $RESULT
}

cleanup() {
    # kubectl delete namespace only-in-test1
    return $?
}