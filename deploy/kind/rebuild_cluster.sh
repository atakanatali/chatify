#!/bin/bash
set -e

# Get the project root directory (two levels up from this script)
PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../" && pwd)"
cd "$PROJECT_ROOT" || exit 1

echo "🚀 Starting Full Cluster Rebuild..."

# Check if kind cluster exists and delete it
if kind get clusters | grep -q "chatify"; then
    echo "🗑️ Deleting existing Kind cluster..."
    kind delete cluster --name chatify
fi

echo "📦 Rebuilding Docker images..."
# Build Chat API
echo "  - Building Chat API..."
docker build -f src/Hosts/Chatify.Api/Dockerfile -t chatify-api:latest .

echo "🌱 Creating new Kind cluster..."
kind create cluster --config deploy/kind/kind-cluster.yaml --name chatify

echo "🚚 Loading images into Kind..."
kind load docker-image chatify-api:latest --name chatify

kind load docker-image chatify-api:latest --name chatify
# Pre-pull external images to avoid timeouts (optional but good)
# docker pull redpandadata/redpanda:v22.3.13
# kind load docker-image redpandadata/redpanda:v22.3.13 --name chatify

echo "🔧 Deploying infrastructure & services..."
# Using the existing deploy script which applies manifests in order
./deploy/deploy.sh

echo "⏳ Waiting for initial pod creation..."
sleep 10
kubectl get pods -n chatify

echo "✅ Rebuild Complete! Please monitor pod status."
