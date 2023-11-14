#!/bin/bash

use_context() {
    kubectl config use-context "kind-test$1"
}

R='\033[0;31m'   #'0;31' is Red's ANSI color code
G='\033[0;32m'   #'0;32' is Green's ANSI color code
B='\033[0;34m'   #'0;34' is Blue's ANSI color code
Y='\033[0;33m'   #'1;32' is Yellow's ANSI color code

NOCOLOR='\033[0m'

echo_color() {
    echo -e "$1${NOCOLOR}"
}

echo_color_error() {
    echo -e "$1${NOCOLOR}" >&2
}

spinner_setup() {
    (
        STDOUTFILE="$1/setup-stdout.txt"
        STDERRFILE="$1/setup-stderr.txt"

        . ./test.sh

        eval "echo 'Setting up' && setup" 1> $STDOUTFILE 2> $STDERRFILE &

        PID=$!
        SPINNER=("⡿" "⣟" "⣯" "⣷" "⣾" "⣽" "⣻" "⢿")
        POS=0

        while (kill -0 $PID 1> /dev/null 2> /dev/null)
        do
            let POS+=1
            [ $POS == 8 ] && POS=0
            printf "\r${SPINNER[$POS]} Setting up"
            sleep .1
        done
        echo
        wait $PID
        return $?
    )
}

spinner_assert() {
    (
        STDOUTFILE="$1/assert-stdout.txt"
        STDERRFILE="$1/assert-stderr.txt"

        . ./test.sh

        eval "echo 'Asserting' && assert" 1>$STDOUTFILE 2>$STDERRFILE &

        PID=$!
        SPINNER=("⡿" "⣟" "⣯" "⣷" "⣾" "⣽" "⣻" "⢿")
        POS=0

        while (kill -0 $PID 1> /dev/null 2> /dev/null)
        do
            let POS+=1
            [ $POS == 8 ] && POS=0
            printf "\r${SPINNER[$POS]} Asserting"
            sleep .1
        done
        echo
        wait $PID
        return $?
    )
}

spinner_cleanup() {
    (
        STDOUTFILE="$1/cleanup-stdout.txt"
        STDERRFILE="$1/cleanup-stderr.txt"

        . ./test.sh

        eval "echo 'Cleaning up' && cleanup" 1>$STDOUTFILE 2>$STDERRFILE &

        PID=$!
        SPINNER=("⡿" "⣟" "⣯" "⣷" "⣾" "⣽" "⣻" "⢿")
        POS=0

        while (kill -0 $PID 1> /dev/null 2> /dev/null)
        do
            let POS+=1
            [ $POS == 8 ] && POS=0
            printf "\r${SPINNER[$POS]} Cleaning up"
            sleep .1
        done
        echo
        wait $PID
        return $?
    )
}

spinner_wait() {
    (
        TEXT=$1
        shift
        eval "$@" &

        PID=$!
        SPINNER=("⡿" "⣟" "⣯" "⣷" "⣾" "⣽" "⣻" "⢿")
        POS=0

        while (kill -0 $PID 1> /dev/null 2> /dev/null)
        do
            let POS+=1
            [ $POS == 8 ] && POS=0
            printf "\r${SPINNER[$POS]} $TEXT"
            sleep .1
        done
        echo
        wait $PID
        return $?
    )
}

spinner_test() {
    OUTPUTDIRECTORY=$1
    SETUPRESULT=0
    ASSERTRESULT=0
    CLEANUPRESULT=0
    RESULT=0
    mkdir -p "$OUTPUTDIRECTORY"
    spinner_setup "$OUTPUTDIRECTORY"
    SETUPRESULT=$?
    if [ $SETUPRESULT==0 ]
    then
        spinner_assert "$OUTPUTDIRECTORY"
        ASSERTRESULT=$?
    fi
    spinner_cleanup "$OUTPUTDIRECTORY"
    CLEANUPRESULT=$?
    let RESULT=$SETUPRESULT+$ASSERTRESULT+$CLEANUPRESULT
    return $RESULT
}