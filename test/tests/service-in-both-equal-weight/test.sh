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
    (( RETCODE+=$? )) || true

    use_context 2
    echo "Applying manifests"
    kubectl apply -f test2.yaml
    RETCODE=$?
    echo "Setting namespace"
    set_namespace test-in-both-equal-weight
    (( RETCODE+=$? )) || true

    use_context 1
    echo "Waiting for resource"
    wait_for_resource pod condition=ready app=nginx
    (( RETCODE+=$? )) || true
    echo "Waiting for ingress"
    wait_for_ingress nginx
    (( RETCODE+=$? )) || true

    use_context 2
    echo "Waiting for resource"
    wait_for_resource pod condition=ready app=nginx
    (( RETCODE+=$? )) || true
    echo "Waiting for ingress"
    wait_for_ingress nginx
    (( RETCODE+=$? )) || true

    echo "Giving it a second for the api's to register everything"
    sleep 1
    return $RETCODE
}

assert() {
    RESULT=0
    
    COUNT=0
    COUNTOF1=0
    COUNTOF2=0
    CLUSTERMINIMUM=25

    while (( COUNT < 100 ))
    do
        echo "Running $COUNT of 100"
        (( COUNT++ ))

        ACTUAL=$(get_ip 1 service.test-in-both-equal-weight.test)
        if [ "$ACTUAL" != "$CLUSTER1IP" ] && [ "$ACTUAL" != "$CLUSTER2IP" ]
        then
            echo "Cluster 1 ip mismatch Actual '$ACTUAL' Expected '$CLUSTER1IP' or '$CLUSTER2IP'"
            RESULT=1
            break
        fi

        if [ "$ACTUAL" == "$CLUSTER1IP" ]
        then
            (( COUNTOF1++ ))
        else
            (( COUNTOF2++ ))
        fi
    done

    if [ $COUNTOF1 -lt $CLUSTERMINIMUM ]
    then
        echo "Did not receive enough cluster 1 IPs, expected at least $CLUSTERMINIMUM, got $COUNTOF1"
        RESULT=1
    fi

    if [ $COUNTOF2 -lt $CLUSTERMINIMUM ]
    then
        echo "Did not receive enough cluster 2 IPs, expected at least $CLUSTERMINIMUM, got $COUNTOF2"
        RESULT=1
    fi

    [ $RESULT != 0 ] && return $RESULT

    COUNT=0
    COUNTOF1=0
    COUNTOF2=0

    while (( COUNT < 100 ))
    do
        echo "Running $COUNT of 100"
        (( COUNT++ ))

        ACTUAL=$(get_ip 1 test-in-both-equal-weight.test)
        if [ "$ACTUAL" != "$CLUSTER1IP" ] && [ "$ACTUAL" != "$CLUSTER2IP" ]
        then
            echo "Cluster 1 ip mismatch Actual '$ACTUAL' Expected '$CLUSTER1IP' or '$CLUSTER2IP'"
            RESULT=1
            break
        fi

        if [ "$ACTUAL" == "$CLUSTER1IP" ]
        then
            (( COUNTOF1++ ))
        else
            (( COUNTOF2++ ))
        fi
    done

    if [ $COUNTOF1 -lt $CLUSTERMINIMUM ]
    then
        echo "Did not receive enough cluster 1 IPs, expected at least $CLUSTERMINIMUM, got $COUNTOF1"
        RESULT=1
    fi

    if [ $COUNTOF2 -lt $CLUSTERMINIMUM ]
    then
        echo "Did not receive enough cluster 2 IPs, expected at least $CLUSTERMINIMUM, got $COUNTOF2"
        RESULT=1
    fi

    return 0
}

cleanup() {
    use_context 1
    kubectl delete namespace test-in-both-equal-weight
    RESULT=$?

    use_context 2
    kubectl delete namespace test-in-both-equal-weight
    (( RESULT+=$? )) || true

    return $?
}