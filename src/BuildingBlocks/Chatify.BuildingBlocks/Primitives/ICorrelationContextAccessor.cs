namespace Chatify.BuildingBlocks.Primitives;

/// <summary>
/// Defines a contract for accessing and modifying the correlation context for the current execution flow.
/// </summary>
/// <remarks>
/// <para>
/// The correlation context stores a correlation ID that uniquely identifies a request, operation,
/// or transaction across distributed systems. This ID flows through the entire call stack and
/// across service boundaries, enabling traceability and debugging.
/// </para>
/// <para>
/// Implementations must use <see cref="System.Threading.AsyncLocal{T}"/> to store the correlation ID,
/// ensuring that the context flows correctly across asynchronous operations without leaking between
/// concurrent requests. Each async execution context maintains its own isolated correlation ID.
/// </para>
/// <para>
/// The correlation ID should be:
/// <list type="bullet">
///   <item>Generated at the entry point of a request (API, message handler, background job)</item>
///   <item>Propagated to downstream services via HTTP headers, message metadata, or gRPC metadata</item>
///   <item>Included in all log entries for that request</item>
///   <item>Passed to database queries (via application-level context or database features)</item>
/// </list>
/// </para>
/// </remarks>
public interface ICorrelationContextAccessor
{
    /// <summary>
    /// Gets or sets the correlation ID for the current async execution context.
    /// </summary>
    /// <value>
    /// A string containing the correlation ID, or <c>null</c> if no correlation ID has been set.
    /// </value>
    /// <remarks>
    /// <para>
    /// When getting the correlation ID, implementations must return the value associated with
    /// the current async context. If no correlation ID has been set for the current context,
    /// implementations should return <c>null</c> rather than throwing an exception.
    /// </para>
    /// <para>
    /// When setting the correlation ID, implementations must store the value in the current
    /// async context without affecting other concurrent or parent contexts. Setting this property
    /// to <c>null</c> should clear the correlation ID from the current context.
    /// </para>
    /// <para>
    /// The setter must be thread-safe for operations within the same async context, but different
    /// async contexts must have isolated values. This is typically achieved using
    /// <see cref="System.Threading.AsyncLocal{T}"/>.
    /// </para>
    /// </remarks>
    string? CorrelationId { get; set; }
}
