# Chatify Architecture

## Table of Contents
- [Overview](#overview)
- [Solution Structure](#solution-structure)
- [Modules](#modules)
- [BuildingBlocks (Shared Kernel)](#buildingblocks-shared-kernel)
- [Cross-Cutting Concerns](#cross-cutting-concerns)
- [Infrastructure](#infrastructure)
- [Testing Strategy](#testing-strategy)
- [Operational Considerations](#operational-considerations)
- [Appendix](#appendix)

## Overview
Chatify follows a modular monolith architecture with Clean Architecture boundaries and SOLID principles. This document will be expanded as modules and services are implemented.

## Solution Structure
The Chatify solution is defined in `Chatify.sln` and follows a modular monolith layout:

- **Hosts**
  - `src/Hosts/Chatify.ChatApi`: API host that wires dependencies and exposes the HTTP surface area.
- **Shared Kernel**
  - `src/BuildingBlocks/Chatify.BuildingBlocks`: cross-cutting utilities, shared abstractions, and base types.
- **Modules**
  - `src/Modules/Chat/Chatify.Chat.Domain`: domain entities, value objects, and domain services.
  - `src/Modules/Chat/Chatify.Chat.Application`: use cases, commands/queries, and application orchestration.
  - `src/Modules/Chat/Chatify.Chat.Infrastructure`: persistence, messaging, and external integrations.
- **Tests**
  - `tests/Chatify.Chat.UnitTests`: unit tests for the chat module.
  - `tests/Chatify.ChatApi.IntegrationTests`: integration tests for the API host and module wiring.

## Modules
The Chat module is split using Clean Architecture boundaries. Dependency rules are enforced via project references:

- **Domain** depends only on `Chatify.BuildingBlocks` (when shared kernel utilities are required).
- **Application** depends on **Domain** and **BuildingBlocks**.
- **Infrastructure** depends on **Application**, **Domain**, and **BuildingBlocks**.
- **Host (ChatApi)** depends on **Application**, **Infrastructure**, and **BuildingBlocks**.

This structure keeps domain logic isolated, pushes orchestration to the application layer, and encapsulates infrastructure concerns behind the application boundary.

## BuildingBlocks (Shared Kernel)
The `Chatify.BuildingBlocks` project provides foundational types and utilities used across all modules. It contains no business logic but implements essential cross-cutting concerns that support the application architecture.

### Primitives

#### Clock Abstraction (`IClockService`, `SystemClockService`)
- **Purpose**: Provides a time abstraction for deterministic testing of time-dependent operations.
- **Usage**: Inject `IClockService` where current time is needed. Use `SystemClockService` in production; mock for testing.
- **Location**: `Chatify.BuildingBlocks.Primitives.IClockService`

#### Guard Clauses (`GuardUtility`)
- **Purpose**: Centralizes argument validation with consistent error messages.
- **Methods**:
  - `NotNull(T value)` - Validates reference types are not null
  - `NotEmpty(string value)` - Validates strings are not null or empty
  - `NotEmpty(IEnumerable<T> value)` - Validates collections are not null or empty
  - `InRange(int value, int min, int max)` - Validates numeric ranges (overloads for int, decimal, double, DateTime)
- **Usage**: Call at start of methods to validate preconditions.
- **Location**: `Chatify.BuildingBlocks.Primitives.GuardUtility`

#### Error Handling (`ErrorEntity`, `ResultEntity`)
- **Purpose**: Encapsulates operation results without relying on exceptions for control flow.
- **Types**:
  - `ErrorEntity` - Structured error with Code, Message, Details
  - `ResultEntity` - Generic result wrapper with Success/Failure states
  - `ResultEntity<T>` - Result wrapper that returns a value on success
- **Usage**: Return from methods that can fail instead of throwing exceptions for expected errors.
- **Location**: `Chatify.BuildingBlocks.Primitives.ErrorEntity`, `Chatify.BuildingBlocks.Primitives.ResultEntity`

#### Correlation Context (`ICorrelationContextAccessor`, `CorrelationContextAccessor`, `CorrelationIdUtility`)
- **Purpose**: Manages correlation IDs for distributed tracing across async execution contexts.
- **Components**:
  - `ICorrelationContextAccessor` - Interface for accessing correlation ID
  - `CorrelationContextAccessor` - Implementation using `AsyncLocal<string?>` for async context isolation
  - `CorrelationIdUtility` - Generates and validates correlation IDs (format: `corr_{guid}`)
- **Usage**: Register `CorrelationContextAccessor` as singleton. Middleware extracts/generates ID; services read from accessor.
- **Location**: `Chatify.BuildingBlocks.Primitives.ICorrelationContextAccessor`, `Chatify.BuildingBlocks.Primitives.CorrelationContextAccessor`, `Chatify.BuildingBlocks.Primitives.CorrelationIdUtility`

### Naming Conventions
All types in BuildingBlocks use postfixes for clarity:
- Interfaces: `*Service`, `*Accessor`, `*Repository`
- Entities: `*Entity`
- Data Transfer Objects: `*Dto`
- Options: `*OptionsEntity`
- Commands/Queries: `*Command`, `*Query`
- Handlers: `*Handler`

### Documentation Standards
Every public type and member in BuildingBlocks requires detailed XML documentation comments:
- `<summary>` elements describe purpose and behavior
- `<remarks>` elements provide usage examples and important notes
- `<param>` elements document all parameters
- `<returns>` elements describe return values
- `<exception>` elements document thrown exceptions
- `<value>` elements describe properties
- Private methods also include XML comments for maintainability

## Cross-Cutting Concerns
Chatify centralizes foundational dependencies in `Directory.Packages.props` to ensure consistent package versions across modules. The baseline stack includes:

- **Real-time messaging**: SignalR (`Microsoft.AspNetCore.SignalR`, JSON protocol) for websocket-based chat hubs.
- **Logging/observability**: Serilog with console and Elastic sinks plus enrichers for environment/process/thread/span context.
- **Resilience & configuration**: `Polly` for retries and `Microsoft.Extensions.Options.ConfigurationExtensions` for typed settings.
- **Validation**: `FluentValidation` for request and domain rules.

## Infrastructure

### Overview
The infrastructure layer implements the ports defined by the Application layer, providing concrete integrations with external services and data stores. Following Clean Architecture principles, infrastructure code is isolated behind well-defined interfaces, allowing the application logic to remain decoupled from specific implementation technologies.

### Provider Integrations

#### Message Broker (Event Streaming)
**Purpose:** Produces and consumes chat events for asynchronous messaging and fan-out delivery.

**Options:** `KafkaOptionsEntity`
- `BootstrapServers` - Comma-separated list of broker addresses
- `TopicName` - Topic for chat events
- `Partitions` - Number of topic partitions
- `BroadcastConsumerGroupPrefix` - Prefix for broadcast consumer groups

**DI Extension:** `ServiceCollectionMessageBrokerExtensions.AddMessageBroker(IConfiguration)`

**Registered Services:**
- `KafkaOptionsEntity` (singleton) - Configuration options
- `ChatEventProducerService` (singleton) - Implements `IChatEventProducerService`
- Future: Background consumers for broadcast delivery

**Implementation Status:** Producer implemented using Confluent.Kafka v2.5.3

**Configuration Section:** `Chatify:MessageBroker`

```json
{
  "Chatify": {
    "MessageBroker": {
      "BootstrapServers": "localhost:9092",
      "TopicName": "chat-events",
      "Partitions": 3,
      "BroadcastConsumerGroupPrefix": "chatify-broadcast"
    }
  }
}
```

**Producer Semantics:**
- **Client Library:** Confluent.Kafka v2.5.3 (managed via `Directory.Packages.props`)
- **Message Key:** `ScopeId` (string) - Used for partitioning to ensure ordering within scopes
- **Message Value:** JSON-serialized `ChatEventDto` using `System.Text.Json` with UTF-8 encoding
- **Acknowledgment:** `acks=all` - Waits for all in-sync replicas to acknowledge for durability
- **Idempotence:** `enable.idempotence=true` - Prevents duplicate messages on retry
- **Retries:** `MessageSendMaxRetries=INT_MAX` - Retries indefinitely on transient failures
- **Compression:** `compression.type=snappy` - Efficient network compression
- **Batching:** `linger.ms=5` with 1MB max batch size for improved throughput
- **Return Value:** `ProduceAsync` returns `(int Partition, long Offset)` for delivery tracking

**Partitioning Strategy:** Events are partitioned by `ScopeId` (message key) to ensure ordering within each chat scope. The message broker hashes the key to determine the target partition. All messages for the same scope are routed to the same partition, maintaining strict ordering while allowing parallel processing across different scopes.

**Command Handler Integration:**
- `SendChatMessageCommandHandler` calls `_chatEventProducerService.ProduceAsync()`
- Returns `EnrichedChatEventDto` containing the original event plus partition/offset metadata
- Message broker exceptions (`KafkaException`, `OperationCanceledException`) are caught and returned as `ServiceError.Messaging.EventProductionFailed`

**Graceful Shutdown:** Producer implements `IDisposable`; the DI container disposes it on application shutdown, flushing any pending messages.

#### Fan-Out Consumer (Broadcast Delivery)
**Purpose:** Consumes chat events from Kafka and broadcasts them to connected SignalR clients in real-time. Each pod runs its own consumer to deliver messages locally.

**Implementation:** `ChatBroadcastBackgroundService` (located in `src/Hosts/Chatify.Api/BackgroundServices/`)

**Consumer Configuration:**
- **Group ID:** Unique per pod: `{BroadcastConsumerGroupPrefix}-{PodId}`
  - Example: `chatify-broadcast-chat-api-7d9f4c5b6d-abc12`
- **Auto Offset Reset:** `earliest` - New consumers start from the beginning of the topic
- **Auto Commit:** `true` with 5-second interval for at-least-once delivery
- **Fetch Settings:** Low latency (1 byte min, 100ms max wait) for real-time delivery

**Fan-Out Architecture:**
```
┌─────────────────────────────────────────────────────────────────────────────┐
│                     Fan-Out Broadcast Architecture                           │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  Kafka Topic: "chat-events" (3 partitions)                                 │
│     ├─ Partition 0 ─────────────────────────────────────────────┐           │
│     ├─ Partition 1 ──┐                                          │           │
│     └─ Partition 2 ──┼──────────────────────────────────────────┤           │
│                     │          │            │                    │           │
│                     │          │            │                    │           │
│                     ▼          ▼            ▼                    │           │
│              ┌─────────────────────────────────────────┐         │           │
│              │         Each partition is                │         │           │
│              │    consumed by ALL pods independently    │         │           │
│              └─────────────────────────────────────────┘         │           │
│                     │          │            │                    │           │
│       ┌───────────┴──────┐  ┌─┴──────────┐  ┌─┴──────────┐     │           │
│       │                  │  │            │  │            │     │           │
│       ▼                  ▼  ▼            ▼  ▼            ▼     │           │
│  ┌─────────┐        ┌─────────┐    ┌─────────┐    ┌─────────┐   │           │
│  │ Pod A   │        │ Pod B   │    │ Pod C   │    │ Pod N   │   │           │
│  │ Group:  │        │ Group:  │    │ Group:  │    │ Group:  │   │           │
│  │ chatify-│        │ chatify-│    │ chatify-│    │ chatify-│   │           │
│  │ broadcast│       │ broadcast│    │ broadcast│    │ broadcast│   │           │
│  │ -pod-a  │        │ -pod-b  │    │ -pod-c  │    │ -pod-n  │   │           │
│  └────┬────┘        └────┬────┘    └────┬────┘    └────┬────┘   │           │
│       │                  │            │            │         │           │
│       │ Broadcast locally│            │            │         │           │
│       ▼                  ▼            ▼            ▼         │           │
│  ┌─────────┐        ┌─────────┐    ┌─────────┐    ┌─────────┐   │           │
│  │SignalR  │        │SignalR  │    │SignalR  │    │SignalR  │   │           │
│  │Clients  │        │Clients  │    │Clients  │    │Clients  │   │           │
│  │connected│        │connected│    │connected│    │connected│   │           │
│  │to Pod A │        │to Pod B │    │to Pod C │    │to Pod N │   │           │
│  └─────────┘        └─────────┘    └─────────┘    └─────────┘   │           │
│                                                                 │           │
└─────────────────────────────────────────────────────────────────┘           │
                                                                             │
│  Key Characteristics:                                                      │
│  - Each pod has a UNIQUE consumer group ID                               │
│  - Each pod receives ALL messages from ALL partitions                     │
│  - Messages are broadcast to local SignalR clients only                   │
│  - Clients connect to any pod and receive all messages for their scopes   │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

**Ordering Guarantees:**
- **Within a ScopeId (Partition):** Messages are delivered in strict order by offset
- **Across ScopeIds:** No ordering guarantee - different scopes can be processed in parallel
- **At-Least-Once Delivery:** Duplicate delivery is possible; clients should dedupe by MessageId

**Client Deduplication Example:**
```javascript
// JavaScript/TypeScript SignalR client
const seenMessages = new Set();

connection.on("ReceiveMessage", (event) => {
    // Skip if we've already processed this message
    if (seenMessages.has(event.messageId)) {
        console.log(`Duplicate message skipped: ${event.messageId}`);
        return;
    }

    // Mark as seen
    seenMessages.add(event.messageId);

    // Optionally prune old entries to prevent memory bloat
    if (seenMessages.size > 10000) {
        const oldest = seenMessages.keys().next().value;
        seenMessages.delete(oldest);
    }

    // Process the message...
    displayMessage(event);
});
```

**Error Handling:**
- Consume exceptions are logged to Elasticsearch with full context (partition, offset, error)
- Service continues with exponential backoff: 1s, 2s, 4s, 8s, 16s (max)
- Initial connection failures trigger retry with backoff
- Deserialization errors log the offending message payload for debugging
- Graceful shutdown commits final offsets and closes consumer properly

**Background Service Registration:**
```csharp
// In Program.cs (Chatify.Api)
builder.Services.AddHostedService<BackgroundServices.ChatBroadcastBackgroundService>();
```

---

#### History Writer Consumer (Persistence)
**Purpose:** Consumes chat events from Kafka and persists them to ScyllaDB for durable chat history storage. Multiple pods share the workload through a shared consumer group.

**Architecture:** The history writer follows SOLID principles with clear separation of concerns:
- **`ChatHistoryWriterBackgroundService`** (Host layer) - Orchestrates message consumption and coordinates components
- **`IKafkaConsumerFactory`** (Infrastructure) - Creates and configures Kafka consumers
- **`IChatEventProcessor`** (Infrastructure) - Handles deserialization, validation, and persistence with retry logic
- **`ExponentialBackoff`** (BuildingBlocks) - Provides backoff calculations for error scenarios

**Implementation Locations:**
- Background Service: `src/Hosts/Chatify.Api/BackgroundServices/ChatHistoryWriterBackgroundService.cs`
- Factory: `src/Modules/Chat/Chatify.Chat.Infrastructure/Services/KafkaConsumers/`
- Processor: `src/Modules/Chat/Chatify.Chat.Infrastructure/Services/ChatHistory/ChatEventProcessing/`

**Consumer Configuration:**
- **Group ID:** Configured via `Chatify.ChatHistoryWriter.ConsumerGroupId` (default: `chatify-chat-history-writer`)
- **Auto Offset Reset:** `earliest` - New consumers start from the beginning of the topic
- **Auto Commit:** `false` - Manual commit after successful persistence for at-least-once delivery
- **Fetch Settings:** Low latency (1 byte min, 100ms max wait) for real-time processing

**Shared Consumer Group Architecture:**
```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    Shared Consumer Group Architecture                       │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  Kafka Topic: "chat-events" (3 partitions)                                 │
│     ├─ Partition 0 ─────────────────────────────────────────────┐           │
│     ├─ Partition 1 ──┐                                          │           │
│     └─ Partition 2 ──┼──────────────────────────────────────────┤           │
│                     │          │            │                    │           │
│                     │          │            │                    │           │
│                     ▼          ▼            ▼                    │           │
│              ┌─────────────────────────────────────────┐         │           │
│              │      Consumer Group:                     │         │           │
│              │      "chatify-chat-history-writer"       │         │           │
│              │                                         │         │           │
│              │  Partitions are distributed among        │         │           │
│              │  active consumers via rebalancing        │         │           │
│              └─────────────────────────────────────────┘         │           │
│                     │          │            │                    │           │
│       ┌───────────┴──────┐  ┌─┴──────────┐  ┌─┴──────────┐     │           │
│       │                  │  │            │  │            │     │           │
│       ▼                  ▼  ▼            ▼  ▼            ▼     │           │
│  ┌─────────┐        ┌─────────┐    ┌─────────┐    ┌─────────┐   │           │
│  │ Pod A   │        │ Pod B   │    │ Pod C   │    │ Pod N   │   │           │
│  │ Owns:   │        │ Owns:   │    │ Owns:   │    │ Owns:   │   │           │
│  │ Part 0  │        │ Part 1  │    │ Part 2  │    │ (standby)│   │           │
│  └────┬────┘        └────┬────┘    └────┬────┘    └────┬────┘   │           │
│       │                  │            │            │         │           │
│       │ Write to        │ Write to   │ Write to   │         │           │
│       ▼                  ▼            ▼            ▼         │           │
│  ┌─────────┐        ┌─────────┐    ┌─────────┐    ┌─────────┐   │           │
│  │ScyllaDB │        │ScyllaDB │    │ScyllaDB │    │ScyllaDB │   │           │
│  │(Idempot-│        │(Idempot- │    │(Idempot-│    │(Idempot-│   │           │
│  │ ent)    │        │ ent)     │    │ ent)    │    │ ent)    │   │           │
│  └─────────┘        └─────────┘    └─────────┘    └─────────┘   │           │
│                                                                 │           │
│  Key Characteristics:                                              │           │
│  - All pods use the SAME consumer group ID                       │           │
│  - Each partition is owned by ONE consumer at a time             │           │
│  - Workload is automatically distributed via rebalancing         │           │
│  - Adding pods increases throughput (up to partition count)       │           │
│  - Idempotent writes prevent duplicates on retry                 │           │
│                                                                             │
└─────────────────────────────────────────────────────────────────┘           │
                                                                             │
```

**Processing Pipeline (Refactored Architecture):**
```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    Message Persistence Pipeline (SOLID)                     │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  KAFKA                                                                      │
│     ┌─────────────────────────────────────────────────────────────────┐    │
│     │  Partition: 0, Offset: 1234                                     │    │
│     │  Key: "general" | Value: {ChatEventDto JSON}                    │    │
│     └─────────────────────────────────────────────────────────────────┘    │
│                              │                                              │
│                              ▼                                              │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │          ChatHistoryWriterBackgroundService (Orchestrator)          │   │
│  │  ─────────────────────────────────────────────────────────────────  │   │
│  │  Responsibilities:                                                   │   │
│  │  - Consume messages from Kafka                                      │   │
│  │  - Delegate processing to IChatEventProcessor                       │   │
│  │  - Commit offsets based on ProcessResultEntity                      │   │
│  │  - Apply backoff on consumer errors                                 │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                              │                                              │
│                              ▼                                              │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │              IChatEventProcessor (Business Logic)                   │   │
│  │  ─────────────────────────────────────────────────────────────────  │   │
│  │  Responsibilities:                                                   │   │
│  │  - Deserialize JSON to ChatEventDto                                 │   │
│  │  - Validate message structure                                       │   │
│  │  - Apply Polly retry for transient DB errors                        │   │
│  │  - Return ProcessResultEntity (Success/PermanentFailure)            │   │
│  │                                                                     │   │
│  │  Error Handling:                                                    │   │
│  │  - Permanent errors (deserialize, validation) → Return PermanentFailure│   │
│  │  - Transient errors (DB timeout, network) → Retry via Polly, then throw│   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                              │                                              │
│                              ▼                                              │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │                    ChatHistoryRepository                            │   │
│  │  ─────────────────────────────────────────────────────────────────  │   │
│  │  INSERT INTO chat_messages (...) VALUES (?, ?, ...)                 │   │
│  │  IF NOT EXISTS;  // Lightweight Transaction (Idempotent)            │   │
│  │                                                                      │   │
│  │  Returns: [applied] = true (inserted) or false (duplicate)          │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                              │                                              │
│                              ▼                                              │
│  SCYLLADB                                                                    │
│     ┌─────────────────────────────────────────────────────────────────┐    │
│     │  Table: chat_messages                                           │    │
│     │  PK: (scope_id, created_at_utc, message_id)                     │    │
│     └─────────────────────────────────────────────────────────────────┘    │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

**Commit Strategy (Refactored):**
| Scenario | Action | Rationale |
|----------|--------|-----------|
| **Success** | Commit offset | Message successfully processed |
| **Permanent Failure** | Commit offset | Prevent poison message replay (DLQ in future) |
| **Transient Error** | Do NOT commit | Message will be redelivered for retry |

**Configuration Options:** `Chatify.ChatHistoryWriter`
```json
{
  "Chatify": {
    "ChatHistoryWriter": {
      "ConsumerGroupId": "chatify-chat-history-writer",
      "DatabaseRetryMaxAttempts": 5,
      "DatabaseRetryBaseDelayMs": 100,
      "DatabaseRetryMaxDelayMs": 10000,
      "DatabaseRetryJitterMs": 100,
      "ConsumerBackoffInitialMs": 1000,
      "ConsumerBackoffMaxMs": 16000,
      "MaxPayloadLogBytes": 256
    }
  }
}
```

**DI Extension:** `ServiceCollectionChatHistoryWriterExtensions.AddChatHistoryWriter(IConfiguration)`

**Registered Services:**
- `ChatHistoryWriterOptionsEntity` (singleton) - Configuration options
- `IKafkaConsumerFactory` → `KafkaConsumerFactory` (singleton) - Creates Kafka consumers
- `IChatEventProcessor` → `ChatEventProcessor` (singleton) - Processes messages with retry

**Idempotency Strategy:**
- The repository uses lightweight transactions (LWT): `INSERT IF NOT EXISTS`
- When a message is retried, the database silently ignores the duplicate
- The `[applied]` flag indicates whether the insert was new or skipped
- This ensures exactly-once semantics even with at-least-once delivery

**Retry Strategy (Polly in ChatEventProcessor):**
- **Transient Errors:** Network issues, timeouts, temporary unavailability
- **Policy:** Exponential backoff with jitter (100ms base → 10s max)
- **Max Attempts:** Configurable (default: 5)
- **Jitter:** Configurable (default: 0-100ms) to prevent thundering herd
- **On Exhausted Retries:** Exception thrown; consumer applies backoff and retries

**Poison Message Handling:**
- Deserialization/validation errors return `ProcessResultEntity.PermanentFailure`
- Background service commits offset to skip the message
- Future enhancement: Write to dead-letter queue (DLQ) topic for analysis

**Ordering Guarantees:**
- **Within a ScopeId (Partition):** Messages are processed in strict order by offset
- **Across ScopeIds:** No ordering guarantee - different scopes can be processed in parallel
- **At-Least-Once Delivery:** Duplicate processing possible; idempotency prevents data corruption

**Error Handling:**
- **Consume exceptions:** Logged to Elasticsearch, service continues with exponential backoff
- **Permanent failures:** Logged, offset committed to prevent infinite replay
- **Transient failures:** Retried by processor; on exhaustion, consumer applies backoff
- **Commit exceptions:** Logged but do not prevent consumption (uncommitted offset causes retry)
- **Initial connection failures:** Trigger retry with backoff, eventually let Kubernetes restart
- **Graceful shutdown:** Commits final offsets and closes consumer properly

**Background Service Registration:**
```csharp
// In Program.cs (Chatify.Api)
builder.Services.AddChatHistoryWriter(Configuration); // Register factory, processor, options
builder.Services.AddHostedService<ChatHistoryWriterBackgroundService>();
```

---

#### Redis (Presence, Rate Limiting, Caching)
**Purpose:** Manages user presence, enforces rate limits, and provides distributed caching.

**Options:** `RedisOptionsEntity`
- `ConnectionString` - Redis connection string

**DI Extension:** `ServiceCollectionCachingExtensions.AddRedisChatify(IConfiguration)` (alias: `AddCaching`)

**Registered Services:**
- `RedisOptionsEntity` (singleton) - Configuration options
- `IConnectionMultiplexer` (singleton) - Redis connection multiplexer, disposed on shutdown
- `PresenceService` (singleton) - Implements `IPresenceService` with Redis backend
- `RateLimitService` (singleton) - Implements `IRateLimitService` (placeholder)
- `PodIdentityService` (singleton) - Identifies the current pod instance

**Implementation Status:** Presence tracking implemented with full Redis operations

**Configuration Section:** `Chatify:Caching`

```json
{
  "Chatify": {
    "Caching": {
      "ConnectionString": "localhost:6379"
    }
  }
}
```

**Redis Key Semantics:**

| Key Pattern | Type | Value | TTL | Purpose |
|-------------|------|-------|-----|---------|
| `presence:user:{userId}` | Sorted Set | `{podId}:{connectionId}` with score = timestamp | 60 seconds | Tracks all active connections for a user |
| `route:{userId}:{connectionId}` | String | `podId` | 60 seconds | O(1) lookup for pod routing |

**Presence Data Structure Details:**

```
# User "user123" connected on "pod-a" with connection "conn1"
ZADD presence:user:user123 1704110400 "pod-a:conn1"
EXPIRE presence:user:user123 60

# Set routing key for pod lookup
SETEX route:user123:conn1 60 "pod-a"

# User "user123" connects again on "pod-b" with connection "conn2"
ZADD presence:user:user123 1704110460 "pod-b:conn2"
EXPIRE presence:user:user123 60

# Set routing key for second connection
SETEX route:user123:conn2 60 "pod-b"

# User "user123" now has 2 connections
ZRANGE presence:user:user123 0 -1
# Results: ["pod-a:conn1", "pod-b:conn2"]

# Check if user is online (has any connections)
EXISTS presence:user:user123
# Result: 1 (online)

# Get all connection IDs for a user
ZRANGE presence:user:user123 0 -1
# Parse results to extract connection IDs

# User disconnects from "pod-a"
ZREM presence:user:user123 "pod-a:conn1"
DEL route:user123:conn1

# Check remaining connections
ZCARD presence:user:user123
# Result: 1 (still online)

# Last connection disconnects
ZREM presence:user:user123 "pod-b:conn2"
DEL route:user123:conn2

# Clean up empty presence set
DEL presence:user:user123
```

**TTL Strategy:**
- Presence keys expire after 60 seconds of inactivity
- TTL is refreshed on every heartbeat (client ping)
- If a client disconnects abruptly (network failure), the key expires automatically
- Graceful disconnects remove the connection immediately via `SetOfflineAsync`

**Multi-Pod Routing:**
```
┌─────────────────────────────────────────────────────────────────┐
│                    Multi-Pod Presence Routing                   │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  Redis (Centralized Presence Store)                             │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │ presence:user:alice = {                                  │   │
│  │   "pod-1:conn-abc123",  (score: 1704110400)              │   │
│  │   "pod-2:conn-def456"   (score: 1704110460)              │   │
│  │ }                                                         │   │
│  │ TTL: 60 seconds (refreshed by heartbeat)                 │   │
│  ├─────────────────────────────────────────────────────────┤   │
│  │ route:alice:conn-abc123 = "pod-1"                        │   │
│  │ route:alice:conn-def456 = "pod-2"                        │   │
│  └─────────────────────────────────────────────────────────┘   │
│                                                                 │
│       │                    │                    │               │
│       │ GET route:...      │                    │               │
│       ▼                    ▼                    ▼               │
│  ┌─────────┐        ┌─────────┐        ┌─────────┐             │
│  │ Pod-1   │        │ Pod-2   │        │ Pod-3   │             │
│  │         │        │         │        │         │             │
│  │ conn-   │        │ conn-   │        │ (query  │             │
│  │ abc123  │        │ def456  │        │  routes) │             │
│  └─────────┘        └─────────┘        └─────────┘             │
│                                                                 │
│  To route message to Alice:                                     │
│  1. ZRANGE presence:user:alice 0 -1  (get all connections)     │
│  2. For each connection:                                        │
│     - GET route:alice:{connectionId}  (get pod ID)             │
│     - Route message to that pod                                │
│  3. Pod delivers via SignalR to local connection               │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

**Hub Integration:**
- `OnConnectedAsync`: Calls `SetOnlineAsync` to register presence
- `OnDisconnectedAsync`: Calls `SetOfflineAsync` to remove presence
- Heartbeat: Optional method to refresh TTL for long-lived connections

#### Rate Limiting (Endpoint-Level)
**Purpose:** Enforces rate limits per user per endpoint using Redis with fixed window counter algorithm.

**Implementation:** `RateLimitService` (located in `src/Modules/Chat/Chatify.Chat.Infrastructure/Services/RateLimit/`)

**Algorithm:** Fixed Window Counter with Atomic Lua Script
- **Key Pattern:** `rl:{userId}:{endpoint}:{window}`
  - Example: `rl:user123:SendMessage:60`
  - Window duration is included in the key to prevent configuration change conflicts
- **Data Structure:** Redis string counter with TTL
- **Atomic Operation:** Lua script performs GET, INCR, EXPIRE atomically
- **Window Management:** TTL automatically expires old counters

**Lua Script:**
```lua
local current = redis.call('GET', KEYS[1])
if current == false then
    current = 0
else
    current = tonumber(current)
end
if current < tonumber(ARGV[1]) then
    redis.call('INCR', KEYS[1])
    redis.call('EXPIRE', KEYS[1], ARGV[2])
    return 1  -- Allowed
else
    return 0  -- Blocked
end
```

**Redis Key Semantics:**

| Key Pattern | Type | Value | TTL | Purpose |
|-------------|------|-------|-----|---------|
| `rl:{userId}:{endpoint}:{window}` | String | Counter (incrementing) | window seconds | Tracks request count for endpoint |

**Example Operations:**
```
# Check and increment rate limit for user123 sending messages
# Key: rl:user123:SendMessage:60
# Threshold: 100
# Window: 60 seconds

EVAL "<lua-script>" 1 rl:user123:SendMessage:60 100 60
# Returns: 1 (allowed) or 0 (blocked)

# If allowed, counter is incremented and TTL is set:
GET rl:user123:SendMessage:60
# Result: "1" (first request)
TTL rl:user123:SendMessage:60
# Result: 60 (seconds until expiration)

# After 100 requests within the window:
EVAL "<lua-script>" 1 rl:user123:SendMessage:60 100 60
# Returns: 0 (blocked)

# After 60 seconds, key expires automatically
# Next request starts a fresh window
```

**Integration in SendChatMessageCommandHandler:**
```csharp
var rateLimitKey = ChatifyConstants.RateLimit.SendMessageRateLimitKey(senderId);
var rateLimitResult = await _rateLimitService.CheckAndIncrementAsync(
    rateLimitKey,
    ChatifyConstants.RateLimit.SendChatMessageThreshold,
    ChatifyConstants.RateLimit.SendChatMessageWindowSeconds,
    cancellationToken);

if (rateLimitResult.IsFailure)
{
    _logger.LogWarning("Rate limit exceeded for sender {SenderId}", senderId);
    return ResultEntity<EnrichedChatEventDto>.Failure(ServiceError.Chat.RateLimitExceeded(rateLimitKey, null));
}
```

**Logging:**
- **Debug:** Logs every rate limit check with key, threshold, and window
- **Warning:** Logs when rate limit is exceeded
- **Error:** Logs Redis connection failures

**Error Handling:**
- Returns `ResultEntity.Success()` when request is allowed
- Returns `ResultEntity.Failure(ErrorEntity)` when:
  - Rate limit exceeded (warning log)
  - Redis connection error (error log, returns configuration error)

**Fixed Window Characteristics:**
- Simple and efficient with O(1) complexity
- Counters reset at fixed time boundaries
- TTL ensures automatic cleanup of expired windows
- Allows bursts at window boundaries (e.g., 100 requests at 12:00:59, then 100 more at 12:01:00)

**Distributed Guarantees:**
- All pods see the same counter values via Redis
- Atomic Lua script prevents race conditions
- No single point of failure with Redis cluster

---

#### ScyllaDB (Message Persistence)
**Purpose:** Durable storage for chat message history with time-series query optimization.

**Options:** `ScyllaOptionsEntity`
- `ContactPoints` - Comma-separated list of node addresses (default port: 9042)
- `Keyspace` - Keyspace name (default: "chatify")
- `Username` - Authentication username (optional)
- `Password` - Authentication password (optional)

**DI Extension:** `ServiceCollectionScyllaExtensions.AddScyllaChatify(IConfiguration)`

**Registered Services:**
- `ScyllaOptionsEntity` (singleton) - Configuration options
- `ICluster` (singleton) - Cassandra/ScyllaDB cluster connection
- `ISession` (singleton) - Cassandra/ScyllaDB session to keyspace
- `ChatHistoryRepository` (singleton) - Implements `IChatHistoryRepository`

**Implementation Status:** Fully implemented with idempotent writes and pagination support.

**Configuration Sections:** `Chatify:Scylla` (preferred) or `Chatify:Database` (backward compatibility)

```json
{
  "Chatify": {
    "Scylla": {
      "ContactPoints": "scylla-node1:9042,scylla-node2:9042,scylla-node3:9042",
      "Keyspace": "chatify",
      "Username": "chatify_user",
      "Password": "secure_password"
    }
  }
}
```

**Keyspace Creation:**
```sql
-- Production (multi-datacenter)
CREATE KEYSPACE IF NOT EXISTS chatify
WITH REPLICATION = {
  'class': 'NetworkTopologyStrategy',
  'dc1': 3,
  'dc2': 3
} AND DURABLE_WRITES = true;

-- Development (single datacenter)
CREATE KEYSPACE IF NOT EXISTS chatify
WITH REPLICATION = {
  'class': 'SimpleStrategy',
  'replication_factor': 1
};
```

**Table Schema:**
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
};
```

**Schema Design:**

| Column | Type | Description |
|--------|------|-------------|
| `scope_id` | `text` | Composite key: `"scope_type:scope_id"` (partition key) |
| `created_at_utc` | `timestamp` | Message creation time (clustering key, ASC) |
| `message_id` | `uuid` | Unique message identifier (clustering column, uniqueness) |
| `sender_id` | `text` | User ID who sent the message |
| `text` | `text` | Message content |
| `origin_pod_id` | `text` | Pod that created the message |
| `broker_partition` | `int` | Message broker partition (null when written from API) |
| `broker_offset` | `bigint` | Message broker offset (null when written from API) |

**Composite scope_id Format:**
```
"{ScopeType}:{ScopeId}"

Examples:
- "Channel:general"
- "Channel:random"
- "DirectMessage:user1-user2"
```

**Primary Key Design:**
- **Partition Key:** `(scope_id)` - Groups all messages for a scope on same nodes
- **Clustering Keys:**
  - `created_at_utc ASC` - Chronological ordering for time-range queries
  - `message_id` - Uniqueness guard and tiebreaker for identical timestamps

**Idempotent Write Strategy:**

The repository uses lightweight transactions (LWT) for idempotent appends:

```sql
INSERT INTO chat_messages (...) VALUES (?, ?, ...) IF NOT EXISTS;
```

**Tradeoffs:**

| Aspect | Lightweight Transactions (Current) | Regular INSERT + Dedupe |
|--------|-----------------------------------|-------------------------|
| **Correctness** | Strong guarantee, no duplicates | Potential duplicates on retry |
| **Latency** | Higher (~4 round trips) | Lower (~1 round trip) |
| **Throughput** | Lower (~5K ops/sec/node) | Higher (~50K+ ops/sec/node) |

**When to Use Alternative:** If write throughput becomes a bottleneck, consider:
- Regular INSERT with client-side deduplication (track MessageIds in Redis)
- Materialized View for uniqueness check
- Accept eventual consistency and filter at query time

**Query Patterns:**

1. **Get Most Recent Messages:**
   ```sql
   SELECT * FROM chat_messages
   WHERE scope_id = ?
   ORDER BY created_at_utc ASC
   LIMIT ?;
   ```

2. **Get Messages by Time Range:**
   ```sql
   SELECT * FROM chat_messages
   WHERE scope_id = ?
     AND created_at_utc >= ?
     AND created_at_utc < ?
   ORDER BY created_at_utc ASC
   LIMIT ?;
   ```

**Pagination Strategy:**

Timestamp-based pagination (current implementation):
```csharp
// First page
var page1 = await repository.QueryByScopeAsync(
    scopeType, scopeId,
    fromUtc: null,
    toUtc: DateTime.UtcNow,
    limit: 100,
    ct);

// Next page (use last message's timestamp + 1 tick)
var lastTimestamp = page1.Last().CreatedAtUtc;
var page2 = await repository.QueryByScopeAsync(
    scopeType, scopeId,
    fromUtc: lastTimestamp.AddTicks(1),
    toUtc: DateTime.UtcNow,
    limit: 100,
    ct);
```

**Consistency Levels:**
- **Writes:** LOCAL_QUORUM - Balance consistency and latency
- **Queries:** LOCAL_ONE - Fast reads for recent data
- **LWT:** SERIAL - Required for lightweight transactions

**Prepared Statements:** All CQL statements are prepared once at startup and reused for optimal performance.

**Session Management:** The session is created as a singleton and disposed on application shutdown. Connection pooling is managed automatically by the Cassandra driver.

---

#### Elasticsearch (Logging)
**Purpose:** Centralized log aggregation and analysis via Serilog.

**Options:** `ElasticOptionsEntity`
- `Uri` - Elasticsearch cluster endpoint
- `Username` - Authentication username (optional)
- `Password` - Authentication password (optional)
- `IndexPrefix` - Prefix for log indices (default: "logs-chatify")

**DI Extension:** `ServiceCollectionElasticExtensions.AddElasticLoggingChatify(IConfiguration)`

**Registered Services:**
- `ElasticOptionsEntity` (singleton) - Configuration options

**Implementation Status:** Options registration only (Serilog sink configuration pending)

**Configuration Section:** `Chatify:Elastic`

```json
{
  "Chatify": {
    "Elastic": {
      "Uri": "http://localhost:9200",
      "Username": "elastic",
      "Password": "changeme",
      "IndexPrefix": "logs-chatify"
    }
  }
}
```

**Index Pattern:** `{IndexPrefix}-{Date}` (e.g., `logs-chatify-2026-01-15`)

---

### Service Registration in Program.cs

Infrastructure providers are registered in the host's `Startup.ConfigureServices` method in the following order:

```csharp
public void ConfigureServices(IServiceCollection services)
{
    // 1. Infrastructure Options (must be first)
    services.AddElasticLoggingChatify(Configuration);

    // 2. Infrastructure Providers
    services.AddDatabase(Configuration);
    services.AddCaching(Configuration);
    services.AddMessageBroker(Configuration);

    // 3. Application Services
    services.AddChatifyChatApplication();
}
```

**Registration Order Rationale:**
1. Elasticsearch options must be registered early for Serilog configuration
2. Infrastructure providers are registered before application services (dependency order)
3. Application services depend on infrastructure ports being implemented

---

### Placeholder Implementations

All infrastructure services currently have placeholder implementations that:
1. Log the method invocation with relevant parameters
2. Throw `NotImplementedException` with a descriptive message

This allows the application to compile and the DI container to be configured correctly while the actual implementations are developed in subsequent steps.

**Example Placeholder Pattern:**
```csharp
public Task<(int Partition, long Offset)> ProduceAsync(
    ChatEventDto chatEvent,
    CancellationToken cancellationToken)
{
    _logger.LogWarning(
        "KafkaChatEventProducerService.ProduceAsync called - NOT YET IMPLEMENTED. " +
        "Event: MessageId={MessageId}, ScopeType={ScopeType}, ScopeId={ScopeId}",
        chatEvent.MessageId, chatEvent.ScopeType, chatEvent.ScopeId);

    throw new NotImplementedException(
        $"KafkaChatEventProducerService.ProduceAsync is not yet implemented.");
}
```

---

### Future Implementation Steps

The placeholder implementations will be replaced with actual provider integration in subsequent development steps:

1. **Kafka:** Implement producer with Confluent.Kafka, partitioning, and error handling
2. **Redis:** Implement presence sets with TTL and sliding-window rate limiting
3. **ScyllaDB:** Implement repository with prepared statements and proper consistency levels
4. **Elasticsearch:** Configure Serilog sink with proper index formatting

Each implementation will maintain the existing interfaces and options, ensuring minimal changes to the rest of the codebase.

## Testing Strategy
Testing relies on xUnit and FluentAssertions for unit coverage, with `Microsoft.AspNetCore.Mvc.Testing` for host-level integration tests and `coverlet.collector` for coverage reporting.

## Operational Considerations
Placeholders only. Future steps will cover deployment and runtime considerations.

## Appendix
Placeholders only.

## Chat Domain - Ordering Scope Rules

### Overview
Chatify enforces strict message ordering within each scope to maintain conversation integrity. The ordering system ensures all participants see messages in the exact same chronological sequence, preventing confusion and preserving context.

### Scope Types

#### Channel Scope
- **Definition**: A multi-participant chat where messages are broadcast to all members.
- **ScopeId Format**: Channel name, UUID, or other stable identifier.
- **Use Cases**: Team communication, topic-based discussions, public group conversations.
- **Ordering**: Messages with `ScopeType = Channel` and the same `ScopeId` are ordered together.

#### DirectMessage Scope
- **Definition**: A one-to-one or small group conversation between specific users.
- **ScopeId Format**: Composite key derived from participant IDs or conversation UUID.
- **Use Cases**: Private conversations, direct user-to-user messaging.
- **Ordering**: Messages with `ScopeType = DirectMessage` and the same `ScopeId` are ordered together.

### Message Ordering Guarantees

1. **Scope-Based Ordering**: Messages are ordered strictly by `CreatedAtUtc` timestamp within each unique `(ScopeType, ScopeId)` combination.
2. **Independent Processing**: Different scopes (different ScopeType OR different ScopeId) can be processed in parallel without blocking.
3. **Total Ordering**: Within a single scope, messages form a total order. If two messages have identical `CreatedAtUtc` values, `MessageId` serves as the tiebreaker.

### ChatMessageEntity Structure

```csharp
public record ChatMessageEntity
{
    Guid MessageId;           // Unique identifier
    ChatScopeTypeEnum ScopeType;  // Channel or DirectMessage
    string ScopeId;           // Scope identifier
    string SenderId;          // Who sent the message
    string Text;              // Message content (max 4096 chars)
    DateTime CreatedAtUtc;    // Ordering timestamp
    string OriginPodId;       // Pod that created the message
}
```

### Domain Policy Validation

The `ChatDomainPolicy` static class enforces these invariants:

| Property | Validation Rule | Constant |
|----------|----------------|----------|
| `ScopeId` | 1-256 characters, not null/whitespace | `MaxScopeIdLength = 256` |
| `SenderId` | 1-256 characters, not null/whitespace | `MaxSenderIdLength = 256` |
| `Text` | 0-4096 characters, not null | `MaxTextLength = 4096` |
| `OriginPodId` | 1-256 characters, not null/whitespace | `MaxOriginPodIdLength = 256` |

### Usage Pattern

```csharp
// Validate before creating
ChatDomainPolicy.ValidateScopeId("general");
ChatDomainPolicy.ValidateSenderId("user-123");
ChatDomainPolicy.ValidateText("Hello, world!");
ChatDomainPolicy.ValidateOriginPodId("chat-api-7d9f4c5b6d-abc12");

// Create the message
var message = new ChatMessageEntity
{
    MessageId = Guid.NewGuid(),
    ScopeType = ChatScopeTypeEnum.Channel,
    ScopeId = "general",
    SenderId = "user-123",
    Text = "Hello, world!",
    CreatedAtUtc = DateTime.UtcNow,
    OriginPodId = "chat-api-7d9f4c5b6d-abc12"
};
```

### Kafka Ordering Implications

When producing messages to Kafka:
- Use `(ScopeType, ScopeId)` as the partition key to ensure all messages for a scope land in the same partition.
- Kafka's per-partition ordering guarantees the strict sequencing required by the domain.
- Different scopes can be distributed across partitions for parallel consumption.

## Chat Application - Use Cases and Ports

### Overview
The Chat Application layer implements use cases through Commands and Queries following the CQRS pattern. It defines ports (interfaces) for infrastructure concerns and orchestrates business workflows while depending only on the Domain layer and BuildingBlocks.

### Application Layer Structure

```
Chatify.Chat.Application/
├── Commands/
│   └── SendChatMessage/
│       ├── SendChatMessageCommand.cs
│       └── SendChatMessageCommandHandler.cs
├── Common/
│   ├── Constants/
│   │   └── ChatifyConstants.cs
│   └── Errors/
│       └── ServiceError.cs
├── Dtos/
│   ├── ChatSendRequestDto.cs
│   ├── ChatEventDto.cs
│   └── EnrichedChatEventDto.cs
├── Ports/
│   ├── IChatEventProducerService.cs
│   ├── IChatHistoryRepository.cs
│   ├── IPresenceService.cs
│   ├── IRateLimitService.cs
│   └── IPodIdentityService.cs
└── DependencyInjection/
    └── ServiceCollectionExtensions.cs
```

### Constants and Error Handling

#### ChatifyConstants
Centralizes all magic strings and numeric values to improve maintainability:

```csharp
public static class ChatifyConstants
{
    public static class RateLimit
    {
        public const string SendChatMessageKeyPrefix = "user-{0}:send-message";
        public const int SendChatMessageThreshold = 100;
        public const int SendChatMessageWindowSeconds = 60;
        public static string SendChatMessageKey(string userId) { ... }
    }

    public static class ErrorCodes
    {
        public const string ValidationError = "VALIDATION_ERROR";
        public const string RateLimitExceeded = "RATE_LIMIT_EXCEEDED";
        public const string ConfigurationError = "CONFIGURATION_ERROR";
        public const string EventProductionFailed = "EVENT_PRODUCTION_FAILED";
    }

    public static class LogMessages
    {
        public const string ProcessingSendChatMessage = "Processing SendChatMessage command for sender {SenderId}...";
        public const string DomainValidationFailed = "Domain validation failed for message from {SenderId}...";
        // ... more log message templates
    }
}
```

#### ServiceError
Provides factory methods for creating typed errors without magic strings:

```csharp
public static class ServiceError
{
    public static class Chat
    {
        public static ErrorEntity ValidationFailed(string message, Exception? exception = null);
        public static ErrorEntity RateLimitExceeded(string userId, Exception? exception = null);
    }

    public static class System
    {
        public static ErrorEntity ConfigurationError(string message, Exception? exception = null);
        public static ErrorEntity ConfigurationError(Exception? exception = null);
    }

    public static class Messaging
    {
        public static ErrorEntity EventProductionFailed(string message, Exception? exception = null);
        public static ErrorEntity EventProductionFailed(Exception? exception = null);
    }
}
```

### Data Transfer Objects

#### ChatSendRequestDto
Represents a request to send a chat message.
```csharp
public record ChatSendRequestDto
{
    ChatScopeTypeEnum ScopeType;  // Channel or DirectMessage
    string ScopeId;               // Target conversation identifier
    string Text;                  // Message content (0-4096 chars)
}
```

#### ChatEventDto
Represents a chat event that flows through the system.
```csharp
public record ChatEventDto
{
    Guid MessageId;               // Unique event identifier
    ChatScopeTypeEnum ScopeType;  // Channel or DirectMessage
    string ScopeId;               // Scope for ordering
    string SenderId;              // Who sent the message
    string Text;                  // Message content
    DateTime CreatedAtUtc;        // Ordering timestamp
    string OriginPodId;           // Pod that created the event
}
```

#### EnrichedChatEventDto
Extends ChatEventDto with broker metadata (provider-agnostic).
```csharp
public record EnrichedChatEventDto
{
    ChatEventDto ChatEvent;       // Base event data
    int Partition;                // Broker partition where event was written
    long Offset;                  // Broker offset within the partition
}
```

### Ports (Interfaces)

The Application layer defines ports for infrastructure concerns. These interfaces are implemented in the Infrastructure layer.

#### IChatEventProducerService
Produces chat events to the messaging system (Kafka, Redpanda, etc.).
```csharp
interface IChatEventProducerService
{
    Task<(int Partition, long Offset)> ProduceAsync(
        ChatEventDto chatEvent,
        CancellationToken cancellationToken);
}
```

#### IChatHistoryRepository
Persists and retrieves chat message history.
```csharp
interface IChatHistoryRepository
{
    Task AppendAsync(ChatEventDto chatEvent, CancellationToken cancellationToken);
    Task<IReadOnlyList<ChatEventDto>> QueryByScopeAsync(
        ChatScopeTypeEnum scopeType,
        string scopeId,
        DateTime? fromUtc,
        DateTime? toUtc,
        int limit,
        CancellationToken cancellationToken);
}
```

#### IPresenceService
Manages user online/offline presence and connections.
```csharp
interface IPresenceService
{
    Task SetOnlineAsync(string userId, string connectionId, CancellationToken cancellationToken);
    Task SetOfflineAsync(string userId, string connectionId, CancellationToken cancellationToken);
    Task HeartbeatAsync(string userId, string connectionId, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> GetConnectionsAsync(string userId, CancellationToken cancellationToken);
}
```

#### IRateLimitService
Enforces rate limits to prevent abuse.
```csharp
interface IRateLimitService
{
    Task<ResultEntity> CheckAndIncrementAsync(
        string key,
        int threshold,
        int windowSeconds,
        CancellationToken cancellationToken);
}
```

#### IPodIdentityService
Provides pod identity for tracking and debugging.
```csharp
interface IPodIdentityService
{
    string PodId { get; }
}
```

### Command Handler Flow

The `SendChatMessageCommandHandler` orchestrates the message sending workflow:

```
┌─────────────────┐
│ API Layer       │
│ (Creates        │
│  Command)       │
└────────┬────────┘
         │
         ▼
┌─────────────────────────────────────────────────────────┐
│ SendChatMessageCommandHandler.HandleAsync()            │
├─────────────────────────────────────────────────────────┤
│                                                         │
│ 1. Validate Domain Policy                               │
│    └─> ChatDomainPolicy.Validate*()                    │
│        └─> Return ServiceError.Chat.ValidationFailed   │
│                                                         │
│ 2. Check Rate Limit                                     │
│    └─> IRateLimitService.CheckAndIncrementAsync()      │
│        └─> Return ServiceError.Chat.RateLimitExceeded  │
│                                                         │
│ 3. Create ChatEventDto                                  │
│    └─> Generate MessageId (Guid)                       │
│    └─> Get current time (IClockService)                │
│    └─> Get origin pod ID (IPodIdentityService)         │
│                                                         │
│ 4. Produce Event to Messaging System                   │
│    └─> IChatEventProducerService.ProduceAsync()        │
│        └─> Returns (partition, offset)                 │
│                                                         │
│ 5. Return EnrichedChatEventDto                          │
│    └─> Contains ChatEvent + broker metadata            │
│                                                         │
└─────────────────────────────────────────────────────────┘
         │
         ▼
┌─────────────────┐
│ API Layer       │
│ (Returns        │
│  Result)        │
└─────────────────┘
```

### Error Handling Strategy

The Application layer uses the `ResultEntity<T>` pattern with `ServiceError` factory methods:

| Error Type | ServiceError Method | Error Code | Handling |
|------------|---------------------|------------|----------|
| Validation failed | `ServiceError.Chat.ValidationFailed()` | `VALIDATION_ERROR` | Domain policy validation failure, returned to client |
| Rate limit exceeded | `ServiceError.Chat.RateLimitExceeded()` | `RATE_LIMIT_EXCEEDED` | User exceeded send threshold, retry after delay |
| Configuration error | `ServiceError.System.ConfigurationError()` | `CONFIGURATION_ERROR` | Pod ID validation failed, system misconfiguration |
| Event production failed | `ServiceError.Messaging.EventProductionFailed()` | `EVENT_PRODUCTION_FAILED` | Message broker unavailable, retry |

### Dependency Registration

The Application layer provides an extension method for service registration:

```csharp
// In Program.cs
builder.Services.AddChatifyChatApplication();
```

This registers:
- All command handlers (scoped lifetime)
- Application services (none currently, will be added as needed)

Infrastructure services are registered separately via the Infrastructure layer's extension methods (e.g., `AddMessageBroker`, `AddCaching`).

## Chatify.ChatApi Host - Request Flow and Middleware

### Overview
The `Chatify.ChatApi` host is the entry point for all client requests. It implements a comprehensive middleware pipeline for cross-cutting concerns and exposes a SignalR hub for real-time chat functionality.

### Host Structure

```
src/Hosts/Chatify.ChatApi/
├── Program.cs              # Application entry point, host builder, service registration
├── Middleware/
│   ├── CorrelationIdMiddleware.cs          # Distributed tracing middleware
│   └── GlobalExceptionHandlingMiddleware.cs # Global exception handler
└── Hubs/
    └── ChatHubService.cs  # SignalR hub for real-time chat
```

### Service Registration

The host's `Startup.ConfigureServices` method registers services in the following order:

```csharp
public void ConfigureServices(IServiceCollection services)
{
    // 1. BuildingBlocks (clock, correlation)
    services.AddSingleton<IClockService, SystemClockService>();
    services.AddSingleton<ICorrelationContextAccessor, CorrelationContextAccessor>();

    // 2. Infrastructure Options
    services.AddElasticLoggingChatify(Configuration);

    // 3. Infrastructure Providers
    services.AddDatabase(Configuration);
    services.AddCaching(Configuration);
    services.AddMessageBroker(Configuration);

    // 4. Application Services
    services.AddChatifyChatApplication();

    // 5. ASP.NET Core Services
    services.AddControllers();
    services.AddSignalR();
}
```

### Middleware Pipeline

The HTTP request pipeline is configured in `Startup.Configure`:

```
┌─────────────────────────────────────────────────────────────────┐
│                     HTTP Request Flow                          │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  1. Developer Exception Page (development only)                │
│     └─> Shows detailed error information in development        │
│                                                                 │
│  2. GlobalExceptionHandlingMiddleware                          │
│     ├─> Wraps downstream execution in try/catch                │
│     ├─> Catches all unhandled exceptions                       │
│     ├─> Logs exception with correlation ID                      │
│     └─> Returns RFC 7807 ProblemDetails response              │
│                                                                 │
│  3. CorrelationIdMiddleware                                    │
│     ├─> Extracts X-Correlation-ID header from request          │
│     ├─> Validates format (corr_{guid})                         │
│     ├─> Generates new ID if missing/invalid                    │
│     ├─> Stores in ICorrelationContextAccessor (AsyncLocal)     │
│     └─> Adds X-Correlation-ID to response headers              │
│                                                                 │
│  4. Routing                                                    │
│     └─> Maps request to endpoint (controller or hub)           │
│                                                                 │
│  5. Authorization                                              │
│     └─> Checks user permissions                                │
│                                                                 │
│  6. Endpoints                                                  │
│     ├─> Controllers (/api/*)                                   │
│     └─> SignalR Hubs (/hubs/chat)                              │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

### CorrelationIdMiddleware

**Location:** `src/Hosts/Chatify.ChatApi/Middleware/CorrelationIdMiddleware.cs`

**Purpose:** Ensures every HTTP request has a correlation ID for distributed tracing.

**Behavior:**
1. Checks for `X-Correlation-ID` header in the request
2. Validates the format (`corr_{guid}`) using `CorrelationIdUtility`
3. Generates a new correlation ID if missing or invalid
4. Stores the ID in `ICorrelationContextAccessor` (AsyncLocal storage)
5. Adds the correlation ID to response headers

**Example:**
```
Request:  (no X-Correlation-ID header)
          ↓
Middleware: Generates "corr_a1b2c3d4-e5f6-7890-abcd-ef1234567890"
          ↓
AsyncLocal: Stores ID for async context flow
          ↓
Response: X-Correlation-ID: corr_a1b2c3d4-e5f6-7890-abcd-ef1234567890
```

### GlobalExceptionHandlingMiddleware

**Location:** `src/Hosts/Chatify.ChatApi/Middleware/GlobalExceptionHandlingMiddleware.cs`

**Purpose:** Catches all unhandled exceptions and returns standardized ProblemDetails responses.

**Exception to Status Code Mapping:**

| Exception Type | HTTP Status | Title |
|----------------|-------------|-------|
| `ArgumentException`, `ArgumentNullException` | 400 | Bad Request |
| `UnauthorizedAccessException` | 401 | Unauthorized |
| `KeyNotFoundException` | 404 | Not Found |
| `InvalidOperationException` | 409 | Conflict |
| `TimeoutException` | 504 | Gateway Timeout |
| All other exceptions | 500 | Internal Server Error |

**Response Format (RFC 7807):**
```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.1",
  "title": "Bad Request",
  "status": 400,
  "detail": "The request was invalid or missing required parameters.",
  "instance": "/chat/send"
}
```

### ChatHubService (SignalR Hub)

**Location:** `src/Hosts/Chatify.ChatApi/Hubs/ChatHubService.cs`

**Endpoint:** `/hubs/chat`

**Methods:**

| Method | Parameters | Description |
|--------|------------|-------------|
| `JoinScopeAsync` | `scopeId` | Adds client to SignalR group for the scope |
| `LeaveScopeAsync` | `scopeId` | Removes client from SignalR group |
| `SendAsync` | `ChatSendRequestDto` | Processes and broadcasts message to scope |

**Lifecycle Hooks:**

| Method | Description |
|--------|-------------|
| `OnConnectedAsync` | Logs client connection with connection ID |
| `OnDisconnectedAsync` | Logs client disconnection with exception details |

**SignalR Group Strategy:**
- Each `scopeId` maps to a SignalR group
- Clients join groups to receive messages for specific scopes
- Messages are broadcast using `Clients.Group(scopeId).SendAsync()`

### Complete Request Flow

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    SignalR Chat Message - Complete Flow                     │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  CLIENT                                                                     │
│     ┌──────────────┐                                                        │
│     │ Browser App  │                                                        │
│     └──────┬───────┘                                                        │
│            │                                                                 │
│            │ 1. WebSocket Connection                                         │
│            │    POST /hubs/chat (SignalR negotiation)                        │
│            ▼                                                                 │
│     ┌─────────────────────────────────────────────────────────────────┐    │
│     │              Chatify.ChatApi Middleware Pipeline                 │    │
│     ├─────────────────────────────────────────────────────────────────┤    │
│     │                                                                 │    │
│     │  CorrelationIdMiddleware → Generate/Set Correlation ID          │    │
│     │  GlobalExceptionHandlingMiddleware → Wrap execution             │    │
│     │  SignalR Hub invocation → ChatHubService.OnConnectedAsync()      │    │
│     │                                                                 │    │
│     └─────────────────────────────────────────────────────────────────┘    │
│            │                                                                 │
│            │ 2. Join Scope                                                      │
│            │    hub.invoke("JoinScopeAsync", "general")                         │
│            ▼                                                                 │
│     ┌─────────────────────────────────────────────────────────────────┐    │
│     │                    ChatHubService                               │    │
│     │  ────────────────────────────────────────────────────────────  │    │
│     │  JoinScopeAsync(string scopeId)                                │    │
│     │    └─> Groups.AddToGroupAsync(connectionId, "general")         │    │
│     │                                                                 │    │
│     └─────────────────────────────────────────────────────────────────┘    │
│            │                                                                 │
│            │ 3. Send Message                                                    │
│            │    hub.invoke("SendAsync", requestDto)                             │
│            ▼                                                                 │
│     ┌─────────────────────────────────────────────────────────────────┐    │
│     │                    ChatHubService                               │    │
│     │  ────────────────────────────────────────────────────────────  │    │
│     │  SendAsync(ChatSendRequestDto request)                         │    │
│     │    ├─ Extract senderId from auth context                       │    │
│     │    ├─ Create SendChatMessageCommand                            │    │
│     │    └─ Call SendChatMessageCommandHandler.HandleAsync()         │    │
│     │                           │                                      │    │
│     │                           ▼                                      │    │
│     │  ┌─────────────────────────────────────────────────────────┐   │    │
│     │  │     SendChatMessageCommandHandler                       │   │    │
│     │  ├─────────────────────────────────────────────────────────┤   │    │
│     │  │                                                         │   │    │
│     │  │  1. Validate Domain Policy                              │   │    │
│     │  │     └─> ChatDomainPolicy.Validate*()                    │   │    │
│     │  │                                                         │   │    │
│     │  │  2. Check Rate Limit                                    │   │    │
│     │  │     └─> IRateLimitService.CheckAndIncrementAsync()      │   │    │
│     │  │                                                         │   │    │
│     │  │  3. Create ChatEventDto                                 │   │    │
│     │  │     └─> MessageId, CreatedAtUtc, OriginPodId            │   │    │
│     │  │                                                         │   │    │
│     │  │  4. Produce to Kafka                                    │   │    │
│     │  │     └─> IChatEventProducerService.ProduceAsync()        │   │    │
│     │  │         └─> Returns (partition, offset)                 │   │    │
│     │  │                                                         │   │    │
│     │  │  5. Return EnrichedChatEventDto                          │   │    │
│     │  │                                                         │   │    │
│     │  └─────────────────────────────────────────────────────────┘   │    │
│     │                           │                                      │    │
│     │                           ▼                                      │    │
│     │  Broadcast to Group:                                                │    │
│     │  Clients.Group("general").SendAsync("ReceiveMessage", chatEvent)  │    │
│     │                                                                 │    │
│     └─────────────────────────────────────────────────────────────────┘    │
│            │                                                                 │
│            │ 4. Broadcast to All Clients in Scope                                │
│            │    All clients in "general" group receive message                 │
│            ▼                                                                 │
│     ┌──────────────┐                                                        │
│     │ Browser App  │  ← ReceiveMessage event                                 │
│     └──────────────┘                                                        │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Key Integration Points

| Component | Integration Point | Responsibility |
|-----------|-------------------|----------------|
| `CorrelationIdMiddleware` | Early in pipeline | Set correlation ID for all requests |
| `GlobalExceptionHandlingMiddleware` | Wraps pipeline | Catch and handle all exceptions |
| `ChatHubService` | SignalR endpoint | Real-time hub methods |
| `SendChatMessageCommandHandler` | Application layer | Orchestrate message processing |
| `IPodIdentityService` | Infrastructure | Provide pod identity |
| `ICorrelationContextAccessor` | BuildingBlocks | Async-local correlation storage |

