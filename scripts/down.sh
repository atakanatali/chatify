#!/usr/bin/env bash
set -euo pipefail

# -----------------------------------------------------------------------------
# Chatify Environment Teardown Script
# -----------------------------------------------------------------------------
# This script deletes the kind cluster and all associated resources.
#
# Usage: ./scripts/down.sh
# -----------------------------------------------------------------------------

# Color output for better readability
readonly RED='\033[0;31m'
readonly GREEN='\033[0;32m'
readonly YELLOW='\033[1;33m'
readonly BLUE='\033[0;34m'
readonly NC='\033[0m' # No Color

# Cluster name
readonly CLUSTER_NAME="chatify"
readonly NAMESPACE="chatify"

# -----------------------------------------------------------------------------
# Utility Functions
# -----------------------------------------------------------------------------

log_info() {
    echo -e "${BLUE}[INFO]${NC} $1"
}

log_success() {
    echo -e "${GREEN}[SUCCESS]${NC} $1"
}

log_warn() {
    echo -e "${YELLOW}[WARN]${NC} $1"
}

log_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

# -----------------------------------------------------------------------------
# Prerequisite Checks
# -----------------------------------------------------------------------------

check_prerequisites() {
    log_info "Checking prerequisites..."

    if ! command -v kind &> /dev/null; then
        log_error "kind is not installed. Please install kind from https://kind.sigs.k8s.io/"
        exit 1
    fi

    log_success "Prerequisites check passed"
}

# -----------------------------------------------------------------------------
# Cluster Teardown
# -----------------------------------------------------------------------------

delete_kind_cluster() {
    log_info "Checking for existing cluster '${CLUSTER_NAME}'..."

    if ! kind get clusters | grep -q "^${CLUSTER_NAME}$"; then
        log_warn "Cluster '${CLUSTER_NAME}' does not exist. Nothing to delete."
        return 0
    fi

    log_info "Deleting kind cluster: ${CLUSTER_NAME}"
    kind delete cluster --name "${CLUSTER_NAME}"
    log_success "Kind cluster '${CLUSTER_NAME}' deleted"
}

# -----------------------------------------------------------------------------
# Cleanup Verification
# -----------------------------------------------------------------------------

verify_cleanup() {
    log_info "Verifying cleanup..."

    if kind get clusters | grep -q "^${CLUSTER_NAME}$"; then
        log_error "Cluster '${CLUSTER_NAME}' still exists"
        return 1
    fi

    log_success "Cleanup verification completed"
}

# -----------------------------------------------------------------------------
# Main Execution
# -----------------------------------------------------------------------------

main() {
    log_info "=== Chatify Environment Teardown ==="
    echo ""

    check_prerequisites
    delete_kind_cluster
    verify_cleanup

    echo ""
    log_success "=== Chatify Environment Teardown Complete ==="
    log_info "All resources have been deleted."
}

# Run main function
main "$@"
