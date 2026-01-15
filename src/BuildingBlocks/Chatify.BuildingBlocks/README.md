# Chatify BuildingBlocks

## Overview
BuildingBlocks contains the shared kernel utilities and cross-cutting abstractions used throughout the Chatify modular monolith. This module implements fundamental primitives that are domain-agnostic and used across all layers and modules.

## Architecture

### Design Principles
The BuildingBlocks module follows the "Shared Kernel" concept from Domain-Driven Design:
- Contains elements that other modules depend on but don't change frequently
- Implements cross-cutting concerns that are fundamental to the system
- Maintains no dependencies on other Chatify modules
- Provides stable, versioned interfaces for the entire application

### Organization

```
Chatify.BuildingBlocks/
├── Primitives/                    # Core abstractions and utilities
│   ├── ILogService.cs            # Application-level logging interface
│   ├── LogService.cs             # Serilog-based logging implementation
│   ├── LoggingOptionsEntity.cs   # Elasticsearch logging configuration
│   ├── IClockService.cs          # System clock abstraction
│   ├── SystemClockService.cs     # UTC clock implementation
│   ├── ICorrelationContextAccessor.cs  # Distributed tracing context
│   ├── CorrelationContextAccessor.cs   # Async-local correlation storage
│   ├── CorrelationIdUtility.cs   # Correlation ID generation
│   ├── GuardUtility.cs           # Input validation utilities
│   ├── ErrorEntity.cs            # Error representation
│   └── ResultEntity.cs           # Operation result pattern
└── DependencyInjection/           # DI extension methods
    └── ServiceCollectionLoggingExtensions.cs  # Logging registration
```

## Components

### Logging Primitives

#### ILogService
Application-level logging abstraction with correlation ID support and structured logging capabilities.

**Interface:**
```csharp
public interface ILogService
{
    void Info(string message, object? context = null);
    void Warn(string message, object? context = null);
    void Error(Exception exception, string message, object? context = null);
}
```

**Usage:**
```csharp
// Register in DI (Program.cs)
services.AddLogging(Configuration);

// Use in services
public class ChatMessageHandler
{
    private readonly ILogService _logService;

    public ChatMessageHandler(ILogService logService)
    {
        _logService = logService;
    }

    public async Task HandleAsync(ChatMessage message)
    {
        _logService.Info("Processing chat message", new { MessageId = message.Id });

        try
        {
            // Process message
            _logService.Info("Chat message processed successfully", new { MessageId = message.Id });
        }
        catch (Exception ex)
        {
            _logService.Error(ex, "Failed to process chat message", new { MessageId = message.Id });
            throw;
        }
    }
}
```

#### LoggingOptionsEntity
Configuration options for Elasticsearch logging integration.

**Properties:**
- `Uri` - Elasticsearch cluster endpoint
- `Username` - Authentication username (optional)
- `Password` - Authentication password (optional)
- `IndexPrefix` - Index name prefix (e.g., "logs-chatify-chatapi")

**Configuration:**
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

### Time Primitives

#### IClockService
Abstraction over system time for testing and time zone handling.

**Interface:**
```csharp
public interface IClockService
{
    DateTimeOffset GetUtcNow();
}
```

**Usage:**
```csharp
// Register in DI
services.AddSingleton<IClockService, SystemClockService>();

// Use in domain logic
public class ChatMessage
{
    public ChatMessage(IClockService clockService)
    {
        CreatedAtUtc = clockService.GetUtcNow();
    }
}
```

### Correlation Primitives

#### ICorrelationContextAccessor
Provides async-local storage for correlation IDs in distributed tracing scenarios.

**Interface:**
```csharp
public interface ICorrelationContextAccessor
{
    string? CorrelationId { get; set; }
}
```

**Usage:**
```csharp
// Register in DI
services.AddSingleton<ICorrelationContextAccessor, CorrelationContextAccessor>();

// Middleware sets correlation ID
public class CorrelationIdMiddleware
{
    public async Task InvokeAsync(HttpContext context, ICorrelationContextAccessor correlationAccessor)
    {
        correlationAccessor.CorrelationId = context.Request.Headers["X-Correlation-ID"].ToString();
        // Process request
    }
}

// Services access correlation ID automatically
public class ChatService
{
    private readonly ILogService _logService;
    private readonly ICorrelationContextAccessor _correlationAccessor;

    public ChatService(ILogService logService, ICorrelationContextAccessor correlationAccessor)
    {
        _logService = logService;
        _correlationAccessor = correlationAccessor;
    }

    public void ProcessMessage()
    {
        // Correlation ID is automatically included in logs
        _logService.Info("Processing message");
    }
}
```

#### CorrelationIdUtility
Utility for generating and validating correlation IDs.

**Methods:**
- `CreateId()` - Generates a new GUID-based correlation ID
- `IsValid(string? correlationId)` - Validates correlation ID format

### Validation Primitives

#### GuardUtility
Input validation utilities for defensive programming.

**Methods:**
- `NotNull(object? value)` - Throws if value is null
- `NotNullOrEmpty(string? value)` - Throws if string is null or empty
- `NotNullOrWhiteSpace(string? value)` - Throws if string is null or whitespace

### Error Handling Primitives

#### ErrorEntity
Represents an error with code, message, and details.

#### ResultEntity
Operation result pattern for returning success/failure with optional value or error.

**Usage:**
```csharp
public ResultEntity<ChatMessage> ProcessMessage(SendMessageRequest request)
{
    // Validate
    if (string.IsNullOrWhiteSpace(request.Text))
    {
        return ResultEntity<ChatMessage>.Failure(
            ErrorEntity.Validation("Text is required"));
    }

    // Process
    var message = new ChatMessage(request.Text);

    // Return success
    return ResultEntity<ChatMessage>.Success(message);
}
```

## Dependency Injection

### ServiceCollection Extensions

#### AddLogging
Registers `ILogService` and `LoggingOptionsEntity` with the DI container.

```csharp
services.AddLogging(Configuration);
```

**Registered Services:**
- `ILogService` as `LogService` (scoped)
- `LoggingOptionsEntity` (singleton)

## Best Practices

### When to Use BuildingBlocks

**Use BuildingBlocks for:**
- Cross-cutting concerns (logging, time, correlation)
- Fundamental abstractions used across modules
- Domain-agnostic utilities and primitives
- Stable interfaces that change infrequently

**Don't Use BuildingBlocks for:**
- Domain-specific logic (belongs in Domain layer)
- Business rules (belongs in Application layer)
- Infrastructure integrations (belongs in Infrastructure layer)
- Module-specific features (belongs in specific module)

### Adding New Primitives

When adding new primitives to BuildingBlocks:

1. **Evaluate Necessity**: Is the abstraction truly used across multiple modules?
2. **Define Interface**: Create a clean, focused interface with XML documentation
3. **Implement**: Provide a production-ready implementation
4. **Document**: Add comprehensive XML comments and usage examples
5. **Register**: Create DI extension method for service registration
6. **Test**: Write unit tests for the primitive

## Naming Conventions

- Interfaces: `I{Name}Service` or `I{Name}Accessor`
- Implementations: `{Name}Service` or `{Name}Accessor`
- Options: `{Name}OptionsEntity`
- Utilities: `{Name}Utility`
- Errors: `{Name}Entity` or `{Name}Result`

## See Also
- [Main README](../../../README.md) - Overall project documentation
- [Observability Module](../Observability/README.md) - Observability features
