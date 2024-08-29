#!/bin/bash

. ../../functions.sh

setup() {
    # set -e
    use_context 1
    echo "Applying manifests"
    kubectl apply -f test1.yaml
    RETCODE=$?
    echo "Setting namespace"
    set_namespace cluster-failover
    (( RETCODE+=$? )) || true

    use_context 2
    echo "Applying manifests"
    kubectl apply -f test2.yaml
    RETCODE=$?
    echo "Setting namespace"
    set_namespace cluster-failover
    (( RETCODE+=$? )) || true

    use_context 1
    echo "Waiting for resource"
    wait_for_resource pod condition=ready app=nginx
    (( RETCODE+=$? )) || true
    if [ $RETCODE != 0 ]
    then
        echo "Failed to wait for resource"
        return $RETCODE
    fi

    echo "Waiting for ingress"
    wait_for_ingress nginx
    (( RETCODE+=$? )) || true
    if [ $RETCODE != 0 ]
    then
        echo "Failed to wait for ingress"
        return $RETCODE
    fi

    use_context 2
    echo "Waiting for resource"
    wait_for_resource pod condition=ready app=nginx
    if [ $RETCODE != 0 ]
    then
        echo "Failed to wait for resource"
        return $RETCODE
    fi

    echo "Waiting for ingress"
    wait_for_ingress nginx
    (( RETCODE+=$? )) || true
    if [ $RETCODE != 0 ]
    then
        echo "Failed to wait for ingress"
        return $RETCODE
    fi

    echo "Giving it 10 seconds for the api's to register everything"
    sleep 15
    return $RETCODE
}

assert() {
    RESULT=0
    
    COUNT=0
    COUNTOF1=0
    COUNTOF2=0
    CLUSTERMINIMUM=25

    echo "Testing kind-test1 non-cluster-failover cluster dns"
    while (( COUNT <= 100 ))
    do
        echo "Running $COUNT of 100"
        (( COUNT++ ))

        ACTUAL=$(get_ip 1 cluster-failover.test)
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

    echo "Testing kind-test2 non-cluster-failover cluster dns"
    while (( COUNT <= 100 ))
    do
        echo "Running $COUNT of 100"
        (( COUNT++ ))

        ACTUAL=$(get_ip 2 cluster-failover.test)
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

    use_context 2
    echo "Stopping mcingress-operator pods (except dns and orchestrator) at $(date)"
    kubectl patch deployment -n mcingress-operator operator-operator -p '{"spec":{"replicas":0}}'
    kubectl patch deployment -n mcingress-operator operator-api-server -p '{"spec":{"replicas":0}}'

    wait_for_resource_missing pod app=operator 30
    wait_for_resource_missing pod api-server 30

    echo "Scaled down the controllers at $(date)"

    echo "Waiting 20 seconds to let the cluster timeout"
    sleep 20

    COUNT=0
    COUNTOF1=0
    COUNTOF2=0
    CLUSTERMINIMUM=25

    echo "Testing kind-test1 cluster-failover cluster dns"
    while (( COUNT <= 100 ))
    do
        echo "Running $COUNT of 100"
        (( COUNT++ ))

        ACTUAL=$(get_ip 1 cluster-failover.test)
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

    if [ $COUNTOF1 -lt 100 ]
    then
        echo "Did not receive enough cluster 1 IPs, expected at least $CLUSTERMINIMUM, got $COUNTOF1"
        RESULT=1
    fi

    if [ $COUNTOF2 != 0 ]
    then
        echo "Received $COUNTOF2 cluster 2 IPs, expected 0"
        RESULT=1
    fi

    COUNT=0
    COUNTOF1=0
    COUNTOF2=0

    echo "Testing kind-test2 cluster-failover cluster dns"
    while (( COUNT <= 100 ))
    do
        echo "Running $COUNT of 100"
        (( COUNT++ ))

        ACTUAL=$(get_ip 1 cluster-failover.test)
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

    if [ $COUNTOF1 -lt 100 ]
    then
        echo "Did not receive enough cluster 1 IPs, expected at least $CLUSTERMINIMUM, got $COUNTOF1"
        RESULT=1
    fi

    if [ $COUNTOF2 != 0 ]
    then
        echo "Received $COUNTOF2 cluster 2 IPs, expected 0"
        RESULT=1
    fi

    return $RESULT
}

cleanup() {
    use_context 1

    kubectl delete namespace cluster-failover
    RESULT=$?

    use_context 2

    kubectl patch deployment -n mcingress-operator operator-operator -p '{"spec":{"replicas":1}}'
    kubectl patch deployment -n mcingress-operator operator-api-server -p '{"spec":{"replicas":1}}'
    wait_for_resource pod condition=ready component=orchestrator 90
    wait_for_resource pod condition=ready component=api-server 90

    kubectl delete namespace cluster-failover
    (( RESULT+=$? )) || true

    return $?
}