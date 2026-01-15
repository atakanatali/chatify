using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

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
/// different HTTP status codes and error messages:
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
/// <b>Logging:</b> All exceptions are logged with their correlation ID,
/// enabling efficient debugging and incident response. The correlation ID
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
    /// The logger used to record exception details for debugging and monitoring.
    /// </summary>
    /// <remarks>
    /// Exceptions logged here include correlation IDs and stack traces,
    /// enabling efficient debugging and incident response.
    /// </remarks>
    private readonly ILogger<GlobalExceptionHandlingMiddleware> _logger;

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
    /// <param name="logger">
    /// The logger used to record exception details.
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
        ILogger<GlobalExceptionHandlingMiddleware> logger,
        IWebHostEnvironment env)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
    /// <item>Determine the appropriate HTTP status code based on exception type</item>
    /// <item>Create a ProblemDetails object with error information</item>
    /// <item>Log the full exception details with correlation ID</item>
    /// <item>Set the response status code and content type</item>
    /// <item>Serialize and write the ProblemDetails to the response</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Status Code Mapping:</b>
    /// <list type="table">
    /// <listheader><term>Exception Type</term><description>Status Code</description></listheader>
    /// <item><term>ArgumentException</term><description>400</description></item>
    /// <item><term>ArgumentNullException</term><description>400</description></item>
    /// <item><term>UnauthorizedAccessException</term><description>401</description></item>
    /// <item><term>KeyNotFoundException</term><description>404</description></item>
    /// <item><term>InvalidOperationException</term><description>409</description></item>
    /// <item><term>TimeoutException</term><description>504</description></item>
    /// <item><term>Other</term><description>500</description></item>
    /// </list>
    /// </para>
    /// </remarks>
    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        // Determine the appropriate status code and title based on exception type
        var (statusCode, title) = GetStatusCodeAndTitle(exception);

        // Log the exception with full details
        LogException(context, exception, statusCode);

        // Create ProblemDetails response
        var problemDetails = CreateProblemDetails(context, exception, statusCode, title);

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
    /// Determines the appropriate HTTP status code and title for a given exception.
    /// </summary>
    /// <param name="exception">
    /// The exception to evaluate.
    /// </param>
    /// <returns>
    /// A tuple containing the HTTP status code and a descriptive title.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This method maps exception types to appropriate HTTP status codes following
    /// RFC 7231 guidelines. The mapping ensures clients receive semantically
    /// correct error responses.
    /// </para>
    /// <para>
    /// <b>Default Behavior:</b> Unrecognized exception types default to
    /// 500 Internal Server Error to prevent information leakage.
    /// </para>
    /// </remarks>
    private static (int StatusCode, string Title) GetStatusCodeAndTitle(Exception exception)
    {
        return exception switch
        {
            ArgumentNullException => ((int)HttpStatusCode.BadRequest, "Bad Request"),
            UnauthorizedAccessException => ((int)HttpStatusCode.Unauthorized, "Unauthorized"),
            KeyNotFoundException => ((int)HttpStatusCode.NotFound, "Not Found"),
            InvalidOperationException => ((int)HttpStatusCode.Conflict, "Conflict"),
            TimeoutException => ((int)HttpStatusCode.GatewayTimeout, "Gateway Timeout"),
            ArgumentException => ((int)HttpStatusCode.BadRequest, "Bad Request"),
            _ => ((int)HttpStatusCode.InternalServerError, "Internal Server Error")
        };
    }

    /// <summary>
    /// Creates a ProblemDetails object with error information for the client.
    /// </summary>
    /// <param name="context">
    /// The HTTP context for the current request.
    /// </param>
    /// <param name="exception">
    /// The exception that occurred.
    /// </param>
    /// <param name="statusCode">
    /// The HTTP status code to return.
    /// </param>
    /// <param name="title">
    /// A short, human-readable summary of the problem type.
    /// </param>
    /// <returns>
    /// A <see cref="ProblemDetails"/> object containing error information.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>Production vs Development:</b> In production environments, only
    /// generic error information is included to prevent information leakage.
    /// In development, additional details like stack traces may be included
    /// to aid debugging.
    /// </para>
    /// <para>
    /// <b>RFC 7807 Compliance:</b> The ProblemDetails object follows the
    /// RFC 7807 specification for problem details in HTTP APIs.
    /// </para>
    /// </remarks>
    private ProblemDetails CreateProblemDetails(
        HttpContext context,
        Exception exception,
        int statusCode,
        string title)
    {
        var problemDetails = new ProblemDetails
        {
            Type = $"https://tools.ietf.org/html/rfc7231#section-{GetStatusCodeSection(statusCode)}",
            Title = title,
            Status = statusCode,
            Detail = GetDetailMessage(exception, statusCode),
            Instance = context.Request.Path
        };

        // In development, include additional debug information
        if (_env.IsDevelopment())
        {
            problemDetails.Extensions["stackTrace"] = exception.StackTrace;
            problemDetails.Extensions["innerException"] = exception.InnerException?.Message;
        }

        return problemDetails;
    }

    /// <summary>
    /// Gets the appropriate detail message for an exception based on environment and status code.
    /// </summary>
    /// <param name="exception">
    /// The exception that occurred.
    /// </param>
    /// <param name="statusCode">
    /// The HTTP status code being returned.
    /// </param>
    /// <returns>
    /// A string containing the detail message for the response.
    /// </returns>
    /// <remarks>
    /// <para>
    /// In development, the actual exception message is returned for debugging.
    /// In production, a generic message is returned to prevent information leakage.
    /// </para>
    /// </remarks>
    private string GetDetailMessage(Exception exception, int statusCode)
    {
        if (_env.IsDevelopment())
        {
            return exception.Message;
        }

        return statusCode switch
        {
            400 => "The request was invalid or missing required parameters.",
            401 => "Authentication is required to access this resource.",
            404 => "The requested resource was not found.",
            409 => "The request could not be completed due to a conflict.",
            504 => "The request timed out while processing.",
            _ => "An error occurred while processing your request. Please try again later."
        };
    }

    /// <summary>
    /// Gets the RFC 7231 section identifier for a given status code.
    /// </summary>
    /// <param name="statusCode">
    /// The HTTP status code.
    /// </param>
    /// <returns>
    /// A string containing the RFC 7231 section identifier (e.g., "6.5.1" for 400).
    /// </returns>
    /// <remarks>
    /// This method maps status codes to their corresponding RFC 7231 sections,
    /// which are used in the ProblemDetails type URI to provide standardized
    /// error type references.
    /// </remarks>
    private static string GetStatusCodeSection(int statusCode)
    {
        return statusCode switch
        {
            400 => "6.5.1",
            401 => "6.5.2",
            403 => "6.5.3",
            404 => "6.5.4",
            409 => "6.5.8",
            500 => "6.6.1",
            502 => "6.6.3",
            503 => "6.6.4",
            504 => "6.6.5",
            _ => "6.6.1"
        };
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
    /// All exceptions are logged with full details including stack traces,
    /// correlation IDs, and request path. This information is essential for
    /// debugging and incident response.
    /// </para>
    /// <para>
    /// The log level is determined by the status code:
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

        var logLevel = statusCode >= 500 ? LogLevel.Error : LogLevel.Warning;

        _logger.Log(logLevel, exception,
            "Unhandled exception occurred. CorrelationId: {CorrelationId}, Path: {Path}, Method: {Method}, StatusCode: {StatusCode}, Message: {Message}",
            correlationId,
            path,
            method,
            statusCode,
            exception.Message);
    }
}
