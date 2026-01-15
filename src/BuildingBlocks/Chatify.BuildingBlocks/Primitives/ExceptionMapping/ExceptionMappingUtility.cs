using System.Net;
using Microsoft.AspNetCore.Mvc;

namespace Chatify.BuildingBlocks.Primitives;

/// <summary>
/// Provides utility methods for mapping exceptions to RFC 7807 ProblemDetails responses.
/// </summary>
/// <remarks>
/// <para>
/// <b>Purpose:</b> This utility class centralizes the logic for converting various exception types
/// into standardized ProblemDetails objects with appropriate HTTP status codes, titles, and messages.
/// It ensures consistent error responses across the entire application and prevents information leakage
/// in production environments.
/// </para>
/// <para>
/// <b>Design Philosophy:</b> The mapping is implemented as a static utility class with pure functions
/// that take an exception and environment information, then return a ProblemDetails object. This design
/// allows for easy testing, reuse across different components (middleware, background services, etc.),
/// and ensures deterministic behavior without side effects.
/// </para>
/// <para>
/// <b>Exception Categorization:</b> Exceptions are mapped to appropriate HTTP status codes based on
/// their semantic meaning:
/// <list type="table">
/// <listheader>
/// <term>Exception Type</term>
/// <description>HTTP Status</description>
/// <description>Rationale</description>
/// </listheader>
/// <item><term>ArgumentException, ArgumentNullException</term><description>400 Bad Request</description><description>Invalid input from client</description></item>
/// <item><term>UnauthorizedAccessException</term><description>401 Unauthorized</description><description>Authentication required or failed</description></item>
/// <item><term>KeyNotFoundException</term><description>404 Not Found</description><description>Requested resource does not exist</description></item>
/// <item><term>InvalidOperationException</term><description>409 Conflict</description><description>Operation conflicts with current state</description></item>
/// <item><term>TimeoutException</term><description>504 Gateway Timeout</description><description>External operation timed out</description></item>
/// <item><term>All other exceptions</term><description>500 Internal Server Error</description><description>Unexpected server error</description></item>
/// </list>
/// </para>
/// <para>
/// <b>Security Considerations:</b> In production environments, the utility does not expose
/// stack traces or internal exception details in the response. Sensitive information is only
/// included in development mode for debugging purposes.
/// </para>
/// <para>
/// <b>Thread Safety:</b> This class is thread-safe. All methods are static and do not maintain
/// any state, allowing concurrent calls from multiple threads without synchronization.
/// </para>
/// <para>
/// <b>Usage Examples:</b>
/// <code><![CDATA[
/// // In middleware
/// var problemDetails = ExceptionMappingUtility.MapToProblemDetails(
///     exception,
///     "/api/chat/send",
///     isDevelopment: false);
///
/// // In background service
/// catch (Exception ex)
/// {
///     var problemDetails = ExceptionMappingUtility.MapToProblemDetails(
///         ex,
///         "/background/chat-history-writer",
///         isDevelopment: true);
///     _logService.Error(ex, "Background service error", new { ProblemDetails = problemDetails });
/// }
/// ]]></code>
/// </para>
/// </remarks>
public static class ExceptionMappingUtility
{
    /// <summary>
    /// The base URI for RFC 7231 HTTP status code references.
    /// </summary>
    /// <remarks>
    /// This constant is used as the base for constructing ProblemDetails "type" URIs
    /// that point to the RFC 7231 specification for each status code.
    /// </remarks>
    private const string Rfc7231BaseUri = "https://tools.ietf.org/html/rfc7231#section-";

    /// <summary>
    /// The generic error message used in production for 5xx errors.
    /// </summary>
    /// <remarks>
    /// This message is used to prevent information leakage while still informing
    /// the client that an error occurred on the server.
    /// </remarks>
    private const string GenericServerError = "An error occurred while processing your request. Please try again later.";

