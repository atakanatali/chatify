#!/usr/bin/env bash
set -euo pipefail

# -----------------------------------------------------------------------------
# Chatify Environment Status Script
# -----------------------------------------------------------------------------
# This script shows the status of pods and services in the chatify namespace.
#
# Usage: ./scripts/status.sh
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

# -----------------------------------------------------------------------------
# Status Display Functions
# -----------------------------------------------------------------------------

check_cluster() {
    print_header "Cluster Status"

    if ! kubectl cluster-info &> /dev/null; then
        log_error "Cannot connect to Kubernetes cluster"
        log_info "Please ensure the cluster is running:"
        echo "  - For kind: kind get clusters"
        echo "  - Start cluster if needed: kind start chatify"
        exit 1
    fi

    log_success "Connected to Kubernetes cluster"
    kubectl cluster-info
    echo ""
}

check_namespace() {
    print_header "Namespace Status"

    if ! kubectl get namespace "${NAMESPACE}" &> /dev/null; then
        log_error "Namespace '${NAMESPACE}' does not exist"
        log_info "Please run ./scripts/up.sh to create the environment"
        exit 1
    fi

    log_success "Namespace '${NAMESPACE}' exists"
    echo ""
}

show_pods() {
    print_header "Pods in ${NAMESPACE} Namespace"

    kubectl get pods -n "${NAMESPACE}" -o wide

    echo ""
    show_pod_summary
}

show_pod_summary() {
    local total_pods
    local running_pods
    local pending_pods
    local failed_pods

    total_pods=$(kubectl get pods -n "${NAMESPACE}" --no-headers 2>/dev/null | wc -l || echo "0")
    running_pods=$(kubectl get pods -n "${NAMESPACE}" --no-headers 2>/dev/null | grep -c "Running" || echo "0")
    pending_pods=$(kubectl get pods -n "${NAMESPACE}" --no-headers 2>/dev/null | grep -c "Pending" || echo "0")
    failed_pods=$(kubectl get pods -n "${NAMESPACE}" --no-headers 2>/dev/null | grep -c -E "(Error|CrashLoopBackOff|Failed)" || echo "0")

    echo -e "${CYAN}Pod Summary:${NC}"
    echo "  Total:   ${total_pods}"
    echo -e "  Running: ${GREEN}${running_pods}${NC}"
    echo -e "  Pending: ${YELLOW}${pending_pods}${NC}"
    echo -e "  Failed:  ${RED}${failed_pods}${NC}"
    echo ""
}

show_services() {
    print_header "Services in ${NAMESPACE} Namespace"

    kubectl get svc -n "${NAMESPACE}"

    echo ""
    show_service_endpoints
}

show_service_endpoints() {
    print_header "Service Endpoints (External Access)"

    echo "Services are accessible via the following ports:"
    echo ""
    echo "  Chat API (HTTP):     http://localhost:8080"
    echo "  Chat API (HTTPS):    https://localhost:8443"
    echo "  Kafka:               localhost:9092"
    echo "  ScyllaDB:            localhost:9042"
    echo "  Redis:               localhost:6379"
    echo "  Elasticsearch:       http://localhost:9200"
    echo "  AKHQ (Kafka UI):     http://localhost:8081"
    echo "  Kibana:              http://localhost:5601"
    echo "  Flink Web UI:        http://localhost:8082"
    echo ""
}

show_statefulsets() {
    print_header "StatefulSets in ${NAMESPACE} Namespace"

    if kubectl get statefulset -n "${NAMESPACE}" --no-headers 2>/dev/null | grep -q .; then
        kubectl get statefulset -n "${NAMESPACE}"
    else
        log_info "No StatefulSets found in namespace"
    fi
    echo ""
}

show_deployments() {
    print_header "Deployments in ${NAMESPACE} Namespace"

    if kubectl get deployment -n "${NAMESPACE}" --no-headers 2>/dev/null | grep -q .; then
        kubectl get deployment -n "${NAMESPACE}"
    else
        log_info "No Deployments found in namespace"
    fi
    echo ""
}

show_jobs() {
    print_header "Jobs in ${NAMESPACE} Namespace"

    if kubectl get job -n "${NAMESPACE}" --no-headers 2>/dev/null | grep -q .; then
        kubectl get job -n "${NAMESPACE}"
    else
        log_info "No Jobs found in namespace"
    fi
    echo ""
}

show_events() {
    print_header "Recent Events in ${NAMESPACE} Namespace"

    local events
    events=$(kubectl get events -n "${NAMESPACE}" --sort-by='.lastTimestamp' --no-headers 2>/dev/null | tail -10 || echo "")

    if [[ -n "$events" ]]; then
        echo "$events" | awk '{printf "  %-20s %-10s %-30s %s\n", $1, $2, $3, $4}'
    else
        log_info "No recent events found"
    fi
    echo ""
}

show_resource_usage() {
    print_header "Resource Usage Summary"

    if command -v kubectl-top &> /dev/null || kubectl top --help &> /dev/null; then
        echo "Pod Resource Usage:"
        kubectl top pods -n "${NAMESPACE}" 2>/dev/null || log_info "Metrics server not available"
        echo ""
    else
        log_info "Metrics not available (metrics-server not installed)"
    fi
}

# -----------------------------------------------------------------------------
# Main Execution
# -----------------------------------------------------------------------------

main() {
    log_info "=== Chatify Environment Status ==="
    echo ""

    check_prerequisites
    check_cluster
    check_namespace

    show_pods
    show_deployments
    show_statefulsets
    show_jobs
    show_services
    show_events
    show_resource_usage

    log_success "=== Status Check Complete ==="
}

# Run main function
main "$@"
