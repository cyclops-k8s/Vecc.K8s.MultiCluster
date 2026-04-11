#!/bin/bash

. ../../functions.sh

setup() {
    # set -e
    use_context 2

    echo "Applying manifests"
    kubectl apply -f test.yaml
    RETCODE=$?
    sleep 1

    echo "Setting namespace"
    set_namespace service-only-in-test2
    (( RETCODE+=$? )) || true

    echo "Waiting for resource"
    wait_for_resource pod condition=ready app=nginx
    (( RETCODE+=$? )) || true

    echo "Waiting for something to be returned for the hostname"
    wait_for_ips "service.only-in-test2.test2" $CLUSTER2IP
    (( RETCODE+=$? )) || true

    return $RETCODE
}

assert() {
    RESULT=0
    
    # do this 100 times
    COUNT=0
    EXPECTED=$CLUSTER2IP

    while
        (( COUNT++ ))

        ACTUAL=$(get_ip 1 service.only-in-test2.test2)
        [ "$ACTUAL" != "$EXPECTED" ] && echo "Cluster 1 ip mismatch Actual '$ACTUAL' Expected '$EXPECTED'" && RESULT=1 && break

        ACTUAL=$(get_ip 2 service.only-in-test2.test2)
        [ "$ACTUAL" != "$EXPECTED" ] && echo "Cluster 2 ip mismatch Actual '$ACTUAL' Expected '$EXPECTED'" && RESULT=1 && break
    do (( COUNT < 100 ))
    done

    return $RESULT
}

cleanup() {
    kubectl delete namespace service-only-in-test2
    
    return $?
}