# Chatify Architecture

## Table of Contents
- [Overview](#overview)
- [Solution Structure](#solution-structure)
- [Modules](#modules)
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
