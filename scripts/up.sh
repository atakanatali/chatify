#!/usr/bin/env bash
set -euo pipefail

# -----------------------------------------------------------------------------
# Chatify Environment Bootstrap Script
# -----------------------------------------------------------------------------
# This script creates a kind cluster and deploys all Chatify infrastructure
# components and applications in the correct order with proper wait conditions.
#
# Usage: ./scripts/up.sh
# -----------------------------------------------------------------------------

# Color output for better readability
readonly RED='\033[0;31m'
readonly GREEN='\033[0;32m'
readonly YELLOW='\033[1;33m'
readonly BLUE='\033[0;34m'
readonly NC='\033[0m' # No Color

# Script directory
readonly SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
readonly PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"
readonly K8S_DIR="${PROJECT_ROOT}/deploy/k8s"
readonly KIND_CONFIG="${PROJECT_ROOT}/deploy/kind/kind-cluster.yaml"

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

    if ! command -v kubectl &> /dev/null; then
        log_error "kubectl is not installed. Please install kubectl from https://kubernetes.io/docs/tasks/tools/"
        exit 1
    fi

    log_success "Prerequisites check passed"
}

# -----------------------------------------------------------------------------
# Kind Cluster Management
# -----------------------------------------------------------------------------

create_kind_cluster() {
    log_info "Creating kind cluster: ${CLUSTER_NAME}"

    if kind get clusters | grep -q "^${CLUSTER_NAME}$"; then
        log_warn "Cluster '${CLUSTER_NAME}' already exists. Deleting existing cluster..."
        kind delete cluster --name "${CLUSTER_NAME}"
    fi

    kind create cluster --config "${KIND_CONFIG}"
    log_success "Kind cluster '${CLUSTER_NAME}' created"

    # Set kubectl context to the new cluster
    kubectl cluster-info context
}

# -----------------------------------------------------------------------------
# Kubernetes Resource Management
# -----------------------------------------------------------------------------

apply_namespace() {
    log_info "Applying namespace: ${NAMESPACE}"

    kubectl apply -f "${K8S_DIR}/00-namespace.yaml"

    # Wait for namespace to be created
    while ! kubectl get namespace "${NAMESPACE}" &> /dev/null; do
        sleep 1
    done

    log_success "Namespace '${NAMESPACE}' created"
}

# Generic function to deploy and wait for a resource
deploy_and_wait() {
    local resource_type="$1"
    local resource_name="$2"
    local manifest="$3"
    local wait_label="$4"
    local timeout="${5:-120s}"

    log_info "Deploying ${resource_type}: ${resource_name}"

    kubectl apply -f "${manifest}"

    log_info "Waiting for ${resource_type} '${resource_name}' to be ready (timeout: ${timeout})..."
    kubectl wait --for=condition=ready pod \
        -l "${wait_label}" \
        -n "${NAMESPACE}" \
        --timeout="${timeout}" || {
        log_error "Timeout waiting for ${resource_type} '${resource_name}'"
        log_info "Checking pod status..."
        kubectl get pods -n "${NAMESPACE}" -l "${wait_label}"
        return 1
    }

    log_success "${resource_type} '${resource_name}' is ready"
}

# Generic function to deploy StatefulSet and wait
deploy_statefulset_and_wait() {
    local resource_name="$1"
    local manifest="$2"
    local wait_label="$3"
    local timeout="${4:-180s}"

    log_info "Deploying StatefulSet: ${resource_name}"

    kubectl apply -f "${manifest}"

    log_info "Waiting for StatefulSet '${resource_name}' to be ready (timeout: ${timeout})..."
    kubectl wait --for=condition=ready pod \
        -l "${wait_label}" \
        -n "${NAMESPACE}" \
        --timeout="${timeout}" || {
        log_error "Timeout waiting for StatefulSet '${resource_name}'"
        kubectl get pods -n "${NAMESPACE}" -l "${wait_label}"
        return 1
    }

    log_success "StatefulSet '${resource_name}' is ready"
}

# -----------------------------------------------------------------------------
# Component Deployment Functions
# -----------------------------------------------------------------------------

deploy_kafka() {
    log_info "=== Deploying Kafka (Redpanda) ==="
    deploy_statefulset_and_wait \
        "chatify-kafka" \
        "${K8S_DIR}/kafka/10-statefulset.yaml" \
        "app.kubernetes.io/name=chatify-kafka" \
        "180s"

    # Deploy topic initialization job
    log_info "Deploying Kafka topic initialization job..."
    kubectl apply -f "${K8S_DIR}/kafka/20-topic-init-job.yaml"
    log_success "Kafka topic initialization job deployed"

    # Wait for job to complete
    log_info "Waiting for Kafka topic initialization job to complete..."
    kubectl wait --for=condition=complete job/chatify-kafka-topic-init \
        -n "${NAMESPACE}" \
        --timeout="60s" || {
        log_warn "Kafka topic init job did not complete successfully, checking logs..."
        kubectl logs -n "${NAMESPACE}" job/chatify-kafka-topic-init --tail=20 || true
    }
    log_success "Kafka deployment completed"
}

deploy_redis() {
    log_info "=== Deploying Redis ==="
    deploy_and_wait \
        "Redis" \
        "chatify-redis" \
        "${K8S_DIR}/redis/10-redis-deployment.yaml" \
        "app.kubernetes.io/name=chatify-redis" \
        "120s"
    log_success "Redis deployment completed"
}

