using System.Text.Json;
using Chatify.BuildingBlocks.Primitives;

namespace Chatify.Api.Middleware;

/// <summary>
/// Middleware that catches unhandled exceptions from the request pipeline,
/// logs them, and returns standardized ProblemDetails responses to clients.
/// </summary>
/// <remarks>
/// <para>
/// <b>Purpose:</b> This middleware provides a global exception handling boundary
/// for the entire application. It ensures that no unhandled exception propagates
/// to the underlying server, preventing exposure of internal implementation details
/// and providing consistent error responses to API consumers.
/// </para>
/// <para>
/// <b>Error Response Format:</b> The middleware returns RFC 7807 ProblemDetails
/// responses, providing a standardized format for error communication:
/// <code><![CDATA[
/// {
///   "type": "https://tools.ietf.org/html/rfc7231#section-6.5.1",
///   "title": "One or more validation errors occurred.",
///   "status": 400,
///   "detail": "The request was invalid or missing required parameters.",
///   "instance": "/chat/send"
/// }
/// ]]></code>
/// </para>
/// <para>
/// <b>Exception Categorization:</b> Different exception types result in
/// different HTTP status codes and error messages via <see cref="ExceptionMappingUtility"/>:
/// <list type="table">
/// <listheader>
/// <term>Exception Type</term>
/// <description>HTTP Status</description>
/// </listheader>
/// <item><term>ArgumentException, ArgumentNullException</term><description>400 Bad Request</description></item>
/// <item><term>UnauthorizedAccessException</term><description>401 Unauthorized</description></item>
/// <item><term>KeyNotFoundException</term><description>404 Not Found</description></item>
/// <item><term>InvalidOperationException</term><description>409 Conflict</description></item>
/// <item><term>TimeoutException</term><description>504 Gateway Timeout</description></item>
/// <item><term>All other exceptions</term><description>500 Internal Server Error</description></item>
/// </list>
/// </para>
/// <para>
/// <b>Security Considerations:</b> In production environments, the middleware
/// does not expose stack traces or internal exception details to clients.
/// Sensitive information is only logged server-side for debugging purposes.
/// </para>
/// <para>
/// <b>Logging:</b> All exceptions are logged via <see cref="ILogService"/> with their
/// correlation ID, enabling efficient debugging and incident response. The correlation ID
/// is included in log entries to facilitate searching for related logs.
/// </para>
/// <para>
/// <b>Middleware Position:</b> This should be registered early in the
/// middleware pipeline (after correlation ID middleware but before authorization)
/// to catch exceptions from any downstream middleware or request handlers.
/// </para>
/// </remarks>
public sealed class GlobalExceptionHandlingMiddleware
{
    /// <summary>
    /// The delegate representing the next middleware in the request pipeline.
    /// </summary>
    /// <remarks>
    /// This delegate wraps the rest of the middleware pipeline and request
    /// handlers. Exceptions thrown downstream are caught by this middleware.
    /// </remarks>
    private readonly RequestDelegate _next;

    /// <summary>
    /// The log service used to record exception details for debugging and monitoring.
    /// </summary>
    /// <remarks>
    /// Exceptions logged here include correlation IDs and request context,
    /// enabling efficient debugging and incident response.
    /// </remarks>
    private readonly ILogService _logService;

    /// <summary>
    /// The hosting environment information used to determine whether to expose
    /// detailed error information in responses.
    /// </summary>
    /// <remarks>
    /// In development environments, additional details may be included in
    /// error responses to aid debugging. In production, responses are sanitized.
    /// </remarks>
    private readonly IWebHostEnvironment _env;

