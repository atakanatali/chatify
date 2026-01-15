# ScyllaDB Schema for Chatify

This document defines the ScyllaDB (Cassandra-compatible) schema for storing chat message history in Chatify.

## Overview

The schema is optimized for high-throughput chat message storage with efficient time-based queries within scopes. It supports idempotent writes to ensure exactly-once semantics in the face of retries.

## Keyspace

### chatify

The `chatify` keyspace contains all Chatify data. Create it with appropriate replication based on your deployment:

```sql
-- Production (multi-datacenter)
CREATE KEYSPACE IF NOT EXISTS chatify
WITH REPLICATION = {
  'class': 'NetworkTopologyStrategy',
  'dc1': 3,
  'dc2': 3
} AND DURABLE_WRITES = true;

-- Development/testing (single datacenter)
CREATE KEYSPACE IF NOT EXISTS chatify
WITH REPLICATION = {
  'class': 'SimpleStrategy',
  'replication_factor': 1
} AND DURABLE_WRITES = true;
```

**Replication Strategy Selection:**
- `NetworkTopologyStrategy`: Use in production with multiple datacenters for disaster recovery
- `SimpleStrategy`: Use in development/testing with a single datacenter
- Replication factor of 3 provides fault tolerance while balancing storage costs

## Tables

### chat_messages

Stores individual chat messages with clustering by time for efficient chronological queries.

```sql
CREATE TABLE IF NOT EXISTS chat_messages (
    scope_id text,
    created_at_utc timestamp,
    message_id uuid,
    sender_id text,
    text text,
    origin_pod_id text,
    broker_partition int,
    broker_offset bigint,
    PRIMARY KEY ((scope_id), created_at_utc, message_id)
) WITH CLUSTERING ORDER BY (created_at_utc ASC)
AND gc_grace_seconds = 86400
AND compaction = {
  'class': 'LeveledCompactionStrategy',
  'sstable_size_in_mb': 160
}
AND comment = 'Chat messages with time-based clustering for efficient scope queries';
```

**Table Schema Explanation:**

| Column | Type | Description |
|--------|------|-------------|
| `scope_id` | `text` | Composite key: "scope_type:scope_id" (partition key) |
| `created_at_utc` | `timestamp` | Message creation time (clustering key) |
| `message_id` | `uuid` | Unique message identifier (clustering column) |
| `sender_id` | `text` | User ID who sent the message |
| `text` | `text` | Message content (max 4096 chars per domain policy) |
| `origin_pod_id` | `text` | Pod that created the message (for debugging) |
| `broker_partition` | `int` | Message broker partition (set by consumer, null on API writes) |
| `broker_offset` | `bigint` | Message broker offset (set by consumer, null on API writes) |

**Primary Key Design:**

- **Partition Key:** `(scope_id)` - Groups all messages for a scope together on the same nodes
  - Format: `"scope_type:scope_id"` (e.g., `"Channel:general"`, `"DirectMessage:user1-user2"`)
  - Ensures efficient queries for a specific scope
- **Clustering Keys:**
  - `created_at_utc ASC` - Orders messages chronologically for time-range queries
  - `message_id` - Provides uniqueness and breaks ties for identical timestamps

**Table Options:**

- `gc_grace_seconds = 86400` (24 hours): Time before deleted data is permanently removed
- `LeveledCompactionStrategy`: Optimized for read-heavy workloads with low read latency
- `sstable_size_in_mb = 160`: Recommended size for ScyllaDB

## Composite scope_id Format

The `scope_id` column uses a composite format to include both scope type and scope identifier:

```
format: "{ScopeType}:{ScopeId}"

examples:
- "Channel:general"
- "Channel:random"
- "DirectMessage:user1-user2"
- "DirectMessage:alice-bob-conversation-123"
```

**Why Composite Key:**
- Includes scope type in partitioning for better data distribution
- Enables filtering by scope type if needed in the future
- Maintains clear separation between different scope types

## Idempotent Write Strategy

### Approach: Lightweight Transactions (INSERT IF NOT EXISTS)

The repository uses lightweight transactions (LWT) for idempotent appends:

```sql
INSERT INTO chat_messages (
    scope_id, created_at_utc, message_id, sender_id, text,
    origin_pod_id, kafka_partition, kafka_offset
) VALUES (?, ?, ?, ?, ?, ?, ?, ?) IF NOT EXISTS;
```

### Tradeoffs Analysis

| Aspect | Lightweight Transactions (Current) | Regular INSERT + Dedupe |
|--------|-----------------------------------|-------------------------|
| **Correctness** | Strong guarantee, no duplicates | Potential duplicates on retry |
| **Latency** | Higher (~4 round trips) | Lower (~1 round trip) |
| **Throughput** | Lower (~5K ops/sec/node) | Higher (~50K+ ops/sec/node) |
| **Complexity** | Simple, database-level | Requires application dedupe |

### When to Use Alternative Strategy

If write throughput becomes a bottleneck, consider:

