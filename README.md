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
services.AddDatabase(Configuration);         // Chat history persistence
services.AddCaching(Configuration);         // Presence, rate limiting, pod identity
services.AddMessageBroker(Configuration);    // Event streaming

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
   ├─ Uses ExceptionMappingUtility for consistent ProblemDetails
   ├─ Catches all unhandled exceptions
   ├─ Logs with correlation ID to Elasticsearch
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

### Global Error Handling

Chatify implements comprehensive global error handling across all layers:

#### HTTP Pipeline (Middleware)
- **GlobalExceptionHandlingMiddleware** catches all unhandled exceptions from HTTP requests
- Uses **ExceptionMappingUtility** to map exceptions to RFC 7807 ProblemDetails
- Logs all errors to Elasticsearch with correlation IDs via **ILogService**
- Returns consistent error responses with appropriate HTTP status codes

#### Background Services
Both background services implement two-level exception handling:

1. **Outer Loop (Service Level):** Catches unexpected exceptions, logs to Elasticsearch, and lets Kubernetes restart the service
2. **Inner Loop (Operation Level):** Handles per-operation errors with exponential backoff retry

**ChatHistoryWriterBackgroundService:**
```csharp
// Outer loop - Prevents service termination
try
{
    await ExecuteConsumeLoopAsync(stoppingToken);
}
catch (Exception ex)
{
    _logService.Error(ex, "Fatal error, will restart", context);
    throw; // Let Kubernetes restart
}

// Inner loop - Handles per-operation errors
while (!stoppingToken.IsCancellationRequested)
{
    try
    {
        var result = await _processor.ProcessAsync(message, stoppingToken);
        backoff.Reset(); // Success - reset backoff
    }
    catch (Exception ex) when (IsTransientError(ex))
    {
        _logService.Error(ex, "Transient error, retrying with backoff", context);
        await Task.Delay(backoff.NextDelayWithJitter(), stoppingToken);
    }
}
```

**ChatBroadcastBackgroundService:**
- Similar two-level error handling pattern
- Deserialization errors log payload preview and continue to next message
- All Kafka/SignalR exceptions logged to Elasticsearch
- Exponential backoff prevents overwhelming external services

#### Exception Mapping

**ExceptionMappingUtility** provides centralized exception-to-ProblemDetails mapping:

| Exception Type | HTTP Status | Error Code Pattern |
|----------------|-------------|-------------------|
| ArgumentException, ArgumentNullException | 400 | Validation errors |
| UnauthorizedAccessException | 401 | Authentication/Authorization |
| KeyNotFoundException | 404 | Resource not found |
| InvalidOperationException | 409 | State conflicts |
| TimeoutException | 504 | External timeouts |
| Other exceptions | 500 | Unexpected errors |

**Logging Guarantees:**
- No Kafka/Redis/Scylla exception leaks unlogged
- All background service errors logged to Elasticsearch via ILogService
- Correlation IDs included in all error logs
- Structured context includes partition, offset, consumer group, etc.

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

### SignalR Hub Testing with wscat

**Note:** `wscat` does not support the SignalR protocol directly. Use SignalR client libraries for proper testing. The examples below use standard WebSocket clients with SignalR-compatible messages.

#### Using the .NET SignalR Client

```csharp
using Microsoft.AspNetCore.SignalR.Client;

// Create connection
var connection = new HubConnectionBuilder()
    .WithUrl("http://localhost:5000/hubs/chat")
    .Build();

// Register message handler
connection.On<ChatEventDto>("ReceiveMessage", (chatEvent) => {
    Console.WriteLine($"[{chatEvent.ScopeId}] {chatEvent.SenderId}: {chatEvent.Text}");
});

// Start connection
await connection.StartAsync();

// Join a scope
await connection.InvokeAsync("JoinScopeAsync", "general");

// Send a message
await connection.InvokeAsync("SendAsync", new ChatSendRequestDto {
    ScopeType = ChatScopeTypeEnum.Channel,
    ScopeId = "general",
    Text = "Hello, Chatify!"
});
```

#### Using JavaScript/TypeScript SignalR Client

```javascript
// Install: npm install @microsoft/signalr

import * as signalR from '@microsoft/signalr';

// Build connection
const connection = new signalR.HubConnectionBuilder()
    .withUrl('/hubs/chat')
    .build();

// Deduplication for at-least-once delivery
const seenMessages = new Set();

// Register message handler
connection.on('ReceiveMessage', (event) => {
    // Client-side deduplication
    if (seenMessages.has(event.messageId)) {
        console.log(`Duplicate skipped: ${event.messageId}`);
        return;
    }
    seenMessages.add(event.messageId);

    // Prune old entries to prevent memory bloat
    if (seenMessages.size > 10000) {
        const oldest = seenMessages.keys().next().value;
        seenMessages.delete(oldest);
    }

    console.log(`[${event.scopeId}] ${event.senderId}: ${event.text}`);
});

// Start connection
await connection.start();

// Join a scope
await connection.invoke('JoinScopeAsync', 'general');

// Send a message
await connection.invoke('SendAsync', {
    scopeType: 0, // Channel
    scopeId: 'general',
    text: 'Hello, Chatify!'
});
```

