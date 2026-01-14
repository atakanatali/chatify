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
The infrastructure layer is expected to integrate with:

- **Kafka** (`Confluent.Kafka`) for event streaming and async messaging.
- **Redis** (`StackExchange.Redis`) for caching, pub/sub, and presence.
- **Scylla/Cassandra** (`CassandraCSharpDriver`) for distributed persistence.
- **Elastic** (Serilog sink) for centralized log storage.

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

Infrastructure services are registered separately via the Infrastructure layer's extension methods (e.g., `AddChatifyChatKafka`, `AddChatifyChatRedis`).
