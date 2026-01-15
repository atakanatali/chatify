namespace Chatify.BuildingBlocks.Primitives;

/// <summary>
/// Defines a contract for structured logging operations with contextual information.
/// </summary>
/// <remarks>
/// <para>
/// <b>Purpose:</b> This interface provides a simplified, application-level abstraction
/// over Serilog for structured logging. It encapsulates the complexity of log levels,
/// enrichment, and contextual data while maintaining clean architecture principles.
/// </para>
/// <para>
/// <b>Design Philosophy:</b> The interface is intentionally minimal with only three
/// methods (Info, Warn, Error) to cover the most common logging scenarios. Additional
/// complexity can be added through the optional context parameter which accepts any
/// object for structured logging.
/// </para>
/// <para>
/// <b>Structured Logging:</b> All methods accept an optional context parameter
/// that can be any object (anonymous type, DTO, entity, etc.). The context
/// object is serialized and included in the log entry as structured properties, enabling
/// powerful querying and filtering in log aggregation systems like Elasticsearch.
/// </para>
/// <para>
/// <b>Correlation ID Integration:</b> Implementations must automatically include the
/// correlation ID from <see cref="ICorrelationContextAccessor"/> in all log entries,
/// ensuring distributed traceability without requiring manual propagation.
/// </para>
/// <para>
/// <b>Usage Examples:</b>
/// <code><![CDATA[
/// // Simple info log
/// _logService.Info("User logged in");
///
/// // Info log with context
/// _logService.Info("Order created", new { OrderId = 123, CustomerId = 456 });
///
/// // Warning with context
/// _logService.Warn("High memory usage detected", new { UsagePercent = 85, Threshold = 80 });
///
/// // Error log with exception
/// _logService.Error(ex, "Failed to process payment", new { OrderId = 123, Amount = 99.99m });
/// ]]></code>
/// </para>
/// <para>
/// <b>When to Use:</b>
/// <list type="bullet">
/// <item>Application-level logging in command handlers, application services, and domain services</item>
/// <item>Business operation logging (e.g., "Order created", "Payment processed")</item>
/// <item>Integration point logging (e.g., "Kafka message sent", "Database query executed")</item>
/// <item>Error logging with business context</item>
/// </list>
/// </para>
/// <para>
/// <b>When NOT to Use:</b>
/// <list type="bullet">
/// <item>Low-level framework logging (use <see cref="Microsoft.Extensions.Logging.ILogger"/> directly)</item>
/// <item>Performance-critical paths with high-frequency logging (use ILogger for efficiency)</item>
/// <item>Third-party library integration (use their native logging abstractions)</item>
/// </list>
/// </para>
/// </remarks>
public interface ILogService
{
    /// <summary>
    /// Logs an informational message with optional contextual data.
    /// </summary>
    /// <param name="message">
    /// The informational message to log. Must not be null or whitespace.
    /// </param>
    /// <param name="context">
    /// An optional object containing contextual data to include in the log entry.
    /// The object is serialized and included as structured properties in the log.
    /// Can be an anonymous type, DTO, entity, or any serializable object.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>Log Level:</b> Information - Used for general informational messages that
    /// track normal application flow and significant business operations.
    /// </para>
    /// <para>
    /// <b>Use Cases:</b>
    /// <list type="bullet">
    /// <item>Business operation completion (e.g., "Order created", "User registered")</item>
    /// <item>State transitions (e.g., "Order status changed from Pending to Shipped")</item>
    /// <item>Integration calls (e.g., "Kafka message published", "Email sent")</item>
    /// <item>Application lifecycle events (e.g., "Application started", "Cache refreshed")</item>
    /// </list>
/// </para>
    /// <para>
    /// <b>Best Practices:</b>
    /// <list type="bullet">
    /// <item>Use clear, descriptive messages that describe what happened (not how)</item>
    /// <item>Include relevant IDs and business identifiers in the context object</item>
    /// <item>Avoid logging sensitive data (passwords, tokens, personal information)</item>
    /// <item>Use structured context for easier querying (e.g., <c>new { OrderId = 123 }</c>)</item>
    /// </list>
    /// </para>
    /// </remarks>
    void Info(string message, object? context = null);

    /// <summary>
    /// Logs a warning message with optional contextual data.
    /// </summary>
    /// <param name="message">
    /// The warning message to log. Must not be null or whitespace.
    /// </param>
    /// <param name="context">
    /// An optional object containing contextual data to include in the log entry.
    /// The object is serialized and included as structured properties in the log.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>Log Level:</b> Warning - Used for potentially harmful situations or
    /// important events that do not prevent the application from functioning but
    /// may require attention.
    /// </para>
    /// <para>
    /// <b>Use Cases:</b>
    /// <list type="bullet">
    /// <item>Approaching limits (e.g., "Cache hit rate below 80%")</item>
    /// <item>Degraded functionality (e.g., "External API slow, using fallback")</item>
    /// <item>Configuration issues (e.g., "Feature flag disabled, using default")</item>
    /// <item>Business rule violations (e.g., "Order placed with expired discount code")</item>
    /// <item>Retry attempts (e.g., "Database query failed, retrying (attempt 1/3)")</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Best Practices:</b>
    /// <list type="bullet">
    /// <item>Log warnings for situations that may require investigation but don't need immediate action</item>
    /// <item>Include actionable information in the context (e.g., threshold values, current state)</item>
    /// <item>Avoid warning spam - use throttling or sampling for high-frequency events</item>
    /// <item>Consider if the situation should be an error instead (data loss, security issue)</item>
    /// </list>
    /// </para>
    /// </remarks>
    void Warn(string message, object? context = null);

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
    /// The object is serialized and included as structured properties in the log.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>Log Level:</b> Error - Used for error events that might still allow the
    /// application to continue running. Fatal errors that cause the application
    /// to terminate should also be logged at this level before termination.
    /// </para>
    /// <para>
    /// <b>Use Cases:</b>
    /// <list type="bullet">
    /// <item>Failed operations (e.g., "Database connection failed", "External API call failed")</item>
    /// <item>Validation failures (e.g., "Message validation failed")</item>
    /// <item>Resource exhaustion (e.g., "Out of memory", "Connection pool exhausted")</item>
    /// <item>Business logic failures (e.g., "Payment declined", "Inventory not available")</item>
    /// <item>Unhandled exceptions (typically logged in middleware before returning error response)</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Exception Handling:</b> The exception parameter is required for error logs to ensure
    /// stack traces and inner exceptions are captured. The message should describe the
    /// business context of the error, not repeat the exception message.
    /// </para>
    /// <para>
    /// <b>Best Practices:</b>
    /// <list type="bullet">
    /// <item>Include business identifiers in context (e.g., OrderId, UserId, RequestId)</item>
    /// <item>Describe what operation failed, not just that it failed</item>
    /// <item>Include retry information if applicable (e.g., "Attempt 2 of 3")</item>
    /// <item>Log the full exception including inner exceptions (handled automatically)</item>
    /// <item>Consider if a warning is more appropriate for expected failures (e.g., validation errors)</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Example:</b>
    /// <code><![CDATA[
    /// try
    /// {
    ///     await _paymentService.ProcessPaymentAsync(order);
    /// }
    /// catch (PaymentGatewayException ex)
    /// {
    ///     _logService.Error(ex, "Failed to process payment", new { OrderId = order.Id, Amount = order.Total });
    ///     throw;
    /// }
    /// ]]></code>
    /// </para>
    /// </remarks>
    void Error(Exception exception, string message, object? context = null);
}
