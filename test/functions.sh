#!/bin/bash

use_context() {
    kubectl config use-context "$1"
}