    /// <summary>
    /// Maps an exception to a ProblemDetails object with appropriate status code, title, and detail.
    /// </summary>
    /// <param name="exception">
    /// The exception to map. Must not be null.
    /// </param>
    /// <param name="instance">
    /// The path or identifier for the specific occurrence of the problem (typically the request path).
    /// Can be null or empty for background services.
    /// </param>
    /// <param name="isDevelopment">
    /// Indicates whether the application is running in development mode.
    /// When true, additional debug information may be included in the response.
    /// </param>
    /// <returns>
    /// A <see cref="ProblemDetails"/> object containing the mapped error information.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="exception"/> is null.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>Mapping Logic:</b>
    /// <list type="number">
    /// <item>Determine HTTP status code and title based on exception type</item>
    /// <item>Set appropriate detail message (generic in production, specific in development)</item>
    /// <item>Include RFC 7231 reference URI in the type field</item>
    /// <item>In development, add stack trace and inner exception as extensions</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Status Code Determination:</b> The status code is determined by the exception type
    /// using the following precedence:
    /// <list type="bullet">
    /// <item>Specific exception types (ArgumentException, UnauthorizedAccessException, etc.)</item>
    /// <item>Exception base types (e.g., ArgumentException for ArgumentNullException)</item>
    /// <item>Default to 500 for unknown exception types</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Development vs Production:</b>
    /// <list type="bullet">
    /// <item><b>Development:</b> Includes exception message, stack trace, and inner exceptions</item>
    /// <item><b>Production:</b> Includes only generic error messages to prevent information leakage</item>
    /// </list>
    /// </para>
    /// </remarks>
    public static ProblemDetails MapToProblemDetails(
        Exception exception,
        string? instance,
        bool isDevelopment)
    {
        GuardUtility.NotNull(exception);

        var (statusCode, title) = GetStatusCodeAndTitle(exception);

        var problemDetails = new ProblemDetails
        {
            Type = $"{Rfc7231BaseUri}{GetStatusCodeSection(statusCode)}",
            Title = title,
            Status = statusCode,
            Detail = GetDetailMessage(exception, statusCode, isDevelopment),
            Instance = instance
        };

        // In development, include additional debug information
        if (isDevelopment)
        {
            problemDetails.Extensions["stackTrace"] = exception.StackTrace;
            problemDetails.Extensions["exceptionType"] = exception.GetType().FullName;
            problemDetails.Extensions["innerException"] = exception.InnerException?.Message;
        }

        return problemDetails;
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
    /// <para>
    /// <b>Mapping Table:</b>
    /// <list type="table">
    /// <listheader>
    /// <term>Exception Type</term>
    /// <description>Status Code</description>
    /// <description>Title</description>
    /// </listheader>
    /// <item><term>ArgumentException</term><description>400</description><description>Bad Request</description></item>
    /// <item><term>ArgumentNullException</term><description>400</description><description>Bad Request</description></item>
    /// <item><term>UnauthorizedAccessException</term><description>401</description><description>Unauthorized</description></item>
    /// <item><term>KeyNotFoundException</term><description>404</description><description>Not Found</description></item>
    /// <item><term>InvalidOperationException</term><description>409</description><description>Conflict</description></item>
    /// <item><term>TimeoutException</term><description>504</description><description>Gateway Timeout</description></item>
    /// <item><term>Other</term><description>500</description><description>Internal Server Error</description></item>
    /// </list>
    /// </para>
    /// </remarks>
    private static (int StatusCode, string Title) GetStatusCodeAndTitle(Exception exception)
    {
        return exception switch
        {
            UnauthorizedAccessException => ((int)HttpStatusCode.Unauthorized, "Unauthorized"),
            KeyNotFoundException => ((int)HttpStatusCode.NotFound, "Not Found"),
            InvalidOperationException => ((int)HttpStatusCode.Conflict, "Conflict"),
            TimeoutException => ((int)HttpStatusCode.GatewayTimeout, "Gateway Timeout"),
            ArgumentNullException => ((int)HttpStatusCode.BadRequest, "Bad Request"),
            ArgumentException => ((int)HttpStatusCode.BadRequest, "Bad Request"),
            _ => ((int)HttpStatusCode.InternalServerError, "Internal Server Error")
        };
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
    /// <param name="isDevelopment">
    /// Indicates whether the application is running in development mode.
    /// </param>
    /// <returns>
    /// A string containing the detail message for the response.
    /// </returns>
    /// <remarks>
    /// <para>
    /// In development, the actual exception message is returned for debugging.
    /// In production, a generic message is returned to prevent information leakage.
    /// </para>
    /// <para>
    /// <b>Production Messages by Status Code:</b>
    /// <list type="table">
    /// <listheader>
    /// <term>Status Code</term>
    /// <description>Message</description>
    /// </listheader>
    /// <item><term>400</term><description>The request was invalid or missing required parameters.</description></item>
    /// <item><term>401</term><description>Authentication is required to access this resource.</description></item>
    /// <item><term>404</term><description>The requested resource was not found.</description></item>
    /// <item><term>409</term><description>The request could not be completed due to a conflict.</description></item>
    /// <item><term>504</term><description>The request timed out while processing.</description></item>
    /// <item><term>500</term><description>An error occurred while processing your request. Please try again later.</description></item>
    /// </list>
    /// </para>
    /// </remarks>
    private static string GetDetailMessage(Exception exception, int statusCode, bool isDevelopment)
    {
        if (isDevelopment)
        {
            return exception.Message;
        }

        return statusCode switch
        {
            (int)HttpStatusCode.BadRequest => "The request was invalid or missing required parameters.",
            (int)HttpStatusCode.Unauthorized => "Authentication is required to access this resource.",
            (int)HttpStatusCode.NotFound => "The requested resource was not found.",
            (int)HttpStatusCode.Conflict => "The request could not be completed due to a conflict.",
            (int)HttpStatusCode.GatewayTimeout => "The request timed out while processing.",
            _ => GenericServerError
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
    /// <para>
    /// This method maps status codes to their corresponding RFC 7231 sections,
    /// which are used in the ProblemDetails type URI to provide standardized
    /// error type references.
    /// </para>
    /// <para>
    /// <b>Mapping Table:</b>
    /// <list type="table">
    /// <listheader>
    /// <term>Status Code</term>
    /// <description>RFC Section</description>
    /// </listheader>
    /// <item><term>400</term><description>6.5.1</description></item>
    /// <item><term>401</term><description>6.5.2</description></item>
    /// <item><term>403</term><description>6.5.3</description></item>
    /// <item><term>404</term><description>6.5.4</description></item>
    /// <item><term>409</term><description>6.5.8</description></item>
    /// <item><term>500</term><description>6.6.1</description></item>
    /// <item><term>502</term><description>6.6.3</description></item>
    /// <item><term>503</term><description>6.6.4</description></item>
    /// <item><term>504</term><description>6.6.5</description></item>
    /// <item><term>Other</term><description>6.6.1</description></item>
    /// </list>
    /// </para>
    /// </remarks>
    private static string GetStatusCodeSection(int statusCode)
    {
        return statusCode switch
        {
            (int)HttpStatusCode.BadRequest => "6.5.1",
            (int)HttpStatusCode.Unauthorized => "6.5.2",
            (int)HttpStatusCode.Forbidden => "6.5.3",
            (int)HttpStatusCode.NotFound => "6.5.4",
            (int)HttpStatusCode.Conflict => "6.5.8",
            (int)HttpStatusCode.InternalServerError => "6.6.1",
            (int)HttpStatusCode.BadGateway => "6.6.3",
            (int)HttpStatusCode.ServiceUnavailable => "6.6.4",
            (int)HttpStatusCode.GatewayTimeout => "6.6.5",
            _ => "6.6.1"
        };
    }

    /// <summary>
    /// Determines whether an exception should result in a server error (5xx) or client error (4xx) status code.
    /// </summary>
    /// <param name="exception">
    /// The exception to evaluate.
    /// </param>
    /// <returns>
    /// <c>true</c> if the exception maps to a 5xx status code; <c>false</c> if it maps to a 4xx status code.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This method is useful for determining the appropriate log level for an exception.
    /// Server errors (5xx) typically indicate a problem with the server and should be logged
    /// as errors. Client errors (4xx) typically indicate a problem with the request and can
    /// be logged as warnings.
    /// </para>
    /// <para>
    /// <b>Usage Example:</b>
    /// <code><![CDATA[
    /// if (ExceptionMappingUtility.IsServerError(exception))
    /// {
    ///     _logService.Error(exception, "Server error occurred");
    /// }
    /// else
    /// {
    ///     _logService.Warn("Client error occurred", new { ExceptionType = exception.GetType().Name });
    /// }
    /// ]]></code>
    /// </para>
    /// </remarks>
    public static bool IsServerError(Exception exception)
    {
        GuardUtility.NotNull(exception);

        var (statusCode, _) = GetStatusCodeAndTitle(exception);
        return statusCode >= 500;
    }

    /// <summary>
    /// Gets the HTTP status code for a given exception without creating a full ProblemDetails object.
    /// </summary>
    /// <param name="exception">
    /// The exception to evaluate.
    /// </param>
    /// <returns>
    /// The HTTP status code that corresponds to the exception type.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is a convenience method for scenarios where only the status code is needed,
    /// such as when setting the HTTP response status directly.
    /// </para>
    /// <para>
    /// <b>Usage Example:</b>
    /// <code><![CDATA[
    /// var statusCode = ExceptionMappingUtility.GetStatusCode(exception);
    /// context.Response.StatusCode = statusCode;
    /// ]]></code>
    /// </para>
    /// </remarks>
    public static int GetStatusCode(Exception exception)
    {
        GuardUtility.NotNull(exception);

        var (statusCode, _) = GetStatusCodeAndTitle(exception);
        return statusCode;
    }
}
