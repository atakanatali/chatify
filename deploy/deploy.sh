#!/bin/bash
set -e

echo "🚀 Starting Chatify Kubernetes Deployment..."

# 1. Namespace
echo "📦 Creating Namespace..."
kubectl apply -f deploy/k8s/00-namespace.yaml

# 2. Infrastructure
echo "🏗️  Deploying Infrastructure..."
echo "   - Redis..."
kubectl apply -f deploy/k8s/redis/
echo "   - ScyllaDB..."
kubectl apply -f deploy/k8s/scylla/
echo "   - Elasticsearch..."
kubectl apply -f deploy/k8s/elastic/
# Kibana Init Job (Data View)
kubectl apply -f deploy/k8s/elastic/30-kibana-init-job.yaml
echo "   - Kafka..."
kubectl apply -f deploy/k8s/kafka/

# Wait for Kafka to be ready and create topic
echo "⏳ Waiting for Kafka to be ready..."
kubectl wait --for=condition=Ready pod/chatify-kafka-0 -n chatify --timeout=120s || true
echo "📦 Creating 'chat-events' topic..."
# We use a loop or just a direct attempt. If it fails (pod not fully ready), we retry.
for i in {1..5}; do
    if kubectl exec -n chatify chatify-kafka-0 -- rpk topic create chat-events -p 3 -r 1; then
        echo "✅ Topic 'chat-events' created."
        break
    fi
    echo "   Retrying topic creation in 5s..."
    sleep 5
done

# 2.5 Logging
echo "🪵 Deploying Logging (Filebeat)..."
kubectl apply -f deploy/k8s/logging/

# 3. Tools
echo "🛠️  Deploying Tools..."
echo "   - AKHQ (Kafka UI)..."
kubectl apply -f deploy/k8s/akhq/
echo "   - Flink (JobManager & TaskManager)..."
kubectl apply -f deploy/k8s/flink/10-flink-jobmanager.yaml
kubectl apply -f deploy/k8s/flink/20-flink-taskmanager.yaml

# 4. Application
echo "📱 Deploying Chat API..."
kubectl apply -f deploy/k8s/chat-api/

# 5. Flink Jobs (Wait a bit for infra? K8s is eventually consistent)
echo "🌊 Deploying Flink Jobs..."
kubectl apply -f deploy/k8s/flink/30-flink-job.yaml

echo "✅ Deployment commands submitted!"
echo "⏳ Waiting for pods to stabilize... (You can verify with: kubectl get pods -n chatify)"

# Optional: Wait loop or status check
kubectl get pods -n chatify
