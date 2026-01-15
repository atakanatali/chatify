using Chatify.BuildingBlocks.Primitives;

namespace Chatify.Api.Middleware;

/// <summary>
/// Middleware that ensures a correlation ID exists for each HTTP request and
/// makes it available throughout the application via the correlation context accessor.
/// </summary>
/// <remarks>
/// <para>
/// <b>Purpose:</b> This middleware implements distributed tracing by ensuring every
/// request has a correlation ID that flows through the entire call stack. This ID
/// enables tracking requests across service boundaries, debugging distributed issues,
/// and correlating log entries from different services.
/// </para>
/// <para>
/// <b>Correlation ID Flow:</b>
/// <list type="number">
/// <item>Check if client provided X-Correlation-ID header</item>
/// <item>If present, validate and use the client-provided value</item>
/// <item>If absent or invalid, generate a new correlation ID</item>
/// <item>Store the correlation ID in <see cref="ICorrelationContextAccessor"/></item>
/// <item>Add the correlation ID to the response headers</item>
/// </list>
/// </para>
/// <para>
/// <b>Header Format:</b> The middleware looks for the <c>X-Correlation-ID</c> HTTP header.
/// If provided by the client, it must be a valid correlation ID in the format
/// <c>corr_{guid}</c>. Invalid values are ignored and a new ID is generated.
/// </para>
/// <para>
/// <b>Client Behavior:</b> Clients should include the X-Correlation-ID header when
/// making requests, especially when calling Chatify from other services. If the
/// client doesn't provide one, the middleware generates a new one and returns it
/// in the response headers, which the client can use for subsequent requests.
/// </para>
/// <para>
/// <b>Async Context Flow:</b> The correlation ID is stored in an <see cref="AsyncLocal{T}"/>
/// context via <see cref="ICorrelationContextAccessor"/>, ensuring it automatically
/// flows with asynchronous operations without explicit parameter passing.
/// </para>
/// <para>
/// <b>Middleware Position:</b> This should be registered early in the middleware pipeline,
/// before any other middleware that might log or use the correlation ID.
/// </para>
/// <para>
/// Example header usage:
/// <code><![CDATA[
/// curl -H "X-Correlation-ID: corr_a1b2c3d4-e5f6-7890-abcd-ef1234567890" \
///      -H "Content-Type: application/json" \
///      -d '{"text":"Hello"}' \
///      https://api.chatify.com/chat/send
/// ]]></code>
/// </para>
/// </remarks>
public sealed class CorrelationIdMiddleware
{
    /// <summary>
    /// The HTTP header name used to transmit the correlation ID between clients and the server.
    /// </summary>
    /// <remarks>
    /// This constant defines the header name that clients should use to provide
    /// a correlation ID, and that the middleware uses to return the correlation ID
    /// in the response. The header name "X-Correlation-ID" is a common convention
    /// for distributed tracing headers.
    /// </remarks>
    public const string CorrelationIdHeaderName = "X-Correlation-ID";

    /// <summary>
    /// The delegate representing the next middleware in the request pipeline.
    /// </summary>
    /// <remarks>
    /// This delegate is called by the <see cref="InvokeAsync"/> method to pass
    /// control to the next middleware in the pipeline. If this is the last
    /// middleware, the delegate represents the actual request handler.
    /// </remarks>
    private readonly RequestDelegate _next;

    /// <summary>
    /// The accessor used to store and retrieve the correlation ID for the current
    /// async execution context.
    /// </summary>
    /// <remarks>
    /// This accessor is injected via dependency injection and is registered as
    /// a singleton. The middleware uses it to make the correlation ID available
    /// to services throughout the application without explicit parameter passing.
    /// </remarks>
    private readonly ICorrelationContextAccessor _correlationContextAccessor;

