using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Chatify.BuildingBlocks.Primitives;

/// <summary>
/// Provides structured logging services with contextual information and correlation ID support.
/// </summary>
/// <remarks>
/// <para>
/// <b>Purpose:</b> This service implements <see cref="ILogService"/> to provide a simplified,
/// application-level abstraction over Serilog for structured logging. It bridges the gap
/// between the generic <see cref="ILogger"/> interface and the specific needs of the Chatify
/// application for contextual, correlation-aware logging.
/// </para>
/// <para>
/// <b>Design Philosophy:</b> The service is deliberately simple to encourage consistent
/// logging patterns across the codebase. It handles the complexity of correlation ID propagation,
/// context serialization, and log level mapping internally.
/// </para>
/// <para>
/// <b>Correlation ID Integration:</b> The service automatically includes the correlation ID
/// from <see cref="ICorrelationContextAccessor"/> in all log entries. This enables distributed
/// tracing without requiring manual propagation of correlation IDs throughout the codebase.
/// </para>
/// <para>
/// <b>Structured Logging:</b> Context objects are serialized and included in log entries as
/// structured properties using Serilog's built-in support for object destructuring. This
/// enables powerful querying and filtering in Elasticsearch.
/// </para>
/// <para>
/// <b>Service Lifetime:</b> Should be registered as a scoped service in the DI container to
/// ensure correlation context is correctly propagated within a request scope. However, it can
/// also be registered as a singleton if used in background services or singletons.
/// </para>
/// <para>
/// <b>Thread Safety:</b> This implementation is thread-safe. Multiple threads can safely
/// call logging methods concurrently. Each call independently retrieves the current
/// correlation ID and writes to the underlying logger.
/// </para>
/// <para>
/// <b>Constructor Dependencies:</b>
/// <list type="bullet">
/// <item><see cref="ILogger"/> - The underlying logger for writing log entries</item>
/// <item><see cref="ICorrelationContextAccessor"/> - Accessor for correlation ID from async-local storage</item>
/// </list>
/// </para>
/// </remarks>
/// <param name="logger">
/// The underlying <see cref="ILogger"/> instance used to write log entries.
/// Injected via dependency injection.
/// </param>
/// <param name="correlationContextAccessor">
/// The accessor for the current correlation context. Used to include correlation IDs
/// in log entries for distributed tracing. Injected via dependency injection.
/// </param>
public sealed class LogService(ILogger<LogService> logger, ICorrelationContextAccessor correlationContextAccessor) : ILogService
{
    /// <summary>
    /// Logs an informational message with optional contextual data.
    /// </summary>

    /// <summary>
    /// Logs an informational message with optional contextual data.
    /// </summary>
    /// <param name="message">
    /// The informational message to log. Must not be null or whitespace.
    /// </param>
    /// <param name="context">
    /// An optional object containing contextual data to include in the log entry.
    /// </param>
    /// <inheritdoc cref="ILogService.Info(string, object?)"/>
    public void Info(string message, object? context = null)
    {
        GuardUtility.NotNull(message);

        var correlationId = correlationContextAccessor.CorrelationId;

        if (context is null)
        {
            logger.LogInformation(
                "Message: {Message}, CorrelationId: {CorrelationId}",
                message,
                correlationId);
        }
        else
        {
            logger.LogInformation(
                "Message: {Message}, CorrelationId: {CorrelationId}, Context: {@Context}",
                message,
                correlationId,
                context);
        }
    }

    /// <summary>
    /// Logs a warning message with optional contextual data.
    /// </summary>
    /// <param name="message">
    /// The warning message to log. Must not be null or whitespace.
    /// </param>
    /// <param name="context">
    /// An optional object containing contextual data to include in the log entry.
    /// </param>
    /// <inheritdoc cref="ILogService.Warn(string, object?)"/>
    public void Warn(string message, object? context = null)
    {
        GuardUtility.NotNull(message);

        var correlationId = correlationContextAccessor.CorrelationId;

        if (context is null)
        {
            logger.LogWarning(
                "Message: {Message}, CorrelationId: {CorrelationId}",
                message,
                correlationId);
        }
        else
        {
            logger.LogWarning(
                "Message: {Message}, CorrelationId: {CorrelationId}, Context: {@Context}",
                message,
                correlationId,
                context);
        }
    }

    /// <summary>
    /// Logs an error message with exception details and optional contextual data.
    /// </summary>
    /// <param name="exception">
    /// The exception that caused the error. Must not be null.
    /// </param>
    /// <param name="message">
    /// A descriptive message explaining the error context or what operation failed.
    /// Must not be null or whitespace.
    /// </param>
    /// <param name="context">
    /// An optional object containing contextual data to include in the log entry.
    /// </param>
    /// <inheritdoc cref="ILogService.Error(Exception, string, object?)"/>
    public void Error(Exception exception, string message, object? context = null)
    {
        GuardUtility.NotNull(exception);
        GuardUtility.NotNull(message);

        var correlationId = correlationContextAccessor.CorrelationId;

        if (context is null)
        {
            logger.LogError(
                exception,
                "Message: {Message}, CorrelationId: {CorrelationId}",
                message,
                correlationId);
        }
        else
        {
            logger.LogError(
                exception,
                "Message: {Message}, CorrelationId: {CorrelationId}, Context: {@Context}",
                message,
                correlationId,
                context);
        }
    }
}
