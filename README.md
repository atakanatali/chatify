# Chatify

<img width="512" height="512" alt="Image" src="https://github.com/user-attachments/assets/b4c51e77-f31d-42bf-a940-7633ea83c907" />

## Table of Contents
- [Overview](#overview)
- [Architecture](#architecture)
- [Request Flow](#request-flow)
- [Getting Started](#getting-started)
- [Development Workflow](#development-workflow)
- [Testing](#testing)
- [Deployment](#deployment)
- [Observability](#observability)
- [Appendix](#appendix)

## Overview
Chatify is a modular monolith built with Clean Architecture and SOLID principles. The project provides a real-time chat API with SignalR hubs, comprehensive middleware for cross-cutting concerns, and a layered architecture that separates domain logic from infrastructure implementation.

## Architecture
See [architecture.md](architecture.md) for the architectural overview and module responsibilities.

### Solution Structure & Module Boundaries
Chatify is organized around a modular monolith layout with a dedicated host, a shared kernel, and a chat module that follows Clean Architecture layering. The solution is anchored by `Chatify.sln` with the following structure:

- `src/Hosts/Chatify.ChatApi`: HTTP host that composes application/infrastructure dependencies.
- `src/BuildingBlocks/Chatify.BuildingBlocks`: shared kernel utilities and cross-cutting abstractions.
- `src/Modules/Chat/Chatify.Chat.Domain`: domain model and business rules.
- `src/Modules/Chat/Chatify.Chat.Application`: use cases, application services, and orchestration.
- `src/Modules/Chat/Chatify.Chat.Infrastructure`: data access and integrations for the chat module.
- `tests/Chatify.Chat.UnitTests`: unit tests for the chat module.
- `tests/Chatify.ChatApi.IntegrationTests`: integration tests for the API host and chat module wiring.

Module boundaries are enforced through project references: the domain layer only depends on `BuildingBlocks` (when needed), the application layer depends on domain and shared kernel, infrastructure depends on application/domain/shared kernel, and the host only depends on application/infrastructure/shared kernel.

### Dependency Overview
Chatify manages baseline dependencies centrally in `Directory.Packages.props` to keep versions consistent. Highlights include SignalR for realtime chat, Serilog for logging (console + Elastic), Kafka/Redis/Cassandra clients for infrastructure integrations, and xUnit + FluentAssertions for testing.

### Service Registration
The host's `Program.cs` orchestrates dependency injection through layer-specific extension methods:

```csharp
// BuildingBlocks (clock, correlation)
services.AddSingleton<IClockService, SystemClockService>();
services.AddSingleton<ICorrelationContextAccessor, CorrelationContextAccessor>();

// Infrastructure Options (must be first)
services.AddElasticLoggingChatify(Configuration);

// Infrastructure Providers
services.AddScyllaChatify(Configuration);   // Chat history persistence
services.AddRedisChatify(Configuration);     // Presence, rate limiting, pod identity
services.AddKafkaChatify(Configuration);     // Event streaming

// Application Services
services.AddChatifyChatApplication();        // Command handlers

// ASP.NET Core Services
services.AddControllers();
services.AddSignalR();
```

### Middleware Pipeline
The HTTP request pipeline is configured in the following order:

```
1. Developer Exception Page (development only)
2. Global Exception Handling Middleware
   ├─ Catches all unhandled exceptions
   ├─ Logs with correlation ID
   └─ Returns RFC 7807 ProblemDetails
3. Correlation ID Middleware
   ├─ Extracts X-Correlation-ID header
   ├─ Generates new ID if missing
   ├─ Stores in ICorrelationContextAccessor
   └─ Adds to response headers
4. Routing
5. Authorization
6. Endpoints
   ├─ Controllers (/api/*)
   └─ SignalR Hubs (/hubs/chat)
```

## Request Flow

### SignalR Chat Message Flow

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                          Chatify Chat Message Flow                          │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  1. SIGNALR CLIENT CONNECTS                                                 │
│     ┌──────────────┐                                                        │
│     │   Browser/   │                                                        │
│     │   Mobile App │                                                        │
│     └──────┬───────┘                                                        │
│            │ WebSocket connection to /hubs/chat                             │
│            ▼                                                                │
│     ┌──────────────┐                                                        │
│     │ ChatHubService│  ────────────────────────┐                            │
│     │  (SignalR)   │  │ OnConnectedAsync()     │                            │
│     └──────────────┘  │   - Logs connection    │                            │
│                      └─────────────────────────┘                            │
│                                                                             │
│  2. CLIENT JOINS SCOPE                                                      │
│     ┌──────────────┐                                                        │
│     │   Client     │                                                        │
│     └──────┬───────┘                                                        │
│            │ hub.invoke("JoinScopeAsync", "general")                        │
│            ▼                                                                │
│     ┌──────────────┐                                                        │
│     │ ChatHubService│  ────────────────────────┐                            │
│     └──────────────┘  │ Groups.AddToGroupAsync()│                            │
│                      │   - Adds connection to   │                            │
│                      │     "general" group      │                            │
│                      └─────────────────────────┘                            │
│                                                                             │
│  3. CLIENT SENDS MESSAGE                                                    │
│     ┌──────────────┐                                                        │
│     │   Client     │                                                        │
│     └──────┬───────┘                                                        │
│            │ hub.invoke("SendAsync", requestDto)                            │
│            ▼                                                                │
│     ┌─────────────────────────────────────────────────────────────────┐    │
│     │                    MIDDLEWARE PIPELINE                           │    │
│     ├─────────────────────────────────────────────────────────────────┤    │
│     │                                                                 │    │
│     │  CorrelationIdMiddleware                                        │    │
│     │  ├─ Extract/Generate Correlation ID                            │    │
│     │  ├─ Store in ICorrelationContextAccessor                        │    │
│     │  └─ Add to response: X-Correlation-ID                           │    │
│     │                          │                                      │    │
│     │                          ▼                                      │    │
│     │  GlobalExceptionHandlingMiddleware                              │    │
│     │  ├─ Wrap downstream execution in try/catch                      │    │
│     │  ├─ On exception: Log + Return ProblemDetails                   │    │
│     │  └─ On success: Continue to next middleware                     │    │
│     │                          │                                      │    │
│     │                          ▼                                      │    │
│     │  SignalR Hub Invocation (ChatHubService.SendAsync)              │    │
│     │                                                                 │    │
│     └────────────────────────────────┬────────────────────────────────┘    │
│                                      │                                     │
│                                      ▼                                     │
│     ┌─────────────────────────────────────────────────────────────────┐    │
│     │              SendChatMessageCommandHandler                      │    │
│     ├─────────────────────────────────────────────────────────────────┤    │
│     │                                                                 │    │
│     │  1. VALIDATE DOMAIN POLICY                                      │    │
│     │     └─> ChatDomainPolicy.ValidateSenderId()                    │    │
│     │     └─> ChatDomainPolicy.ValidateScopeId()                     │    │
│     │     └─> ChatDomainPolicy.ValidateText()                        │    │
│     │         │                                                       │    │
│     │         ├─ FAIL: Return ResultEntity.Failure(ValidationError)   │    │
│     │         └─ PASS: Continue                                       │    │
│     │                                                                 │    │
│     │  2. CHECK RATE LIMIT                                            │    │
│     │     └─> IRateLimitService.CheckAndIncrementAsync()              │    │
│     │         │                                                       │    │
│     │         ├─ FAIL: Return ResultEntity.Failure(RateLimitExceeded)│    │
│     │         └─ PASS: Continue                                       │    │
│     │                                                                 │    │
│     │  3. CREATE CHAT EVENT                                           │    │
│     │     └─> MessageId = Guid.NewGuid()                              │    │
│     │     └─> CreatedAtUtc = IClockService.GetUtcNow()                │    │
│     │     └─> OriginPodId = IPodIdentityService.PodId                 │    │
│     │         │                                                       │    │
│     │         ▼                                                       │    │
│     │     ChatEventDto {                                              │    │
│     │       MessageId, ScopeType, ScopeId,                            │    │
│     │       SenderId, Text, CreatedAtUtc, OriginPodId                 │    │
│     │     }                                                           │    │
│     │                                                                 │    │
│     │  4. PRODUCE TO MESSAGING SYSTEM                                 │    │
│     │     └─> IChatEventProducerService.ProduceAsync()                │    │
│     │         │                                                       │    │
│     │         ├─ FAIL: Return ResultEntity.Failure(EventProductionFailed)│   │
│     │         └─ SUCCESS: Returns (partition, offset)                 │    │
│     │                                                                 │    │
│     │  5. RETURN SUCCESS                                              │    │
│     │     └─> EnrichedChatEventDto {                                   │    │
│     │           ChatEvent, Partition, Offset                           │    │
│     │         }                                                       │    │
│     │                                                                 │    │
│     └────────────────────────────────┬────────────────────────────────┘    │
│                                      │                                     │
│                                      ▼                                     │
│     ┌─────────────────────────────────────────────────────────────────┐    │
│     │                    BROADCAST TO SCOPE                           │    │
│     ├─────────────────────────────────────────────────────────────────┤    │
│     │                                                                 │    │
│     │  Clients.Group(scopeId).SendAsync("ReceiveMessage", chatEvent)  │    │
│     │                                                                 │    │
│     │  All clients in the scope receive the message in real-time      │    │
│     │                                                                 │    │
│     └─────────────────────────────────────────────────────────────────┘    │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Key Components

#### ChatHubService (SignalR Hub)
- **Location**: `src/Hosts/Chatify.ChatApi/Hubs/ChatHubService.cs`
- **Endpoint**: `/hubs/chat`
- **Methods**:
  - `JoinScopeAsync(scopeId)` - Add client to scope group
  - `LeaveScopeAsync(scopeId)` - Remove client from scope group
  - `SendAsync(requestDto)` - Process and broadcast message
- **Lifecycle Hooks**:
  - `OnConnectedAsync()` - Log client connection
  - `OnDisconnectedAsync(exception)` - Log client disconnection

#### Middleware
- **CorrelationIdMiddleware**: Ensures every request has a correlation ID for distributed tracing
- **GlobalExceptionHandlingMiddleware**: Catches exceptions and returns ProblemDetails responses

#### Command Handler
- **SendChatMessageCommandHandler**: Orchestrates message validation, rate limiting, event creation, and production

## Getting Started
Placeholders only. Future steps will include build and run instructions for Chatify.

## Development Workflow
Placeholders only. Future steps will describe local development workflows for Chatify.

## Testing
Placeholders only. Future steps will describe how to run tests for Chatify.

## Deployment
Placeholders only. Future steps will document deployment options for Chatify.

## Observability
Chatify implements comprehensive observability through structured logging with Serilog and Elasticsearch, enabling centralized log aggregation, powerful search capabilities, and real-time monitoring.

### Logging Architecture

#### Design Philosophy
- **Shared Abstraction**: `ILogService` in BuildingBlocks provides a simplified, application-level logging interface
- **Correlation Awareness**: All logs automatically include correlation IDs for distributed tracing
- **Structured Logging**: Context objects are serialized as structured properties for powerful querying
- **Centralized Aggregation**: Logs ship to Elasticsearch for long-term storage and analysis

#### Implementation Location
The logging abstraction (`ILogService`, `LogService`, `LoggingOptionsEntity`) and DI extensions are placed in **BuildingBlocks** rather than the Observability module because:
- Logging is a fundamental cross-cutting primitive like CorrelationId and Clock
- It's used across all modules and layers
- It should have no dependencies on other modules
- It aligns with the "Shared Kernel" concept in Domain-Driven Design
- The Observability module is reserved for domain-specific observability features

#### LogService API

```csharp
public interface ILogService
{
    void Info(string message, object? context = null);
    void Warn(string message, object? context = null);
    void Error(Exception exception, string message, object? context = null);
}
```

**Usage Examples:**
```csharp
// Simple info log
_logService.Info("User logged in");

// Info log with context
_logService.Info("Order created", new { OrderId = 123, CustomerId = 456 });

// Warning with context
_logService.Warn("High memory usage detected", new { UsagePercent = 85, Threshold = 80 });

// Error log with exception
_logService.Error(ex, "Failed to process payment", new { OrderId = 123, Amount = 99.99m });
```

#### Serilog Configuration

Serilog is configured in `Program.cs` with the following enrichers:
- **CorrelationId**: From `ICorrelationContextAccessor` for distributed tracing
- **MachineName**: Host identifier for multi-pod deployments
- **EnvironmentName**: Production/staging/development
- **ProcessId**: Process identifier
- **ThreadId**: Thread identifier
- **Application**: Service name (e.g., "Chatify.ChatApi")

#### Elasticsearch Integration

**Index Naming Pattern:**
```
logs-chatify-{servicename}-{yyyy.MM.dd}
```
Examples:
- `logs-chatify-chatapi-2026.01.15`
- `logs-chatify-worker-2026.01.15`

**Configuration (appsettings.json):**
```json
{
  "Chatify": {
    "Logging": {
      "Uri": "http://localhost:9200",
      "Username": "elastic",
      "Password": "changeme",
      "IndexPrefix": "logs-chatify-chatapi"
    }
  }
}
```

**Features:**
- Console sink for immediate feedback during development
- Elasticsearch sink for centralized log aggregation
- Data Streams API for optimized indexing
- Basic authentication support
- Automatic buffer configuration for reliability

#### Middleware Integration

The `GlobalExceptionHandlingMiddleware` uses `ILogService.Error` to log all unhandled exceptions with full context:
- Request path and method
- HTTP status code
- Exception type and message
- Correlation ID for distributed tracing

**Example log entry:**
```json
{
  "@timestamp": "2026-01-15T10:30:45.123Z",
  "level": "Error",
  "message": "Unhandled exception occurred. CorrelationId: abc-123, Path: /chat/send, Method: POST, StatusCode: 500",
  "correlationId": "abc-123",
  "context": {
    "Path": "/chat/send",
    "Method": "POST",
    "StatusCode": 500,
    "ExceptionType": "DatabaseConnectionException",
    "ExceptionMessage": "Unable to connect to ScyllaDB"
  },
  "exception": "DatabaseConnectionException: Unable to connect..."
}
```

#### Best Practices

**When to Use ILogService:**
- Application-level logging in command handlers and application services
- Business operation logging (e.g., "Order created", "Payment processed")
- Integration point logging (e.g., "Kafka message sent", "Database query executed")
- Error logging with business context

**When NOT to Use ILogService:**
- Low-level framework logging (use `ILogger` directly)
- Performance-critical paths with high-frequency logging (use `ILogger` for efficiency)
- Third-party library integration (use their native logging abstractions)

**Guidelines:**
- Use clear, descriptive messages that describe what happened (not how)
- Include relevant IDs and business identifiers in the context object
- Avoid logging sensitive data (passwords, tokens, personal information)
- Use structured context for easier querying (e.g., `new { OrderId = 123 }`)

#### Future Enhancements
The Observability module is reserved for:
- Metrics collection (Prometheus)
- Distributed tracing (OpenTelemetry)
- Health checks
- Custom dashboards and alerting rules

## Appendix
Placeholders only.
