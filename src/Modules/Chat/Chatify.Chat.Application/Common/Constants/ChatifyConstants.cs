namespace Chatify.Chat.Application.Common.Constants;

/// <summary>
/// Defines constant values used throughout the Chatify Chat Application layer.
/// This centralizes magic strings and numeric values to improve maintainability
/// and prevent typos in error-prone string literals.
/// </summary>
/// <remarks>
/// <para>
/// <b>Purpose:</b> Constants are grouped by functional area (rate limits, error codes)
/// to make discovery and maintenance easier. All constants are static readonly
/// to prevent modification while allowing efficient access.
/// </para>
/// <para>
/// <b>Naming Convention:</b> Constant names use PascalCase with descriptive suffixes
/// indicating their purpose (Key, Code, Prefix, etc.).
/// </para>
/// <para>
/// <b>Usage Pattern:</b>
/// <code><![CDATA[
/// var key = ChatifyConstants.RateLimit.SendChatMessageKey(userId);
/// var error = ServiceError.Chat.ValidationFailed("Invalid scope ID");
/// ]]></code>
/// </para>
/// </remarks>
public static class ChatifyConstants
{
    /// <summary>
    /// Defines constant values related to rate limiting in the Chatify system.
    /// </summary>
    public static class RateLimit
    {
        /// <summary>
        /// The prefix for all chat message sending rate limit keys.
        /// </summary>
        /// <remarks>
        /// Format: "user-{userId}:send-message"
        /// </remarks>
        public const string SendChatMessageKeyPrefix = "user-{0}:send-message";

        /// <summary>
        /// The default threshold for message sending rate limits.
        /// </summary>
        /// <remarks>
        /// Maximum number of messages allowed per time window.
        /// </remarks>
        public const int SendChatMessageThreshold = 100;

        /// <summary>
        /// The default time window for message sending rate limits in seconds.
        /// </summary>
        /// <remarks>
        /// Time window in which the threshold applies.
        /// </remarks>
        public const int SendChatMessageWindowSeconds = 60;

        /// <summary>
        /// Formats a rate limit key for chat message sending.
        /// </summary>
        /// <param name="userId">The user ID to include in the key.</param>
        /// <returns>A formatted rate limit key string.</returns>
        public static string SendChatMessageKey(string userId)
        {
            return string.Format(SendChatMessageKeyPrefix, userId);
        }
    }

    /// <summary>
    /// Defines constant values related to error codes used in the Chatify system.
    /// </summary>
    public static class ErrorCodes
    {
        /// <summary>
        /// Error code for validation failures.
        /// </summary>
        public const string ValidationError = "VALIDATION_ERROR";

        /// <summary>
        /// Error code for rate limit exceeded errors.
        /// </summary>
        public const string RateLimitExceeded = "RATE_LIMIT_EXCEEDED";

        /// <summary>
        /// Error code for configuration errors.
        /// </summary>
        public const string ConfigurationError = "CONFIGURATION_ERROR";

        /// <summary>
        /// Error code for event production failures.
        /// </summary>
        public const string EventProductionFailed = "EVENT_PRODUCTION_FAILED";
    }

    /// <summary>
    /// Defines constant values for log messages used in the Chatify system.
    /// </summary>
    public static class LogMessages
    {
        /// <summary>
        /// Log message template for processing send chat message command.
        /// </summary>
        public const string ProcessingSendChatMessage = "Processing SendChatMessage command for sender {SenderId}, scope type {ScopeType}, scope {ScopeId}";

        /// <summary>
        /// Log message template for domain validation failure.
        /// </summary>
        public const string DomainValidationFailed = "Domain validation failed for message from {SenderId}: {ErrorMessage}";

        /// <summary>
        /// Log message template for rate limit exceeded.
        /// </summary>
        public const string RateLimitExceeded = "Rate limit exceeded for sender {SenderId}";

        /// <summary>
        /// Log message template for origin pod ID validation failure.
        /// </summary>
        public const string OriginPodIdValidationFailed = "Origin pod ID validation failed: {ErrorMessage}";

        /// <summary>
        /// Log message template for successful event production.
        /// </summary>
        public const string SuccessfullyProducedMessage = "Successfully produced message {MessageId} from sender {SenderId} to partition {Partition}, offset {Offset}";

        /// <summary>
        /// Log message template for event production failure.
        /// </summary>
        public const string FailedToProduceMessage = "Failed to produce message {MessageId} from sender {SenderId}";
    }

    /// <summary>
    /// Defines constant values for common error messages used in the Chatify system.
    /// </summary>
    public static class ErrorMessages
    {
        /// <summary>
        /// Error message indicating a feature is not yet implemented.
        /// </summary>
        public const string NotImplemented = "not yet implemented";
    }
}
