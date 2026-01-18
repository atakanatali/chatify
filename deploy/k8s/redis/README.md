# Chatify Redis Deployment

This directory contains Kubernetes manifests for deploying Redis as the caching layer for Chatify.

## Purpose

Redis provides low-latency data storage for:
- **Presence Tracking**: Real-time user online/offline status across SignalR connections
- **Rate Limiting**: Per-user message rate limits to prevent spam and abuse
- **Pod Identity Management**: Distributed coordination across multiple ChatApi pods

## Files

- `10-redis-deployment.yaml` - Redis deployment with ClusterIP and NodePort services

## Deployment

```bash
# Deploy Redis
kubectl apply -f deploy/k8s/redis/10-redis-deployment.yaml

# Wait for Redis to be ready
kubectl wait --for=condition=ready pod -l app.kubernetes.io/name=chatify-redis -n chatify --timeout=120s

# Verify deployment
kubectl get pods -n chatify -l app.kubernetes.io/name=chatify-redis
kubectl get svc -n chatify -l app.kubernetes.io/name=chatify-redis
```

## Configuration

- **Image**: `redis:8.0-alpine`
- **Memory Limit**: 256MB with `allkeys-lru` eviction policy
- **Persistence**: AOF (Append Only File) with everysec fsync
- **Replicas**: 1 (single instance for local development)
- **Resources**:
  - Requests: 250m CPU, 256Mi memory
  - Limits: 1000m CPU, 512Mi memory

## Services

### ClusterIP Service
- **Name**: `chatify-redis`
- **Port**: 6379
- **Usage**: Internal cluster communication

### NodePort Service
- **Name**: `chatify-redis-nodeport`
- **Port**: 30079 (external), 6379 (container)
- **Usage**: External access from host machine

## Health Checks

- **Liveness Probe**: TCP socket check on port 6379
- **Readiness Probe**: `redis-cli ping` command

## Data Persistence

For local development, Redis uses `emptyDir` with 1Gi size limit. Data is lost when pods are restarted.

For production, consider using PersistentVolumes for data durability.

## External Access

From the host machine (via kind port mapping):

```bash
# Connect to Redis
redis-cli -h localhost -p 6379

# Test connection
redis-cli -h localhost -p 6379 PING
```

From within the cluster:

```bash
# Port forward to local port
kubectl port-forward -n chatify svc/chatify-redis-nodeport 6379:30079

# Connect
redis-cli -h localhost -p 6379
```

## Monitoring

```bash
# View logs
kubectl logs -f -n chatify deployment/chatify-redis

# Check Redis info
kubectl exec -n chatify deployment/chatify-redis -- redis-cli INFO

# Monitor commands in real-time
kubectl exec -n chatify deployment/chatify-redis -- redis-cli MONITOR
```

## Production Considerations

For production deployments, consider:
- **Redis Sentinel** for high availability and automatic failover
- **Redis Cluster** for horizontal scaling and data sharding
- **Persistent volumes** instead of emptyDir for data durability
- **Memory optimization** based on actual usage patterns
- **Security**: Enable AUTH and TLS for production environments
- **Backup strategy**: Implement regular RDB snapshots or AOF rewrite
