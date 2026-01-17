# Chatify ScyllaDB Deployment

## Overview

This directory contains Kubernetes manifests for deploying ScyllaDB as the primary data store for Chatify chat history.

## Schema Management

**Important:** Chatify uses a **code-first schema migration system**. The database schema is **applied automatically by the application on startup**, not by a separate init job.

### How Schema is Applied

1. **Keyspace Creation**: When ChatApi starts, `AddScyllaChatify` extension method:
   - Connects to the ScyllaDB cluster
   - Creates the keyspace if it doesn't exist (using `SimpleStrategy` with RF=1 for development)
   - Connects to the keyspace

2. **Schema Migrations**: The `ScyllaSchemaMigrationBackgroundService` runs during application startup:
   - Checks if `ApplySchemaOnStartup` is enabled (default: `true`)
   - Creates the `schema_migrations` table if it doesn't exist
   - Discovers all migrations implementing `IScyllaSchemaMigration`
   - Applies pending migrations in order (by ModuleName, then MigrationId)
   - Records each applied migration in the history table

### Configuration

Schema migration behavior is controlled via the `Chatify:Scylla` configuration section:

| Configuration Key | Type | Default | Description |
|-------------------|------|---------|-------------|
| `CHATIFY__SCYLLA__KEYSPACE` | string | "chatify" | Target keyspace for migrations |
| `CHATIFY__SCYLLA__APPLYSCHEMAONSTARTUP` | boolean | true | Auto-apply migrations on startup |
| `CHATIFY__SCYLLA__SCHEMAMIGRATIONTABLENAME` | string | "schema_migrations" | Table name for migration history |
| `CHATIFY__SCYLLA__FAILFASTONSCHEMAERROR` | boolean | true | Stop startup if migration fails |

### Why This Approach?

- **Simplicity**: No separate init job or manual schema scripts
- **Idempotency**: Migrations use `IF NOT EXISTS` clauses
- **Safety**: Migration history prevents re-application
- **Flexibility**: Easy to add new migrations without touching k8s manifests
- **Testability**: Migrations are C# classes that can be unit tested

## Deployment

### Prerequisites

- Kubernetes cluster (kind, minikube, or cloud provider)
- kubectl configured to access the cluster

### Deploy ScyllaDB

```bash
# Deploy the ConfigMap with ScyllaDB configuration
kubectl apply -f deploy/k8s/scylla/10-scylla-config.yaml

# Deploy the ScyllaDB StatefulSet
kubectl apply -f deploy/k8s/scylla/20-scylla-statefulset.yaml

# Deploy the ScyllaDB services
kubectl apply -f deploy/k8s/scylla/30-scylla-service.yaml

# Wait for ScyllaDB to be ready
kubectl wait --for=condition=ready pod -l app.kubernetes.io/name=chatify-scylla -n chatify --timeout=300s
```

### Verify Deployment

```bash
# Check pods
kubectl get pods -n chatify -l app.kubernetes.io/name=chatify-scylla

# Check services
kubectl get svc -n chatify -l app.kubernetes.io/name=chatify-scylla

# View logs
kubectl logs -f -n chatify statefulset/chatify-scylla
```

### Access ScyllaDB

From within the cluster:
```bash
kubectl exec -n chatify -it statefulset/chatify-scylla -- cqlsh
```

From the host machine (via NodePort):
```bash
cqlsh localhost --port 30042
```

## Configuration

The ScyllaDB ConfigMap (`10-scylla-config.yaml`) configures:

- **Developer Mode**: Enabled for kind/local development
- **Memory**: 1GB allocation (adjust based on needs)
- **Authentication**: Disabled for development (enable for production)
- **SSL/TLS**: Disabled for development (enable for production)

## Production Considerations

For production deployments, consider:

- **Multi-node cluster**: Deploy 3+ replicas for high availability
- **NetworkTopologyStrategy**: Replace SimpleStrategy with proper data center awareness
- **Replication factor**: Use RF=3 for production critical data
- **Resource allocation**: 8+ CPU cores, 16GB+ memory per node
- **SSD storage**: Sufficient IOPS for write-heavy workloads
- **Backups**: Configure regular snapshots using nodetool
- **Monitoring**: Deploy ScyllaDB Monitoring Stack (Prometheus + Grafana)
- **Security**: Enable SSL/TLS and configure authentication

## Troubleshooting

### Check Keyspace Exists

```bash
kubectl exec -n chatify statefulset/chatify-scylla -- cqlsh -e "DESCRIBE KEYSPACES;"
```

### Check Schema Migrations

```bash
kubectl exec -n chatify statefulset/chatify-scylla -- cqlsh -e "SELECT * FROM chatify.schema_migrations;"
```

### Check Tables

```bash
kubectl exec -n chatify statefulset/chatify-scylla -- cqlsh -e "DESCRIBE KEYSPACE chatify;"
```

### View ChatApi Schema Migration Logs

```bash
kubectl logs -n chatify deployment/chatify-chat-api | grep "ScyllaSchemaMigration"
```

## Cleanup

```bash
# Delete ScyllaDB resources
kubectl delete -f deploy/k8s/scylla/

# Delete the namespace (removes all Chatify resources)
kubectl delete namespace chatify
```

## References

- [ScyllaDB Documentation](https://docs.scylladb.com/)
- [ScyllaDB CQL Reference](https://docs.scylladb.com/getting-started/cql/)
- [ScyllaDB Kubernetes Operator](https://github.com/scylladb/scylla-operator/)
