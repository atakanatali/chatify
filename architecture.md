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
