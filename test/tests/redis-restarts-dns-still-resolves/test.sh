#!/bin/bash

. ../../functions.sh

setup() {
    return 0
}

assert() {
    RESULT=0
    
    COUNT=0
    COUNTOF1=0
    COUNTOF2=0
    
    echo "Restarting redis in context 1"
    use_context 1
    set_namespace mcingress-operator
    echo "Restarting redis"
    kubectl rollout restart redis
    sleep 15
    wait_for_resource pod condition=ready app=redis

    echo "Restarting redis in context 2"
    use_context 2
    set_namespace mcingress-operator
    echo "Restarting redis"
    kubectl rollout restart redis
    sleep 15
    wait_for_resource pod condition=ready app=redis

    # set -e
    use_context 1
    echo "Applying manifests"
    kubectl apply -f test1.yaml
    RETCODE=$?
    echo "Setting namespace"
    set_namespace redis-restart
    (( RETCODE+=$? )) || true

    use_context 2
    echo "Applying manifests"
    kubectl apply -f test2.yaml
    RETCODE=$?
    echo "Setting namespace"
    set_namespace redis-restart
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

    if [ $RETCODE != 0 ]
    then
        echo "Unable to establish test case"
        return $RETCODE
    fi

    while (( COUNT < 100 ))
    do
        echo "Running $COUNT of 100"
        (( COUNT++ ))

        ACTUAL=$(get_ip 1 redis-restart.test)
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

        ACTUAL=$(get_ip 2 redis-restart.test)
        if [ "$ACTUAL" != "$CLUSTER1IP" ] && [ "$ACTUAL" != "$CLUSTER2IP" ]
        then
            echo "Cluster 2 ip mismatch Actual '$ACTUAL' Expected '$CLUSTER1IP' or '$CLUSTER2IP'"
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

    CLUSTERMINIMUM=10
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

    return $RESULT
}

cleanup() {
    use_context 1
    kubectl delete namespace redis-restart
    RESULT=$?

    use_context 2
    kubectl delete namespace redis-restart
    (( RESULT+=$? )) || true

    return $?
}