    /// <summary>
    /// Initializes a new instance of the <see cref="GlobalExceptionHandlingMiddleware"/> class.
    /// </summary>
    /// <param name="next">
    /// The delegate representing the next middleware in the ASP.NET Core request pipeline.
    /// Must not be null.
    /// </param>
    /// <param name="logService">
    /// The log service used to record exception details.
    /// Must not be null.
    /// </param>
    /// <param name="env">
    /// The hosting environment information.
    /// Must not be null.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when any required parameter is null.
    /// </exception>
    /// <remarks>
    /// <para>
    /// The constructor receives its dependencies via dependency injection.
    /// ASP.NET Core automatically provides these when the middleware is instantiated.
    /// </para>
    /// </remarks>
    public GlobalExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogService logService,
        IWebHostEnvironment env)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _logService = logService ?? throw new ArgumentNullException(nameof(logService));
        _env = env ?? throw new ArgumentNullException(nameof(env));
    }

    /// <summary>
    /// Processes an HTTP request with global exception handling.
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
    /// <item>Invoke the next middleware in the pipeline</item>
    /// <item>If no exception occurs, allow normal response flow</item>
    /// <item>If an exception occurs, catch it and handle according to type</item>
    /// <item>Log the exception with correlation ID and context</item>
    /// <item>Generate a ProblemDetails response based on exception type</item>
    /// <item>Return the error response to the client</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Exception Handling Strategy:</b>
    /// <list type="bullet">
    /// <item>Specific exception types receive appropriate status codes</item>
    /// <item>Unhandled exceptions default to 500 Internal Server Error</item>
    /// <item>All exceptions are logged with full details</item>
    /// <item>Client responses are sanitized in production</item>
    /// </list>
    /// </para>
    /// </remarks>
    public async Task InvokeAsync(HttpContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        try
        {
            // Call the next middleware in the pipeline
            await _next(context);
        }
        catch (Exception ex)
        {
            // Catch and handle all unhandled exceptions
            await HandleExceptionAsync(context, ex);
        }
    }

    /// <summary>
    /// Handles an exception by logging it and returning a ProblemDetails response.
    /// </summary>
    /// <param name="context">
    /// The HTTP context for the current request. Used to set the response
    /// status code, headers, and body.
    /// </param>
    /// <param name="exception">
    /// The exception that was thrown during request processing.
    /// </param>
    /// <returns>
    /// A <see cref="Task"/> representing the asynchronous operation.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>Handling Process:</b>
    /// <list type="number">
    /// <item>Map exception to ProblemDetails using <see cref="ExceptionMappingUtility"/></item>
    /// <item>Log the exception with full details and correlation ID</item>
    /// <item>Set the response status code and content type</item>
    /// <item>Serialize and write the ProblemDetails to the response</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Status Code Mapping:</b> The status code is determined by
    /// <see cref="ExceptionMappingUtility.MapToProblemDetails"/> based on the exception type.
    /// </para>
    /// </remarks>
    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        // Map exception to ProblemDetails using the centralized utility
        var problemDetails = ExceptionMappingUtility.MapToProblemDetails(
            exception,
            context.Request.Path,
            _env.IsDevelopment());

        var statusCode = problemDetails.Status ?? 500;

        // Log the exception with full details
        LogException(context, exception, statusCode);

        // Set response properties
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";

        // Serialize and write the response
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = _env.IsDevelopment()
        };

        var json = JsonSerializer.Serialize(problemDetails, options);
        await context.Response.WriteAsync(json);
    }

    /// <summary>
    /// Logs an exception with correlation ID and request context.
    /// </summary>
    /// <param name="context">
    /// The HTTP context for the current request.
    /// </param>
    /// <param name="exception">
    /// The exception to log.
    /// </param>
    /// <param name="statusCode">
    /// The HTTP status code being returned.
    /// </param>
    /// <remarks>
    /// <para>
    /// All exceptions are logged via <see cref="ILogService"/> with full details including
    /// stack traces, correlation IDs, and request path. This information is essential for
    /// debugging and incident response.
    /// </para>
    /// <para>
    /// The log level is determined by <see cref="ExceptionMappingUtility.IsServerError"/>:
    /// <list type="bullet">
    /// <item>Client errors (4xx): Warning</item>
    /// <item>Server errors (5xx): Error</item>
    /// </list>
    /// </para>
    /// </remarks>
    private void LogException(HttpContext context, Exception exception, int statusCode)
    {
        var correlationId = context.Response.Headers[CorrelationIdMiddleware.CorrelationIdHeaderName].ToString();
        var path = context.Request.Path;
        var method = context.Request.Method;

        var logContext = new
        {
            Path = path.ToString(),
            Method = method,
            StatusCode = statusCode,
            ExceptionType = exception.GetType().Name,
            ExceptionMessage = exception.Message
        };

        var message = $"Unhandled exception occurred. CorrelationId: {correlationId}, Path: {path}, Method: {method}, StatusCode: {statusCode}";

        // Use ExceptionMappingUtility to determine log level
        if (ExceptionMappingUtility.IsServerError(exception))
        {
            _logService.Error(exception, message, logContext);
        }
        else
        {
            _logService.Warn(message, logContext);
        }
    }
}
