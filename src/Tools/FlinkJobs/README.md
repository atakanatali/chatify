# Chatify Flink Jobs

Apache Flink streaming jobs for Chatify real-time analytics and rate limiting.

## Overview

This module contains Flink streaming jobs that consume chat events from Kafka, perform real-time analytics aggregation and rate limit detection, and produce derived events to downstream Kafka topics.

## Processing Pipeline

```
chat-events (Kafka)
    ↓
ChatEventDeserializer
    ↓
[Fork]
├─→ Analytics Aggregation → analytics-events (Kafka)
└─→ Rate Limit Detection → rate-limit-events (Kafka)
```

## Features

### Analytics Aggregation
- Aggregates messages by composite scope ID (`scopeType:scopeId`)
- Computes tumbling window statistics (message count, active users, avg length)
- Produces one analytics event per window per scope
- Default window size: 60 seconds (configurable)

### Rate Limit Detection
- Aggregates messages by user ID across all scopes
- Computes sliding window statistics for high-frequency detection
- Produces rate limit events when thresholds are exceeded:
  - **Warning**: 80 messages/window
  - **Throttle**: 100 messages/window
  - **Flag**: 200 messages/window (for admin review)

## Build Requirements

- Java 11 or higher
- Maven 3.6 or higher
- Network access to Maven Central

## Building the JAR

```bash
# Navigate to the Flink jobs directory
cd src/Tools/FlinkJobs

# Build with Maven (creates shaded JAR with dependencies)
mvn clean package

# The output JAR will be at:
# target/chatify-flink-jobs-1.0.0.jar
```

## Running the Job

### Development Mode

```bash
# Run locally using Flink CLI
flink run -c com.chatify.flink.processor.ChatEventProcessorJob \
  target/chatify-flink-jobs-1.0.0.jar
```

### Kubernetes Deployment

See `deploy/k8s/flink/30-flink-job.yaml` for the Kubernetes job submission manifest.

### Configuration

The job is configured via environment variables:

| Variable | Description | Default |
|----------|-------------|---------|
| `KAFKA_BOOTSTRAP_SERVERS` | Kafka broker addresses | `chatify-kafka:9092` |
| `KAFKA_CONSUMER_GROUP_ID` | Consumer group ID | `chatify-flink-processor` |
| `KAFKA_SOURCE_TOPIC` | Source topic name | `chat-events` |
| `KAFKA_ANALYTICS_TOPIC` | Analytics sink topic | `analytics-events` |
| `KAFKA_RATE_LIMIT_TOPIC` | Rate limit sink topic | `rate-limit-events` |
| `CHECKPOINT_INTERVAL_MS` | Checkpoint interval (ms) | `60000` |
| `ANALYTICS_WINDOW_SIZE_SECONDS` | Analytics window size | `60` |
| `RATE_LIMIT_WINDOW_SIZE_SECONDS` | Rate limit window size | `60` |
| `RATE_LIMIT_WARNING_THRESHOLD` | Warning threshold | `80` |
| `RATE_LIMIT_THROTTLE_THRESHOLD` | Throttle threshold | `100` |
| `RATE_LIMIT_FLAG_THRESHOLD` | Flag threshold | `200` |
| `JOB_PARALLELISM` | Job parallelism | `2` |

## Event Schemas

### ChatEventEntity (Source)

```json
{
  "messageId": "550e8400-e29b-41d4-a716-446655440000",
  "scopeType": "Channel",
  "scopeId": "general",
  "senderId": "user-123",
  "text": "Hello, world!",
  "createdAtUtc": "2026-01-15T10:30:00Z",
  "originPodId": "chat-api-7d9f4c5b6d-abc12"
}
```

### AnalyticsEventEntity (Sink)