deploy_elastic() {
    log_info "=== Deploying Elasticsearch ==="
    deploy_statefulset_and_wait \
        "chatify-elastic" \
        "${K8S_DIR}/elastic/10-elasticsearch.yaml" \
        "app.kubernetes.io/name=chatify-elastic" \
        "300s"

    log_info "Deploying Kibana..."
    kubectl apply -f "${K8S_DIR}/elastic/20-kibana.yaml"

    log_info "Waiting for Kibana to be ready..."
    kubectl wait --for=condition=ready pod \
        -l "app.kubernetes.io/name=chatify-kibana" \
        -n "${NAMESPACE}" \
        --timeout="300s"

    log_success "Elasticsearch and Kibana deployment completed"
}

deploy_scylla() {
    log_info "=== Deploying ScyllaDB ==="

    # Deploy ScyllaDB ConfigMap
    log_info "Applying ScyllaDB configuration..."
    kubectl apply -f "${K8S_DIR}/scylla/10-scylla-config.yaml"

    # Deploy ScyllaDB StatefulSet
    deploy_statefulset_and_wait \
        "chatify-scylla" \
        "${K8S_DIR}/scylla/20-scylla-statefulset.yaml" \
        "app.kubernetes.io/name=chatify-scylla" \
        "300s"

    # Deploy ScyllaDB Service
    log_info "Applying ScyllaDB service..."
    kubectl apply -f "${K8S_DIR}/scylla/30-scylla-service.yaml"

    # Deploy and run schema initialization job
    log_info "Deploying ScyllaDB schema initialization job..."
    kubectl apply -f "${K8S_DIR}/scylla/40-scylla-schema-init-job.yaml"

    log_info "Waiting for schema initialization job to complete..."
    kubectl wait --for=condition=complete job/chatify-scylla-schema-init \
        -n "${NAMESPACE}" \
        --timeout="120s" || {
        log_warn "Schema init job did not complete successfully, checking logs..."
        kubectl logs -n "${NAMESPACE}" job/chatify-scylla-schema-init --tail=30 || true
    }

    log_success "ScyllaDB deployment completed"
}

deploy_flink() {
    log_info "=== Deploying Apache Flink ==="

    # Deploy Flink JobManager
    log_info "Deploying Flink JobManager..."
    kubectl apply -f "${K8S_DIR}/flink/10-flink-jobmanager.yaml"

    log_info "Waiting for Flink JobManager to be ready..."
    kubectl wait --for=condition=ready pod \
        -l "app.kubernetes.io/name=chatify-flink-jobmanager" \
        -n "${NAMESPACE}" \
        --timeout="180s"

    # Deploy Flink TaskManager
    log_info "Deploying Flink TaskManager..."
    kubectl apply -f "${K8S_DIR}/flink/20-flink-taskmanager.yaml"

    log_info "Waiting for Flink TaskManager to be ready..."
    kubectl wait --for=condition=ready pod \
        -l "app.kubernetes.io/name=chatify-flink-taskmanager" \
        -n "${NAMESPACE}" \
        --timeout="180s"

    # Deploy Flink job (placeholder)
    log_info "Deploying Flink job..."
    kubectl apply -f "${K8S_DIR}/flink/30-flink-job.yaml" || {
        log_warn "Flink job deployment failed or already exists"
    }

    log_success "Flink deployment completed"
}

deploy_akhq() {
    log_info "=== Deploying AKHQ (Kafka Management UI) ==="
    deploy_and_wait \
        "AKHQ" \
        "chatify-akhq" \
        "${K8S_DIR}/akhq/10-deployment.yaml" \
        "app.kubernetes.io/name=chatify-akhq" \
        "120s"
    log_success "AKHQ deployment completed"
}

deploy_chat_api() {
    log_info "=== Deploying Chatify Chat API ==="

    # Deploy ConfigMap
    log_info "Applying Chat API ConfigMap..."
    kubectl apply -f "${K8S_DIR}/chat-api/10-configmap.yaml"

    # Deploy Deployment
    log_info "Deploying Chat API Deployment..."
    kubectl apply -f "${K8S_DIR}/chat-api/20-deployment.yaml"

    log_info "Waiting for Chat API pods to be ready..."
    kubectl wait --for=condition=ready pod \
        -l "app.kubernetes.io/name=chatify-chat-api" \
        -n "${NAMESPACE}" \
        --timeout="300s"

    # Deploy Service
    log_info "Applying Chat API Service..."
    kubectl apply -f "${K8S_DIR}/chat-api/30-service.yaml"

    log_success "Chat API deployment completed"
}

# -----------------------------------------------------------------------------
# Post-Deployment Verification
# -----------------------------------------------------------------------------

verify_deployment() {
    log_info "=== Verifying Deployment ==="

    echo ""
    kubectl get pods -n "${NAMESPACE}"
    echo ""

    kubectl get svc -n "${NAMESPACE}"
    echo ""

    log_success "Deployment verification completed"
}

print_access_info() {
    log_info "=== Access Information ==="
    echo ""
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
    echo "Use ./scripts/port-forward.sh to port-forward services individually."
    echo ""
    echo "To check deployment status:"
    echo "  ./scripts/status.sh"
    echo ""
    echo "To view logs:"
    echo "  ./scripts/logs-chatify.sh"
    echo ""
}

# -----------------------------------------------------------------------------
# Main Execution
# -----------------------------------------------------------------------------

main() {
    log_info "=== Chatify Environment Bootstrap ==="
    log_info "Starting deployment process..."
    echo ""

    check_prerequisites
    create_kind_cluster
    apply_namespace

    # Deploy infrastructure components in dependency order
    deploy_kafka
    deploy_redis
    deploy_elastic
    deploy_scylla
    deploy_flink
    deploy_akhq

    # Deploy application
    deploy_chat_api

    # Verify and provide access information
    verify_deployment
    print_access_info

    log_success "=== Chatify Environment Bootstrap Complete ==="
}

# Run main function
main "$@"
