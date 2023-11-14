#!/bin/bash

. ../../functions.sh

setup() {
    echo_color "${G}Setting up 1"
    use_context 1

    return 1
}

assert() {
    echo_color "${G}Asserting 1"
    use_context 1

    return 1
}

cleanup() {
    echo_color "${G}Cleaning up 1"
    use_context 1

    return 1
}