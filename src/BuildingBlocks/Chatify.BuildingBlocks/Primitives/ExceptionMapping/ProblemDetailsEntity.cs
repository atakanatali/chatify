namespace Chatify.BuildingBlocks.Primitives;

/// <summary>
/// Represents an RFC 7807 Problem Details error response.
/// </summary>
/// <remarks>
/// <para>
/// <b>Purpose:</b> This class provides a simple, dependency-free representation
/// of RFC 7807 Problem Details for HTTP API error responses. It is designed
/// to be used in the BuildingBlocks layer without requiring ASP.NET Core dependencies.
/// </para>
/// <para>
/// <b>RFC 7807 Compliance:</b> This class implements the standard fields defined
/// in RFC 7807: type, title, status, detail, and instance.
/// </para>
/// <para>
/// <b>Usage:</b> This class is typically returned from middleware or converted
/// to JSON for HTTP responses. In ASP.NET Core middleware, it can be mapped
/// to Microsoft.AspNetCore.Mvc.ProblemDetails if needed.
/// </para>
/// <para>
/// <b>Example:</b>
/// <code><![CDATA[
/// var problemDetails = new ProblemDetailsEntity
/// {
///     Type = "https://tools.ietf.org/html/rfc7231#section-6.5.1",
///     Title = "Bad Request",
///     Status = 400,
///     Detail = "The request was invalid or missing required parameters.",
///     Instance = "/api/chat/send"
/// };
/// ]]></code>
/// </para>
/// </remarks>
public class ProblemDetailsEntity
{
    /// <summary>
    /// Gets or sets the URI reference that identifies the problem type.
    /// </summary>
    /// <remarks>
    /// When omitted, this value defaults to "about:blank". This should typically
    /// reference an RFC 7231 section (e.g., "https://tools.ietf.org/html/rfc7231#section-6.5.1").
    /// </remarks>
    public string? Type { get; set; }

    /// <summary>
    /// Gets or sets a short, human-readable summary of the problem type.
    /// </summary>
    /// <remarks>
    /// This should not change between occurrences of the problem except for
    /// localization purposes. Examples: "Bad Request", "Unauthorized", "Not Found".
    /// </remarks>
    public string? Title { get; set; }

    /// <summary>
    /// Gets or sets the HTTP status code for this problem.
    /// </summary>
    /// <remarks>
    /// This should be the same as the HTTP response status code. Common values:
    /// 400 (Bad Request), 401 (Unauthorized), 404 (Not Found), 409 (Conflict),
    /// 500 (Internal Server Error), 504 (Gateway Timeout).
    /// </remarks>
    public int? Status { get; set; }

    /// <summary>
    /// Gets or sets a human-readable explanation specific to this occurrence of the problem.
    /// </summary>
    /// <remarks>
    /// The detail should be specific to this occurrence and may include information
    /// about what went wrong and how to fix it. In production, this should be generic
    /// to prevent information leakage. In development, it may include exception details.
    /// </remarks>
    public string? Detail { get; set; }

    /// <summary>
    /// Gets or sets the URI reference that identifies the specific occurrence of the problem.
    /// </summary>
    /// <remarks>
    /// This is typically the request path or a unique identifier for this error occurrence.
    /// Example: "/api/chat/send" or "/background/chat-history-writer".
    /// </remarks>
    public string? Instance { get; set; }

    /// <summary>
    /// Gets or sets additional extension properties for the problem details.
    /// </summary>
    /// <remarks>
    /// RFC 7807 allows for additional properties beyond the standard fields.
    /// Common extensions include stackTrace, exceptionType, and innerException
    /// (for development/debugging purposes only).
    /// </remarks>
    public IDictionary<string, object?> Extensions { get; set; } = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
}