1. **Regular INSERT with client-side deduplication:**
   ```sql
   INSERT INTO chat_messages (...) VALUES (?, ?, ?, ...);
   ```
   - Track written MessageIds in Redis or memory
   - Check before writing (with TTL to prevent unbounded growth)

2. **Materialized View for uniqueness:**
   ```sql
   CREATE MATERIALIZED VIEW chat_messages_by_id AS
     SELECT message_id, scope_id, created_at_utc
     FROM chat_messages
     WHERE message_id IS NOT NULL AND scope_id IS NOT NULL AND created_at_utc IS NOT NULL
     PRIMARY KEY (message_id, scope_id, created_at_utc);
   ```
   - Query before insert to check existence
   - Higher write amplification (writes to base table + view)

3. **Accept eventual consistency:**
   - Use regular INSERT
   - Filter duplicates at query time
   - Accept brief periods where duplicates exist

## Query Patterns

### Get Messages by Scope (Most Recent)

```sql
SELECT * FROM chat_messages
WHERE scope_id = ?
ORDER BY created_at_utc ASC
LIMIT ?;
```

**Usage:** Fetch the N most recent messages in a scope.

**Performance:** Efficient single-partition query.

### Get Messages by Time Range

```sql
SELECT * FROM chat_messages
WHERE scope_id = ?
  AND created_at_utc >= ?  -- Optional lower bound
  AND created_at_utc < ?   -- Optional upper bound
ORDER BY created_at_utc ASC
LIMIT ?;
```

**Usage:** Fetch messages within a specific time window.

**Performance:** Efficient range scan on clustering key.

### Pagination Strategy

For large result sets, use one of these pagination approaches:

**1. Timestamp-based (Current Implementation):**
```csharp
// First page
var page1 = await repository.QueryByScopeAsync(
    scopeType, scopeId,
    fromUtc: null,
    toUtc: DateTime.UtcNow,
    limit: 100,
    ct);

// Next page (use last message's timestamp as fromUtc)
var lastTimestamp = page1.Last().CreatedAtUtc;
var page2 = await repository.QueryByScopeAsync(
    scopeType, scopeId,
    fromUtc: lastTimestamp.AddTicks(1),
    toUtc: DateTime.UtcNow,
    limit: 100,
    ct);
```

**2. Paging State (Future Enhancement):**
The Cassandra driver supports server-side paging state for stateless pagination. This requires extending the `IChatHistoryRepository` interface to return paging state tokens.

## Data Lifecycle

### TTL Strategy

For chat messages with retention requirements, consider adding TTL:

```sql
-- Auto-delete messages after 1 year
INSERT INTO chat_messages (...) VALUES (?, ?, ...)
USING TTL 31536000;
```

### Manual Cleanup

For compliance or retention policies, implement scheduled cleanup:

```sql
-- Delete messages older than a date
DELETE FROM chat_messages
WHERE scope_id = ?
  AND created_at_utc < ?;
```

## Performance Tuning

### Consistency Levels

| Operation | Consistency | Rationale |
|-----------|-------------|-----------|
| Writes | LOCAL_QUORUM | Balance consistency and latency |
| Queries | LOCAL_ONE | Fast reads for recent data |
| LWT | SERIAL | Required for lightweight transactions |

### Connection Pooling

The Cassandra driver manages connection pooling automatically. Configure in `AddScyllaChatify`:

```csharp
builder.WithPoolingOptions(new PoolingOptions()
    .SetCoreConnectionsPerHost(HostDistance.Local, 2)
    .SetMaxConnectionsPerHost(HostDistance.Local, 4)
    .SetCoreConnectionsPerHost(HostDistance.Remote, 1)
    .SetMaxConnectionsPerHost(HostDistance.Remote, 2));
```

### Query Optimization Tips

1. **Always provide partition key** - Queries without partition key scan all nodes
2. **Limit result sets** - Use LIMIT to prevent large memory allocations
3. **Use prepared statements** - Already implemented in the repository
4. **Avoid SELECT \*** - Select only needed columns (minor optimization)
5. **Monitor partition size** - Consider time-based bucketing for high-volume scopes

## Monitoring

### Key Metrics

- Write latency (p50, p95, p99)
- Query latency (p50, p95, p99)
- LWT contention rate
- Partition size distribution
- Compaction metrics

### Alerts

- High write latency (> 100ms p95)
- High LWT timeout rate
- Wide partitions (> 100M rows)
- Failed requests

## Migration Strategy

To deploy schema changes:

1. Test in non-production environment
2. Deploy to one node at a time in production
3. Monitor for errors and performance degradation
4. Have rollback plan ready

## Security

### Access Control

Create dedicated user for Chatify:

```sql
-- Create user
CREATE USER IF NOT EXISTS chatify_app WITH PASSWORD 'secure_password' NOSUPERUSER;

-- Grant permissions
GRANT ALL PERMISSIONS ON KEYSPACE chatify TO chatify_app;
```

### Encryption

- Enable SSL/TLS for client-to-node encryption in production
- Use authentication in all environments
- Rotate credentials regularly
