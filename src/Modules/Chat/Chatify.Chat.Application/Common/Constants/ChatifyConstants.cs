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
        /// The prefix for all endpoint-level rate limit keys.
        /// </summary>
        /// <remarks>
        /// Format: "rl:{userId}:{endpoint}:{window}"
        /// where:
        /// - {userId} is the user identifier
        /// - {endpoint} is the operation name (e.g., "SendMessage")
        /// - {window} is the window duration in seconds (for TTL management)
        /// </remarks>
        public const string EndpointKeyPrefix = "rl:{0}:{1}:{2}";

        /// <summary>
        /// The endpoint name for the SendMessage operation.
        /// </summary>
        /// <remarks>
        /// Used in rate limit keys to identify the message sending endpoint.
        /// </remarks>
        public const string SendMessageEndpoint = "SendMessage";

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
        /// Formats an endpoint-level rate limit key for the SendMessage operation.
        /// </summary>
        /// <param name="userId">The user ID to include in the key.</param>
        /// <returns>A formatted rate limit key string following the pattern rl:{userId}:{endpoint}:{window}.</returns>
        /// <remarks>
        /// <para>
        /// <b>Key Format:</b> The returned key follows the pattern:
        /// <c>rl:{userId}:SendMessage:{windowSeconds}</c>
        /// </para>
        /// <para>
        /// <b>Example:</b> For user "user123" with a 60-second window:
        /// <c>rl:user123:SendMessage:60</c>
        /// </para>
        /// <para>
        /// <b>Why Include Window in Key:</b> The window duration is included in the key
        /// to ensure that if the rate limit configuration changes, old counters with
        /// different window durations won't interfere with the new configuration.
        /// Each window duration has its own independent counter.
        /// </para>
        /// </remarks>
        public static string SendMessageRateLimitKey(string userId)
        {
            return string.Format(EndpointKeyPrefix, userId, SendMessageEndpoint, SendChatMessageWindowSeconds);
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