#### Using Browser DevTools Console

```javascript
// Open browser DevTools console on a page that loads SignalR

// Create connection
const connection = new signalR.HubConnectionBuilder()
    .withUrl('http://localhost:5000/hubs/chat')
    .configureLogging(signalR.LogLevel.Information)
    .build();

// Message handler
connection.on('ReceiveMessage', (event) => {
    console.log('Received:', event);
    // Display in UI...
});

// Start and join
connection.start().then(() => {
    console.log('Connected');
    return connection.invoke('JoinScopeAsync', 'general');
}).then(() => {
    console.log('Joined scope: general');
});
```

### Kafka Topic Testing with kcat (formerly kafkacat)

```bash
# Install: apt-get install kafkacat (Debian/Ubuntu)
# Or: brew install kcat (macOS)

# Consume from chat-events topic
kcat -C -b localhost:9092 -t chat-events -f 'Partition(%p) Offset(%o) Key(%k): %s\n'

# Produce a test message
echo '{"messageId":"123e4567-e89b-12d3-a456-426614174000","scopeType":0,"scopeId":"general","senderId":"test-user","text":"Hello from kcat","createdAtUtc":"2026-01-15T10:30:00Z","originPodId":"test-pod"}' | \
  kcat -P -b localhost:9092 -t chat-events -k general
```

### Verifying Fan-Out Broadcast

To verify the fan-out consumption pattern works across multiple pods:

1. **Deploy multiple ChatApi pods** (e.g., 3 replicas)

2. **Connect to each pod** with a SignalR client and join the same scope:

```javascript
// Client 1 - connects to Pod A
// Client 2 - connects to Pod B
// Client 3 - connects to Pod C

// All join the same scope
await connection.invoke('JoinScopeAsync', 'general');
```

3. **Send a message from any client**:

```javascript
await connection.invoke('SendAsync', {
    scopeType: 0,
    scopeId: 'general',
    text: 'Testing fan-out broadcast!'
});
```

4. **Verify all clients receive the message**, regardless of which pod they're connected to.

5. **Check logs** to see each pod's consumer independently receiving and broadcasting:

```bash
# View logs for a specific pod
kubectl logs -f deployment/chatify-chat-api -c chatify-chat-api | grep "ChatBroadcastBackgroundService"
```

Expected output pattern:
```
ChatBroadcastBackgroundService starting. ConsumerGroupId: chatify-broadcast-chat-api-7d9f4c5b6d-abc12...
Broadcasting chat event xxx to scope general (partition 0, offset 42)...
Successfully broadcasted message xxx to scope general
```

## Deployment

### Prerequisites

