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

#### Kafka (Event Streaming)
**Purpose:** Produces and consumes chat events for asynchronous messaging and fan-out delivery.

**Options:** `KafkaOptionsEntity`
- `BootstrapServers` - Comma-separated list of broker addresses
- `TopicName` - Topic for chat events
- `Partitions` - Number of topic partitions
- `BroadcastConsumerGroupPrefix` - Prefix for broadcast consumer groups

**DI Extension:** `ServiceCollectionKafkaExtensions.AddKafkaChatify(IConfiguration)`

**Registered Services:**
- `KafkaOptionsEntity` (singleton) - Configuration options
- `KafkaChatEventProducerService` (singleton) - Implements `IChatEventProducerService`
- Future: Background consumers for broadcast delivery

**Implementation Status:** Placeholder (logs and throws `NotImplementedException`)

**Configuration Section:** `Chatify:Kafka`

```json
{
  "Chatify": {
    "Kafka": {
      "BootstrapServers": "localhost:9092",
      "TopicName": "chat-events",
      "Partitions": 3,
      "BroadcastConsumerGroupPrefix": "chatify-broadcast"
    }
  }
}
```

**Partitioning Strategy:** Events are partitioned by `(ScopeType, ScopeId)` to ensure ordering within each chat scope. All messages for a scope go to the same partition, maintaining strict ordering while allowing parallel processing across scopes.

---

#### Redis (Presence, Rate Limiting, Caching)
**Purpose:** Manages user presence, enforces rate limits, and provides distributed caching.

**Options:** `RedisOptionsEntity`
- `ConnectionString` - Redis connection string

**DI Extension:** `ServiceCollectionRedisExtensions.AddRedisChatify(IConfiguration)`

**Registered Services:**
- `RedisOptionsEntity` (singleton) - Configuration options
- `RedisPresenceService` (singleton) - Implements `IPresenceService`
- `RedisRateLimitService` (singleton) - Implements `IRateLimitService`
- Future: `IConnectionMultiplexer` registration, caching services

**Implementation Status:** Placeholder (logs and throws `NotImplementedException`)

**Configuration Section:** `Chatify:Redis`

```json
{
  "Chatify": {
    "Redis": {
      "ConnectionString": "localhost:6379"
    }
  }
}
```

**Data Structures:**
- Presence: `presence:user:{userId}` (set of connection IDs with TTL)
- Rate Limiting: `ratelimit:{key}` (sorted set with sliding window)

---

#### ScyllaDB (Message Persistence)
**Purpose:** Durable storage for chat message history with time-series query optimization.

**Options:** `ScyllaOptionsEntity`
- `ContactPoints` - Comma-separated list of node addresses
- `Keyspace` - Keyspace name
- `Username` - Authentication username (optional)
- `Password` - Authentication password (optional)

**DI Extension:** `ServiceCollectionScyllaExtensions.AddScyllaChatify(IConfiguration)`

**Registered Services:**
- `ScyllaOptionsEntity` (singleton) - Configuration options
- `ScyllaChatHistoryRepository` (singleton) - Implements `IChatHistoryRepository`
- Future: `ISession` / `ICluster` registration, schema migrations

**Implementation Status:** Placeholder (logs and throws `NotImplementedException`)

**Configuration Section:** `Chatify:Scylla`

```json
{
  "Chatify": {
    "Scylla": {
      "ContactPoints": "scylla-node1:9042,scylla-node2:9042",
      "Keyspace": "chatify",
      "Username": "chatify_user",
      "Password": "secure_password"
    }
  }
}
```

**Table Schema:**
```sql
CREATE TABLE chat_messages (
    scope_type text,
    scope_id text,
    created_at_utc timestamp,
    message_id uuid,
    sender_id text,
    text text,
    origin_pod_id text,
    PRIMARY KEY ((scope_type, scope_id), created_at_utc, message_id)
) WITH CLUSTERING ORDER BY (created_at_utc ASC);
```

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
    services.AddScyllaChatify(Configuration);
    services.AddRedisChatify(Configuration);
    services.AddKafkaChatify(Configuration);

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

Infrastructure services are registered separately via the Infrastructure layer's extension methods (e.g., `AddKafkaChatify`, `AddRedisChatify`).

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
    services.AddScyllaChatify(Configuration);
    services.AddRedisChatify(Configuration);
    services.AddKafkaChatify(Configuration);

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

