#!/bin/bash

. ../../functions.sh

setup() {
    echo_color "${G}Setting up 2"
    use_context 2
}

assert() {
    echo_color "${G}Asserting 2"
    use_context 2
}

cleanup() {
    echo_color "${G}Cleaning up 2"
    use_context 2
}