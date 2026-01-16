#!/usr/bin/env bash
set -euo pipefail

# -----------------------------------------------------------------------------
# Chatify Port Forward Script
# -----------------------------------------------------------------------------
# This script port-forwards Chatify services for local access.
#
# Usage:
#   ./scripts/port-forward.sh           # Forward all services
#   ./scripts/port-forward.sh chat-api  # Forward specific service
#   ./scripts/port-forward.sh --list    # List available services
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
# Service Definitions
# -----------------------------------------------------------------------------

# Service port mappings (SERVICE_NAME:LOCAL_PORT:SERVICE_PORT:K8S_SERVICE)
declare -A SERVICES=(
    ["chat-api"]="8080:80:chatify-chat-api"
    ["akhq"]="8081:8080:chatify-akhq-nodeport"
    ["kibana"]="5601:5601:chatify-kibana-nodeport"
    ["flink-jobmanager"]="8082:8081:chatify-flink-jobmanager-ui"
)

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
        log_error "kubectl is not installed. Please install kubectl from https://kubernetes.io/docs/tools/"
        exit 1
    fi

    log_success "Prerequisites check passed"
}

check_cluster_and_namespace() {
    if ! kubectl cluster-info &> /dev/null; then
        log_error "Cannot connect to Kubernetes cluster"
        log_info "Please run ./scripts/up.sh to create the environment"
        exit 1
    fi

    if ! kubectl get namespace "${NAMESPACE}" &> /dev/null; then
        log_error "Namespace '${NAMESPACE}' does not exist"
        log_info "Please run ./scripts/up.sh to create the environment"
        exit 1
    fi
}

# -----------------------------------------------------------------------------
# Service Management
# -----------------------------------------------------------------------------

list_services() {
    print_header "Available Services for Port Forwarding"

    echo "Service Name         Local Port    Description"
    echo "--------------------------------------------------------"
    echo "chat-api             8080          Chatify Chat API"
    echo "akhq                 8081          AKHQ Kafka Management UI"
    echo "kibana               5601          Kibana Log Visualization"
    echo "flink-jobmanager     8082          Flink Web UI"
    echo ""
    echo "All services can be forwarded together (default)."
    echo ""
}

check_service_exists() {
    local service_name="$1"

    if ! kubectl get svc "$service_name" -n "${NAMESPACE}" &> /dev/null; then
        log_error "Service '${service_name}' not found in namespace '${NAMESPACE}'"
        log_info "Available services:"
        kubectl get svc -n "${NAMESPACE}"
        return 1
    fi

    return 0
}

check_port_in_use() {
    local port="$1"
    local service_name="$2"

    if lsof -Pi :"$port" -sTCP:LISTEN -t >/dev/null 2>&1 || \
       netstat -an 2>/dev/null | grep ":$port " | grep LISTEN >/dev/null || \
       ss -ln 2>/dev/null | grep ":$port " >/dev/null; then
        log_warn "Port $port is already in use (possibly by another port-forward)"
        log_info "Checking for existing port-forward for ${service_name}..."

        # Try to kill existing port-forward for this service
        pkill -f "kubectl.*port-forward.*${service_name}" || true
        sleep 1

        # Check again
        if lsof -Pi :"$port" -sTCP:LISTEN -t >/dev/null 2>&1 || \
           netstat -an 2>/dev/null | grep ":$port " | grep LISTEN >/dev/null || \
           ss -ln 2>/dev/null | grep ":$port " >/dev/null; then
            log_error "Port $port is still in use. Please manually stop the process using this port."
            log_info "Try: lsof -ti:$port | xargs kill -9"
            return 1
        else
            log_success "Cleared previous port-forward for ${service_name}"
        fi
    fi

    return 0
}

port_forward_service() {
    local service_key="$1"
    local config="${SERVICES[$service_key]}"
    local local_port
    local service_port
    local k8s_service

    IFS=':' read -r local_port service_port k8s_service <<< "$config"

    print_header "Port Forwarding: ${service_key}"

    # Check if service exists
    if ! check_service_exists "$k8s_service"; then
        return 1
    fi

    # Check if port is available
    if ! check_port_in_use "$local_port" "$service_key"; then
        return 1
    fi

    log_info "Starting port-forward for ${service_key}..."
    log_info "Local: http://localhost:${local_port} -> Service: ${k8s_service}:${service_port}"
    echo ""

    # Start port-forward in background
    kubectl port-forward -n "${NAMESPACE}" "svc/${k8s_service}" "${local_port}:${service_port}" >/dev/null 2>&1 &
    local pf_pid=$!

    # Give it a moment to start
    sleep 2

    # Verify the process is still running
    if ps -p "$pf_pid" > /dev/null; then
        log_success "Port-forward started for ${service_key} (PID: ${pf_pid})"
        echo ""
        echo -e "${GREEN}Access ${service_key} at: http://localhost:${local_port}${NC}"
        echo ""
        echo "Press Ctrl+C to stop this port-forward"
        echo ""

        # Wait for interrupt
        wait "$pf_pid"
    else
        log_error "Failed to start port-forward for ${service_key}"
        return 1
    fi
}