- [kind](https://kind.sigs.k8s.io/) (Kubernetes in Docker) v0.20.0 or later
- [kubectl](https://kubernetes.io/docs/tasks/tools/) v1.27.0 or later
- [Docker](https://www.docker.com/) Desktop or Engine

### Local Deployment with kind

Chatify provides Kubernetes manifests optimized for local development using kind. The deployment includes:

- Namespace `chatify`
- ChatApi deployment with 3 replicas
- NodePort service for external access
- ConfigMap with infrastructure endpoints (Kafka, Redis, Scylla, Elasticsearch)
- Health probes (liveness, readiness, startup)
- Pod identity injection via `POD_NAME` environment variable

#### Deployment Architecture

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                          kind Cluster: chatify                              │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  ┌──────────────────────────────────────────────────────────────────────┐  │
│  │                      Namespace: chatify                              │  │
│  │                                                                      │  │
│  │  ┌────────────────────────────────────────────────────────────────┐  │  │
│  │  │  Deployment: chatify-chat-api (replicas: 3)                    │  │
│  │  │  ┌──────────┐  ┌──────────┐  ┌──────────┐                     │  │  │
│  │  │  │  Pod-1   │  │  Pod-2   │  │  Pod-3   │                     │  │  │
│  │  │  │ POD_NAME │  │ POD_NAME │  │ POD_NAME │                     │  │  │
│  │  │  │ injected │  │ injected │  │ injected │                     │  │  │
│  │  │  └────┬─────┘  └────┬─────┘  └────┬─────┘                     │  │  │
│  │  │       │             │             │                            │  │  │
│  │  └───────┼─────────────┼─────────────┼────────────────────────────┘  │  │
│  │          │             │             │                               │  │
│  │          └─────────────┼─────────────┘                               │  │
│  │                        ▼                                            │  │
│  │  ┌────────────────────────────────────────────────────────────────┐  │  │
│  │  │  Service: chatify-chat-api (NodePort: 30080/30443)             │  │  │
│  │  └────────────────────────────────────────────────────────────────┘  │  │
│  │                                                                      │  │
│  │  ┌────────────────────────────────────────────────────────────────┐  │  │
│  │  │  ConfigMap: chatify-chat-api-config                             │  │  │
│  │  │  - Kafka: chatify-kafka:9092                                    │  │  │
│  │  │  - Redis: chatify-redis:6379                                    │  │  │
│  │  │  - Scylla: chatify-scylla:9042                                  │  │  │
│  │  │  - Elastic: http://chatify-elastic:9200                         │  │  │
│  │  └────────────────────────────────────────────────────────────────┘  │  │
│  │                                                                      │  │
│  └──────────────────────────────────────────────────────────────────────┘  │
│                                                                             │
│  Port Mappings (host -> container):                                         │
│  - 8080 -> 30080 (HTTP)                                                     │
│  - 8443 -> 30443 (HTTPS)                                                    │
│  - 9092 -> 30092 (Kafka)                                                    │
│  - 9042 -> 30042 (ScyllaDB)                                                 │
│  - 6379 -> 30079 (Redis)                                                    │
│  - 9200 -> 30020 (Elasticsearch)                                            │
│  - 8081 -> 3080 (AKHQ)                                                      │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

#### Quick Start

1. **Create the kind cluster:**

```bash
kind create cluster --config deploy/kind/kind-cluster.yaml
```

2. **Apply Kubernetes manifests:**

```bash
# Create namespace
kubectl apply -f deploy/k8s/00-namespace.yaml

# Create ConfigMap
kubectl apply -f deploy/k8s/chat-api/10-configmap.yaml

# Create deployment
kubectl apply -f deploy/k8s/chat-api/20-deployment.yaml

# Create service
kubectl apply -f deploy/k8s/chat-api/30-service.yaml
```

3. **Verify deployment:**

```bash
# Check pods
kubectl get pods -n chatify

# Check services
kubectl get svc -n chatify

# Check logs
kubectl logs -f deployment/chatify-chat-api -n chatify
```

4. **Access ChatApi:**

```bash
# Port forward to local port
kubectl port-forward -n chatify svc/chatify-chat-api 8080:80

# Or access via NodePort (from kind)
curl http://localhost:8080/health/live
```

#### Using Deployment Scripts

The `scripts/` directory provides helper scripts for common operations:

```bash
# Bootstrap entire environment
./scripts/up.sh

# Tear down environment
./scripts/down.sh

# Check deployment status
./scripts/status.sh

# View logs
./scripts/logs-chatify.sh

# Port forward services
./scripts/port-forward.sh
```

#### Health Endpoints

ChatApi exposes the following health endpoints:

| Endpoint | Purpose |
|----------|---------|
| `/health/live` | Liveness probe - checks if container is alive |
| `/health/ready` | Readiness probe - checks if container can serve traffic |
| `/health/startup` | Startup probe - checks if application has started |

#### Pod Identity

Each pod receives its identity via the `POD_NAME` environment variable, which is injected using Kubernetes `fieldRef`:

```yaml
env:
  - name: POD_NAME
    valueFrom:
      fieldRef:
        fieldPath: metadata.name
```

This identity is used by `IPodIdentityService` to track message origins across distributed pods.

#### Configuration Reference

The ConfigMap `chatify-chat-api-config` contains all infrastructure endpoints:

| Configuration | Description | Default Value |
|---------------|-------------|---------------|
| `CHATIFY__LOGGING__URI` | Elasticsearch endpoint | `http://chatify-elastic:9200` |
| `CHATIFY__DATABASE__CONTACTPOINTS` | ScyllaDB contact points | `chatify-scylla` |
| `CHATIFY__DATABASE__PORT` | ScyllaDB port | `9042` |
| `CHATIFY__DATABASE__KEYSPACE` | ScyllaDB keyspace | `chatify` |
| `CHATIFY__CACHING__CONNECTIONSTRING` | Redis endpoint | `chatify-redis:6379` |
| `CHATIFY__MESSAGEBROKER__BOOTSTRAPSERVERS` | Kafka bootstrap servers | `chatify-kafka:9092` |
| `CHATIFY__MESSAGEBROKER__TOPICNAME` | Kafka topic for chat events | `chat-events` |
| `CHATIFY__MESSAGEBROKER__PARTITIONS` | Kafka topic partitions | `3` |

#### Infrastructure Services

The following infrastructure services must be deployed before ChatApi:

1. **Kafka** - Message broker for chat events
2. **Redis** - Caching for presence and rate limiting
3. **ScyllaDB** - NoSQL database for chat history
4. **Elasticsearch** - Log aggregation and search
5. **AKHQ** - Kafka management UI

##### Kafka Deployment (Redpanda)

Chatify uses Redpanda as the Kafka-compatible message broker. Redpanda provides full Kafka protocol compatibility with simplified deployment and management.

**Deploy Kafka:**

```bash
# Deploy Kafka StatefulSet
kubectl apply -f deploy/k8s/kafka/10-statefulset.yaml

# Wait for Kafka to be ready
kubectl wait --for=condition=ready pod -l app.kubernetes.io/name=chatify-kafka -n chatify --timeout=120s

# Initialize the chat-events topic
kubectl apply -f deploy/k8s/kafka/20-topic-init-job.yaml

# Verify topic creation
kubectl logs -n chatify job/chatify-kafka-topic-init
```

**Kafka Configuration:**
- **Topic**: `chat-events`
- **Partitions**: 3 (configurable via ConfigMap)
- **Replication Factor**: 1
- **Bootstrap Servers**: `chatify-kafka:9092` (internal), `localhost:9092` (via NodePort)

##### AKHQ Deployment

AKHQ (formerly KafkaHQ) is a Kafka GUI for managing Apache Kafka, Redpanda, and Zookeeper. It provides a web UI for viewing topics, partitions, consumers, and messages.

**Deploy AKHQ:**

```bash
kubectl apply -f deploy/k8s/akhq/10-deployment.yaml

# Wait for AKHQ to be ready
kubectl wait --for=condition=ready pod -l app.kubernetes.io/name=chatify-akhq -n chatify --timeout=120s
```

**Access AKHQ:**

```bash
# Port forward to local port
kubectl port-forward -n chatify svc/chatify-akhq-nodeport 8081:8080

# Or access via NodePort (from kind)
curl http://localhost:8081
```

Open your browser to `http://localhost:8081` to access the AKHQ UI.

##### Verifying Kafka and AKHQ

**1. Verify Kafka is running:**

```bash
kubectl get pods -n chatify -l app.kubernetes.io/name=chatify-kafka
kubectl get svc -n chatify -l app.kubernetes.io/name=chatify-kafka
```

**2. Verify topic creation via AKHQ:**

In the AKHQ UI:
1. Navigate to the **chatify-kafka** connection
2. Click on **Topics** in the left sidebar
3. Verify the `chat-events` topic exists with 3 partitions
4. Click on the topic to view partition details and consumer groups

**3. Produce a test message via AKHQ:**

In the AKHQ UI:
1. Navigate to **Topics** -> **chat-events**
2. Click the **Produce** button
3. Enter a key (e.g., `general`) and value (JSON):
   ```json
   {
     "messageId": "123e4567-e89b-12d3-a456-426614174000",
     "scopeType": 0,
     "scopeId": "general",
     "senderId": "test-user",
     "text": "Hello from AKHQ!",
     "createdAtUtc": "2026-01-16T10:30:00Z",
     "originPodId": "akhq-test"
   }
   ```
4. Click **Produce** to send the message

**4. Verify message in AKHQ:**

In the AKHQ UI:
1. Navigate to **Topics** -> **chat-events**
2. Click the **Messages** tab
3. View the message in partition 0 (or based on key routing)
4. Expand the message to view full JSON content, key, timestamp, offset, and partition

**5. Monitor consumer groups:**

In the AKHQ UI:
1. Navigate to **Consumers** in the left sidebar
2. View active consumer groups:
   - `chatify-chat-history-writer` - Chat history persistence
   - `chatify-broadcast-*` - Broadcast consumers for each API pod
3. Click on a consumer group to view:
   - Lag (messages pending consumption)
   - Offset positions per partition
   - Member assignments

**6. Verify topic partitions:**

In the AKHQ UI:
1. Navigate to **Topics** -> **chat-events**
2. View the **Partitions** tab showing:
   - Partition 0, 1, 2
   - Replication factor
   - In-sync replica count
   - Total messages per partition

##### External Kafka Access

To access Kafka from the host machine for testing:

```bash
# Via kcat (kafkacat)
kcat -C -b localhost:9092 -t chat-events -f 'Partition(%p) Offset(%o) Key(%k): %s\n'

# Or produce a test message
echo '{"messageId":"test","scopeType":0,"scopeId":"general","senderId":"test","text":"Hello","createdAtUtc":"2026-01-16T00:00:00Z","originPodId":"test"}' | \
  kcat -P -b localhost:9092 -t chat-events -k general
```

Note: Port mapping `9092 -> 30092` is configured in `deploy/kind/kind-cluster.yaml`.

#### Cleanup

```bash
# Delete resources
kubectl delete namespace chatify

# Or delete entire kind cluster
kind delete cluster --name chatify
```

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
