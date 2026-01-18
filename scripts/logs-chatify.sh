#!/usr/bin/env bash
set -euo pipefail

# -----------------------------------------------------------------------------
# Chatify Logs Script
# -----------------------------------------------------------------------------
# This script tails logs for the Chatify Chat API deployment.
#
# Usage:
#   ./scripts/logs-chatify.sh           # Follow all Chat API pods
#   ./scripts/logs-chatify.sh -p pod    # Follow specific pod
#   ./scripts/logs-chatify.sh -f        # Show logs for a specific container
#   ./scripts/logs-chatify.sh --since 1h # Show logs since 1 hour ago
# -----------------------------------------------------------------------------

# Color output for better readability
readonly RED='\033[0;31m'
readonly GREEN='\033[0;32m'
readonly YELLOW='\033[1;33m'
readonly BLUE='\033[0;34m'
readonly CYAN='\033[0;36m'
readonly NC='\033[0m' # No Color

# Namespace
readonly NAMESPACE="chatify"
readonly DEPLOYMENT_NAME="chatify-chat-api"

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

print_header() {
    echo ""
    echo -e "${CYAN}=== $1 ===${NC}"
    echo ""
}

# -----------------------------------------------------------------------------
# Prerequisite Checks
# -----------------------------------------------------------------------------

check_prerequisites() {
    log_info "Checking prerequisites..."

    if ! command -v kubectl &> /dev/null; then
        log_error "kubectl is not installed. Please install kubectl from https://kubernetes.io/docs/tasks/tools/"
        exit 1
    fi

    log_success "Prerequisites check passed"
}

check_cluster_and_namespace() {
    if ! kubectl cluster-info &> /dev/null; then
        log_error "Cannot connect to Kubernetes cluster"
        exit 1
    fi

    if ! kubectl get namespace "${NAMESPACE}" &> /dev/null; then
        log_error "Namespace '${NAMESPACE}' does not exist"
        log_info "Please run ./scripts/up.sh to create the environment"
        exit 1
    fi
}

# -----------------------------------------------------------------------------
# Pod Selection
# -----------------------------------------------------------------------------

select_pod() {
    local pods
    local pod_count
    local selected_pod=""

    pods=$(kubectl get pods -n "${NAMESPACE}" -l "app.kubernetes.io/name=${DEPLOYMENT_NAME}" --no-headers 2>/dev/null | awk '{print $1}')
    pod_count=$(echo "$pods" | grep -c . || echo "0")

    if [[ $pod_count -eq 0 ]]; then
        log_error "No pods found for deployment '${DEPLOYMENT_NAME}'"
        log_info "Available pods in namespace:"
        kubectl get pods -n "${NAMESPACE}"
        exit 1
    fi

    # If specific pod requested
    if [[ -n "${SPECIFIC_POD:-}" ]]; then
        selected_pod="$SPECIFIC_POD"
    # If only one pod exists, select it automatically
    elif [[ $pod_count -eq 1 ]]; then
        selected_pod="$pods"
    # Multiple pods - select first running pod
    else
        selected_pod=$(kubectl get pods -n "${NAMESPACE}" -l "app.kubernetes.io/name=${DEPLOYMENT_NAME}" --no-headers 2>/dev/null | grep -m1 "Running" | awk '{print $1}')
        if [[ -z "$selected_pod" ]]; then
            selected_pod=$(echo "$pods" | head -1)
        fi
    fi

    echo "$selected_pod"
}

show_available_pods() {
    print_header "Available Chat API Pods"

    kubectl get pods -n "${NAMESPACE}" -l "app.kubernetes.io/name=${DEPLOYMENT_NAME}" -o wide
    echo ""
}

# -----------------------------------------------------------------------------
# Log Display Functions
# -----------------------------------------------------------------------------