```json
{
  "analyticsId": "660e8400-e29b-41d4-a716-446655440000",
  "scopeType": "Channel",
  "scopeId": "general",
  "windowStartUtc": "2026-01-15T10:30:00Z",
  "windowEndUtc": "2026-01-15T10:31:00Z",
  "windowDurationSeconds": 60,
  "messageCount": 150,
  "activeUserCount": 25,
  "uniqueSenderCount": 25,
  "totalCharacterCount": 4500,
  "averageMessageLength": 30.0,
  "computedAtUtc": "2026-01-15T10:31:05Z",
  "computeTaskId": "chatify-flink-processor"
}
```

### RateLimitEventEntity (Sink)

```json
{
  "rateLimitEventId": "770e8400-e29b-41d4-a716-446655440000",
  "eventType": "Throttle",
  "userId": "user-123",
  "scopeType": "Channel",
  "scopeId": "general",
  "windowStartUtc": "2026-01-15T10:30:00Z",
  "windowEndUtc": "2026-01-15T10:31:00Z",
  "windowDurationSeconds": 60,
  "messageCount": 105,
  "threshold": 100,
  "excessCount": 5,
  "messagesPerSecond": 1.75,
  "detectedAtUtc": "2026-01-15T10:31:05Z",
  "detectorTaskId": "chatify-flink-processor",
  "context": "User exceeded throttle threshold"
}
```

## Architecture

### Package Structure

```
src/main/java/com/chatify/flink/
├── config/
│   └── FlinkJobConfiguration.java    # Environment-based configuration
├── model/
│   ├── ChatEventEntity.java          # Source event entity
│   ├── ChatScopeTypeEnum.java        # Scope type enumeration
│   ├── AnalyticsEventEntity.java     # Analytics event entity
│   └── RateLimitEventEntity.java     # Rate limit event entity
├── processor/
│   └── ChatEventProcessorJob.java    # Main Flink job entry point
├── serializer/
│   ├── ChatEventDeserializer.java    # Kafka source deserializer
│   ├── AnalyticsEventSerializer.java # Analytics sink serializer
│   └── RateLimitEventSerializer.java # Rate limit sink serializer
└── sink/                             # Custom sinks (future)
```

### Key Classes

- **`ChatEventProcessorJob`**: Main job class that orchestrates the streaming pipeline
- **`FlinkJobConfiguration`**: Centralized configuration from environment variables
- **`ChatEventDeserializer`**: Deserializes JSON from Kafka to ChatEventEntity
- **`AnalyticsAggregateFunction`**: Computes windowed analytics aggregations
- **`RateLimitAggregateFunction`**: Computes user message frequency for rate limiting

## TODO

This is a placeholder implementation with the following enhancements planned:

1. **State Backend**: Configure RocksDB with external checkpoint storage (HDFS/S3)
2. **Dead Letter Queue**: Route failed messages to a DLQ topic for analysis
3. **Metrics**: Expose Flink metrics for Prometheus/Grafana monitoring
4. **Dynamic Configuration**: Support runtime threshold updates via config topic
5. **Advanced Analytics**: Add word frequency, sentiment analysis, emoji detection
6. **Multi-Tenancy**: Support per-tenant rate limiting and analytics
7. **Backfill**: Support batch mode for historical analytics
8. **Testing**: Add unit tests and integration tests

## Troubleshooting

### Job Won't Start

1. Check Kafka connectivity: `kubectl exec -it deployment/chatify-flink-jobmanager -- nc -zv chatify-kafka 9092`
2. Verify topic exists: `kubectl exec -it deployment/chatify-kafka -- rpk topic list`
3. Check job logs: `kubectl logs -f deployment/chatify-flink-jobmanager`

### No Events Produced

1. Verify source topic has messages: `kubectl exec -it deployment/chatify-kafka -- rpk topic consume chat-events`
2. Check consumer group lag: `kubectl exec -it deployment/chatify-kafka -- rpk group describe chatify-flink-processor`
3. Review Flink job metrics in Web UI (port-forward required)

### High Latency

1. Increase job parallelism via `JOB_PARALLELISM`
2. Reduce window sizes for faster aggregation
3. Check for backpressure in Flink Web UI

## License

Part of the Chatify project. See project root for license information.