    /// <summary>
    /// Initializes a new instance of the <see cref="CorrelationIdMiddleware"/> class.
    /// </summary>
    /// <param name="next">
    /// The delegate representing the next middleware in the ASP.NET Core request pipeline.
    /// Must not be null.
    /// </param>
    /// <param name="correlationContextAccessor">
    /// The accessor used to store the correlation ID in the async context.
    /// Must not be null.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="next"/> or <paramref name="correlationContextAccessor"/> is null.
    /// </exception>
    /// <remarks>
    /// <para>
    /// The constructor receives the next middleware delegate via dependency injection.
    /// ASP.NET Core automatically provides this delegate when the middleware is
    /// instantiated.
    /// </para>
    /// <para>
    /// The correlation context accessor is also injected, allowing the middleware
    /// to store the correlation ID in a way that flows with async operations.
    /// </para>
    /// </remarks>
    public CorrelationIdMiddleware(
        RequestDelegate next,
        ICorrelationContextAccessor correlationContextAccessor)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _correlationContextAccessor = correlationContextAccessor ?? throw new ArgumentNullException(nameof(correlationContextAccessor));
    }

    /// <summary>
    /// Processes an HTTP request to ensure a correlation ID exists and is available
    /// throughout the application.
    /// </summary>
    /// <param name="context">
    /// The HTTP context for the current request. Contains the request and response
    /// objects, headers, and other request-related data.
    /// </param>
    /// <returns>
    /// A <see cref="Task"/> representing the asynchronous operation.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="context"/> is null.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>Processing Flow:</b>
    /// <list type="number">
    /// <item>Extract correlation ID from X-Correlation-ID header (if present)</item>
    /// <item>Validate the provided correlation ID format</item>
    /// <item>If missing or invalid, generate a new correlation ID</item>
    /// <item>Store the correlation ID in the async context via <see cref="ICorrelationContextAccessor"/></item>
    /// <item>Call the next middleware in the pipeline</item>
    /// <item>Add the correlation ID to the response headers</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Header Handling:</b> The middleware accepts correlation IDs from clients
    /// via the X-Correlation-ID header. This enables distributed tracing across
    /// multiple services. If a client provides an invalid format, a new ID is
    /// generated to maintain system integrity.
    /// </para>
    /// <para>
    /// <b>Response Headers:</b> The correlation ID is always added to response headers,
    /// ensuring clients can correlate their requests with server-side logs, even
    /// when they didn't provide an initial correlation ID.
    /// </para>
    /// <para>
    /// <b>Idempotency:</b> Calling this middleware multiple times for the same request
    /// is safe. If a correlation ID is already set in the context, it will be
    /// preserved rather than regenerated.
    /// </para>
    /// </remarks>
    public Task InvokeAsync(HttpContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        // Extract correlation ID from request headers
        var correlationId = GetCorrelationIdFromRequest(context);

        // Store in async context for access throughout the application
        _correlationContextAccessor.CorrelationId = correlationId;

        // Add to response headers so clients can correlate their requests
        context.Response.OnStarting(() =>
        {
            context.Response.Headers.Append(CorrelationIdHeaderName, correlationId);
            return Task.CompletedTask;
        });

        // Call the next middleware in the pipeline
        return _next(context);
    }

    /// <summary>
    /// Extracts or generates a correlation ID from the current HTTP request.
    /// </summary>
    /// <param name="context">
    /// The HTTP context containing the request headers to inspect.
    /// </param>
    /// <returns>
    /// A valid correlation ID string in the format <c>corr_{guid}</c>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This method implements the following logic:
    /// <list type="number">
    /// <item>Check if X-Correlation-ID header exists in the request</item>
    /// <item>If it exists, validate the format using <see cref="CorrelationIdUtility.IsValid"/></item>
    /// <item>If valid, use the client-provided correlation ID (normalized to lowercase)</item>
    /// <item>If missing or invalid, generate a new correlation ID</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Validation:</b> Client-provided correlation IDs must match the format
    /// <c>corr_{guid}</c> to be accepted. Invalid formats are rejected to ensure
    /// all correlation IDs in the system follow a consistent format for logging
    /// and searching.
    /// </para>
    /// <para>
    /// <b>Normalization:</b> Valid client-provided IDs are normalized to lowercase
    /// to ensure consistency across the system. GUIDs are case-insensitive, but
    /// storing them in a consistent case improves log readability and searching.
    /// </para>
    /// </remarks>
    private static string GetCorrelationIdFromRequest(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(CorrelationIdHeaderName, out var headerValues))
        {
            var providedCorrelationId = headerValues.FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(providedCorrelationId) &&
                CorrelationIdUtility.TryParse(providedCorrelationId, out var normalizedId))
            {
                // Client provided a valid correlation ID, use it
                return normalizedId!;
            }
        }

        // No valid correlation ID provided, generate a new one
        return CorrelationIdUtility.Generate();
    }
}
