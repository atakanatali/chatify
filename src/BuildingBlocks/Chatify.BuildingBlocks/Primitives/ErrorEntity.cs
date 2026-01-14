namespace Chatify.BuildingBlocks.Primitives;

/// <summary>
/// Represents structured error information containing an error code, message, and optional details.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="ErrorEntity"/> class encapsulates error information in a consistent format
/// throughout the Chatify application. It is used by <see cref="ResultEntity"/> to represent
/// operation failures and can be serialized for transmission across service boundaries.
/// </para>
/// <para>
/// Error codes should follow a hierarchical naming convention (e.g., "CHATIFY.USER.NOT_FOUND")
/// to enable programmatic error handling and client-side error mapping.
/// </para>
/// <para>
/// Example usage:
/// <code><![CDATA[
/// var error = new ErrorEntity("CHATIFY.USER.NOT_FOUND", "User not found", "User ID '123' does not exist");
/// var result = ResultEntity.Failure(error);
/// ]]></code>
/// </para>
/// </remarks>
public sealed record ErrorEntity
{
    /// <summary>
    /// Gets the error code that identifies the type of error that occurred.
    /// </summary>
    /// <value>
    /// A string containing a machine-readable error code. Error codes should follow
    /// a hierarchical dot-notation format (e.g., "CHATIFY.MODULE.SPECIFIC_ERROR").
    /// </value>
    /// <remarks>
    /// Error codes enable programmatic error handling, client-side error mapping,
    /// and internationalization of error messages. The code should be stable and unique
    /// across the application to avoid collisions.
    /// </remarks>
    public string Code { get; }

    /// <summary>
    /// Gets the human-readable error message describing what went wrong.
    /// </summary>
    /// <value>
    /// A string containing a description of the error suitable for display to users or logging.
    /// This message should be clear and actionable, explaining the error in non-technical terms
    /// when possible.
    /// </value>
    /// <remarks>
    /// The message should not contain sensitive information such as passwords, API keys,
    /// or internal system details that could aid attackers. It should be safe to expose
    /// to authenticated users.
    /// </remarks>
    public string Message { get; }

    /// <summary>
    /// Gets additional details about the error, including contextual information or stack traces.
    /// </summary>
    /// <value>
    /// A string containing supplementary error information, or <c>null</c> if no details are available.
    /// This may include technical details for debugging, contextual information about the operation
    /// that failed, or suggested remediation steps.
    /// </value>
    /// <remarks>
    /// <para>
    /// Details are optional and should be used for information that aids in troubleshooting
    /// but is not essential for understanding the error. This field may contain sensitive
    /// information and should not be exposed to end users.
    /// </para>
    /// <para>
    /// In production environments, details should be logged but not returned to clients
    /// to prevent information leakage.
    /// </para>
    /// </remarks>
    public string? Details { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ErrorEntity"/> record.
    /// </summary>
    /// <param name="code">The error code identifying the type of error.</param>
    /// <param name="message">The human-readable error message.</param>
    /// <param name="details">Optional additional details about the error.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="code"/> or <paramref name="message"/> is null or empty.
    /// </exception>
    /// <remarks>
    /// The constructor validates that both code and message are provided. Use empty string
    /// for <paramref name="details"/> when no additional information is available rather
    /// than passing <c>null</c>.
    /// </remarks>
    public ErrorEntity(string code, string message, string? details = null)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Error code cannot be null or empty.", nameof(code));
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("Error message cannot be null or empty.", nameof(message));
        }

        Code = code;
        Message = message;
        Details = details;
    }

    /// <summary>
    /// Creates an <see cref="ErrorEntity"/> representing a validation error.
    /// </summary>
    /// <param name="fieldName">The name of the field that failed validation.</param>
    /// <param name="message">The validation error message.</param>
    /// <returns>
    /// An <see cref="ErrorEntity"/> with a "VALIDATION" error code prefix.
    /// </returns>
    /// <remarks>
    /// This factory method creates a standardized validation error with the code format
    /// "CHATIFY.VALIDATION.{fieldName}". Use this method for consistent validation error
    /// representation across the application.
    /// </remarks>
    public static ErrorEntity Validation(string fieldName, string message)
    {
        return new ErrorEntity($"CHATIFY.VALIDATION.{fieldName.ToUpperInvariant()}", message);
    }

    /// <summary>
    /// Creates an <see cref="ErrorEntity"/> representing a "not found" error.
    /// </summary>
    /// <param name="resourceName">The type of resource that was not found (e.g., "User", "Message").</param>
    /// <param name="identifier">The identifier that was searched for.</param>
    /// <returns>
    /// An <see cref="ErrorEntity"/> with a "NOT_FOUND" error code.
    /// </returns>
    /// <remarks>
    /// This factory method creates a standardized "not found" error with the code format
    /// "CHATIFY.NOT_FOUND.{resourceName}". The message includes the resource name and identifier
    /// for clarity.
    /// </remarks>
    public static ErrorEntity NotFound(string resourceName, string identifier)
    {
        return new ErrorEntity(
            $"CHATIFY.NOT_FOUND.{resourceName.ToUpperInvariant()}",
            $"{resourceName} '{identifier}' was not found.");
    }

    /// <summary>
    /// Creates an <see cref="ErrorEntity"/> representing an unauthorized access error.
    /// </summary>
    /// <param name="message">The error message describing the unauthorized access attempt.</param>
    /// <returns>
    /// An <see cref="ErrorEntity"/> with an "UNAUTHORIZED" error code.
    /// </returns>
    /// <remarks>
    /// This factory method creates a standardized unauthorized error with the code
    /// "CHATIFY.UNAUTHORIZED". Use this for authentication and authorization failures.
    /// </remarks>
    public static ErrorEntity Unauthorized(string message = "Unauthorized access.")
    {
        return new ErrorEntity("CHATIFY.UNAUTHORIZED", message);
    }

    /// <summary>
    /// Returns a string representation of the error, including code and message.
    /// </summary>
    /// <returns>
    /// A string in the format "[Code]: Message".
    /// </returns>
    /// <remarks>
    /// This method provides a concise string representation suitable for logging
    /// or debugging. Details are not included to keep the output concise.
    /// </remarks>
    public override string ToString()
    {
        return $"[{Code}]: {Message}";
    }
}