tail_deployment_logs() {
    local pod="$1"
    local container="${CONTAINER_NAME:-}"
    local since="${SINCE:-}"
    local tail_lines="${TAIL_LINES:-100}"

    print_header "Tailing Logs for ${DEPLOYMENT_NAME}"

    if [[ -n "$container" ]]; then
        log_info "Following logs for pod: ${pod}, container: ${container}"
    else
        log_info "Following logs for pod: ${pod}"
    fi

    echo ""
    log_info "Press Ctrl+C to stop following logs"
    echo ""

    local kubectl_args=(
        logs
        -n "${NAMESPACE}"
        "$pod"
        --tail="$tail_lines"
        -f
    )

    if [[ -n "$container" ]]; then
        kubectl_args+=(-c "$container")
    fi

    if [[ -n "$since" ]]; then
        kubectl_args+=(--since="$since")
    fi

    kubectl "${kubectl_args[@]}"
}

show_all_pods_logs() {
    local pods
    local container="${CONTAINER_NAME:-}"

    print_header "Logs for All ${DEPLOYMENT_NAME} Pods"

    pods=$(kubectl get pods -n "${NAMESPACE}" -l "app.kubernetes.io/name=${DEPLOYMENT_NAME}" --no-headers 2>/dev/null | awk '{print $1}')

    local first=true
    for pod in $pods; do
        if [[ "$first" == "true" ]]; then
            first=false
        else
            echo ""
            echo "----------------------------------------"
            echo ""
        fi

        print_header "Pod: $pod"

        local kubectl_args=(
            logs
            -n "${NAMESPACE}"
            "$pod"
            --tail=50
        )

        if [[ -n "$container" ]]; then
            kubectl_args+=(-c "$container")
        fi

        kubectl "${kubectl_args[@]}" 2>&1 || true
    done
}

# -----------------------------------------------------------------------------
# Main Execution
# -----------------------------------------------------------------------------

show_usage() {
    cat << EOF
Usage: $0 [OPTIONS]

Tail logs for the Chatify Chat API deployment.

OPTIONS:
    -p, --pod POD_NAME        Show logs for a specific pod
    -c, --container CONTAINER Show logs for a specific container
    -a, --all                 Show logs for all pods (not following)
    -s, --since DURATION      Show logs since a duration (e.g., 1h, 30m, 10s)
    -n, --tail LINES          Number of lines to tail (default: 100)
    -l, --list                List available pods and exit
    -h, --help                Show this help message

EXAMPLES:
    # Follow logs for a pod (auto-selected if multiple)
    $0

    # Follow logs for a specific pod
    $0 -p chatify-chat-api-7d9f4c5b6d-abc12

    # Show logs from the last hour
    $0 --since 1h

    # Show logs for all pods (not following)
    $0 --all

    # List available pods
    $0 --list

EOF
}

main() {
    # Parse arguments
    local follow_logs=true
    local show_all=false
    local list_only=false

    while [[ $# -gt 0 ]]; do
        case $1 in
            -p|--pod)
                SPECIFIC_POD="$2"
                shift 2
                ;;
            -c|--container)
                CONTAINER_NAME="$2"
                shift 2
                ;;
            -a|--all)
                show_all=true
                follow_logs=false
                shift
                ;;
            -s|--since)
                SINCE="$2"
                shift 2
                ;;
            -n|--tail)
                TAIL_LINES="$2"
                shift 2
                ;;
            -l|--list)
                list_only=true
                shift
                ;;
            -h|--help)
                show_usage
                exit 0
                ;;
            *)
                log_error "Unknown option: $1"
                show_usage
                exit 1
                ;;
        esac
    done

    log_info "=== Chatify Logs ==="
    echo ""

    check_prerequisites
    check_cluster_and_namespace

    if [[ "$list_only" == "true" ]]; then
        show_available_pods
        exit 0
    fi

    if [[ "$show_all" == "true" ]]; then
        show_all_pods_logs
    else
        local pod
        pod=$(select_pod)
        tail_deployment_logs "$pod"
    fi
}

# Run main function
main "$@"
