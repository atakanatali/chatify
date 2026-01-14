namespace Chatify.BuildingBlocks.Primitives;

/// <summary>
/// Provides access to the correlation context using <see cref="System.Threading.AsyncLocal{T}"/>
/// to maintain isolation across asynchronous execution contexts.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="CorrelationContextAccessor"/> is the default implementation of
/// <see cref="ICorrelationContextAccessor"/>, designed to store correlation IDs in a way
/// that automatically flows with asynchronous operations while remaining isolated between
/// concurrent requests.
/// </para>
/// <para>
/// <b>AsyncLocal Behavior:</b><br/>
/// <see cref="System.Threading.AsyncLocal{T}"/> stores data that flows with the async execution
/// context. When code awaits an async operation, the async context is captured and restored
/// when the operation completes. This means:
/// <list type="bullet">
///   <item>The correlation ID is automatically available in continuations (after await)</item>
///   <item>Parallel tasks get a copy of the parent's value, which they can modify independently</item>
///   <item>Each request/thread gets its own isolated storage</item>
///   <item>Changes made in a child context do not affect the parent context</item>
/// </list>
/// </para>
/// <para>
/// <b>Usage in Chatify:</b><br/>
/// This accessor is registered as a singleton in the DI container. Middleware components
/// extract or generate correlation IDs and store them here. Services throughout the application
/// can then access the current correlation ID without explicit parameter passing.
/// </para>
/// <para>
/// Example usage in middleware:
/// <code><![CDATA[
/// public class CorrelationMiddleware
/// {
///     private readonly ICorrelationContextAccessor _correlationAccessor;
///
///     public async Task InvokeAsync(HttpContext context)
///     {
///         var correlationId = context.Request.Headers["X-Correlation-ID"].FirstOrDefault();
///         if (string.IsNullOrEmpty(correlationId))
///         {
///             correlationId = CorrelationIdUtility.Generate();
///         }
///         _correlationAccessor.CorrelationId = correlationId;
///
///         await _next(context);
///     }
/// }
/// ]]></code>
/// </para>
/// <para>
/// Example usage in services:
/// <code><![CDATA[
/// public class UserService
/// {
///     private readonly ICorrelationContextAccessor _correlationAccessor;
///     private readonly ILogger<UserService> _logger;
///
///     public void CreateUser(UserDto user)
///     {
///         _logger.LogInformation("Creating user with CorrelationId: {CorrelationId}",
///             _correlationAccessor.CorrelationId);
///         // Business logic...
///     }
/// }
/// ]]></code>
/// </para>
/// </remarks>
public sealed class CorrelationContextAccessor : ICorrelationContextAccessor
{
    /// <summary>
    /// The async-local storage slot that holds the correlation ID for the current execution context.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This field uses <see cref="System.Threading.AsyncLocal{T}"/> to provide storage that
    /// is unique to each asynchronous execution context. Each async context gets its own
    /// isolated copy of this value, ensuring thread safety without explicit locking.
    /// </para>
    /// <para>
    /// When a new task is created, it inherits the value from its parent context. However,
    /// modifications made in the child context do not propagate back to the parent.
    /// This behavior is ideal for correlation IDs, which should flow downstream but
    /// not be affected by downstream modifications.
    /// </para>
    /// <para>
    /// The <see cref="System.Threading.AsyncLocal{T}"/> instance itself is static and shared
    /// across all execution contexts, but the value stored within it is context-specific.
    /// </para>
    /// </remarks>
    private static readonly AsyncLocal<string?> _asyncLocalCorrelationId = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="CorrelationContextAccessor"/> class.
    /// </summary>
    /// <remarks>
    /// This constructor is parameterless to support singleton registration in the DI container.
    /// The class maintains all state in static <see cref="System.Threading.AsyncLocal{T}"/>
    /// storage, so instance methods operate on the current async context regardless of which
    /// instance is used.
    /// </remarks>
    public CorrelationContextAccessor()
    {
    }

    /// <summary>
    /// Gets or sets the correlation ID for the current async execution context.
    /// </summary>
    /// <value>
    /// A string containing the correlation ID, or <c>null</c> if no correlation ID has been set.
    /// </value>
    /// <remarks>
    /// <para>
    /// The getter retrieves the value from <see cref="System.Threading.AsyncLocal{T}"/> storage
    /// for the current async context. This value represents the correlation ID set by middleware
    /// or application code for the current request/operation.
    /// </para>
    /// <para>
    /// If no correlation ID has been set for the current context, this property returns <c>null</c>.
    /// Callers should handle the null case appropriately, typically by generating a new correlation ID
    /// or using a default value.
    /// </para>
    /// <para>
    /// The value is automatically preserved across await points, so code running after an await
    /// will see the same correlation ID as before the await, unless explicitly changed.
    /// </para>
    /// <para>
    /// The setter stores the provided value in <see cref="System.Threading.AsyncLocal{T}"/>
    /// storage for the current async context. This operation affects only the current context;
    /// parent contexts and concurrent contexts are not modified.
    /// </para>
    /// <para>
    /// Setting to <c>null</c> clears the correlation ID from the current context.
    /// </para>
    /// <para>
    /// <b>Important:</b> Changes made to the correlation ID in a child context (such as
    /// within a Task.Run delegate or a parallel branch) do not affect the parent context.
    /// When the child context completes, the parent context retains its original value.
    /// This is the intended behavior of <see cref="System.Threading.AsyncLocal{T}"/>.
    /// </para>
    /// </remarks>
    public string? CorrelationId
    {
        get => _asyncLocalCorrelationId.Value;
        set => _asyncLocalCorrelationId.Value = value;
    }
}