port_forward_all_services() {
    local pids=()
    local ports=()

    print_header "Port Forwarding All Services"

    log_info "Starting port-forwards for all services..."
    echo ""
    echo "Services will be accessible at:"
    echo "  Chat API (HTTP):     http://localhost:8080"
    echo "  AKHQ (Kafka UI):     http://localhost:8081"
    echo "  Kibana:              http://localhost:5601"
    echo "  Flink Web UI:        http://localhost:8082"
    echo ""
    log_info "Press Ctrl+C to stop all port-forwards"
    echo ""

    # Start each port-forward in background
    for service_key in "${!SERVICES[@]}"; do
        local config="${SERVICES[$service_key]}"
        local local_port
        local service_port
        local k8s_service

        IFS=':' read -r local_port service_port k8s_service <<< "$config"

        # Check if service exists
        if ! check_service_exists "$k8s_service"; then
            log_warn "Skipping ${service_key} (service not found)"
            continue
        fi

        # Check if port is in use, skip if unavailable
        if lsof -Pi :"$local_port" -sTCP:LISTEN -t >/dev/null 2>&1 || \
           netstat -an 2>/dev/null | grep ":$local_port " | grep LISTEN >/dev/null || \
           ss -ln 2>/dev/null | grep ":$local_port " >/dev/null; then
            log_warn "Port ${local_port} already in use, skipping ${service_key}"
            continue
        fi

        log_info "Starting port-forward for ${service_key}..."

        kubectl port-forward -n "${NAMESPACE}" "svc/${k8s_service}" "${local_port}:${service_port}" >/dev/null 2>&1 &
        local pf_pid=$!
        pids+=("$pf_pid")
        ports+=("$local_port:$service_key")

        sleep 1
    done

    # Verify all started successfully
    echo ""
    log_success "Port-forwards started:"
    for i in "${!ports[@]}"; do
        local port_info="${ports[$i]}"
        local pid="${pids[$i]}"

        if ps -p "$pid" > /dev/null 2>&1; then
            echo -e "  ${GREEN}[RUNNING]${NC} ${port_info} (PID: ${pid})"
        else
            echo -e "  ${RED}[FAILED]${NC} ${port_info}"
        fi
    done
    echo ""

    # Cleanup function
    cleanup() {
        echo ""
        log_info "Stopping all port-forwards..."
        for pid in "${pids[@]}"; do
            kill "$pid" 2>/dev/null || true
        done
        log_success "All port-forwards stopped"
        exit 0
    }

    trap cleanup SIGINT SIGTERM

    # Wait for all background processes
    for pid in "${pids[@]}"; do
        wait "$pid" 2>/dev/null || true
    done
}

# -----------------------------------------------------------------------------
# Main Execution
# -----------------------------------------------------------------------------

show_usage() {
    cat << EOF
Usage: $0 [SERVICE]

Port-forward Chatify services for local access.

ARGUMENTS:
    SERVICE                Optional service name to forward (default: all)

AVAILABLE SERVICES:
    chat-api              Chatify Chat API (http://localhost:8080)
    akhq                  AKHQ Kafka Management UI (http://localhost:8081)
    kibana                Kibana Log Visualization (http://localhost:5601)
    flink-jobmanager      Flink Web UI (http://localhost:8082)

OPTIONS:
    -l, --list            List available services and exit
    -h, --help            Show this help message

EXAMPLES:
    # Forward all services (default)
    $0

    # Forward only Chat API
    $0 chat-api

    # Forward only AKHQ
    $0 akhq

    # List available services
    $0 --list

NOTES:
    - Press Ctrl+C to stop port-forwards
    - Each service runs in the background
    - Ports must be available on localhost

EOF
}

main() {
    local service_to_forward=""
    local list_only=false

    # Parse arguments
    while [[ $# -gt 0 ]]; do
        case $1 in
            -l|--list)
                list_only=true
                shift
                ;;
            -h|--help)
                show_usage
                exit 0
                ;;
            chat-api|akhq|kibana|flink-jobmanager)
                service_to_forward="$1"
                shift
                ;;
            *)
                log_error "Unknown option or service: $1"
                show_usage
                exit 1
                ;;
        esac
    done

    log_info "=== Chatify Port Forward ==="
    echo ""

    check_prerequisites
    check_cluster_and_namespace

    if [[ "$list_only" == "true" ]]; then
        list_services
        exit 0
    fi

    if [[ -n "$service_to_forward" ]]; then
        port_forward_service "$service_to_forward"
    else
        port_forward_all_services
    fi
}

# Run main function
main "$@"
