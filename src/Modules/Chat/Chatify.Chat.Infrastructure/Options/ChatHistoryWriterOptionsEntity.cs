namespace Chatify.Chat.Infrastructure.Options;

/// <summary>
/// Configuration options for the chat history writer background service.
/// </summary>
/// <remarks>
/// <para>
/// <b>Purpose:</b> This options class encapsulates all configurable behavior
/// for the chat history writer, including retry policy parameters, backoff
/// settings, and consumer group identification.
/// </para>
/// <para>
/// <b>Configuration Section:</b> These settings are read from the
/// <c>"Chatify:ChatHistoryWriter"</c> configuration section in appsettings.json
/// or environment variables:
/// <code><![CDATA[
/// {
///   "Chatify": {
///     "ChatHistoryWriter": {
///       "ConsumerGroupId": "chatify-chat-history-writer",
///       "DatabaseRetryMaxAttempts": 5,
///       "DatabaseRetryBaseDelayMs": 100,
///       "DatabaseRetryMaxDelayMs": 10000,
///       "ConsumerBackoffInitialMs": 1000,
///       "ConsumerBackoffMaxMs": 16000
///     }
///   }
/// }
/// ]]></code>
/// </para>
/// <para>
/// <b>Validation:</b> The <see cref="IsValid"/> method validates that all
/// required fields are present and within acceptable ranges. Invalid
/// configuration will cause the application to fail fast at startup.
/// </para>
/// </remarks>
public record ChatHistoryWriterOptionsEntity
{
    /// <summary>
    /// Gets or sets the consumer group ID for the chat history writer.
    /// </summary>
    /// <value>
    /// The consumer group ID used by all pod instances to share the workload
    /// of persisting chat events. Default: "chatify-chat-history-writer".
    /// </value>
    /// <remarks>
    /// <para>
    /// <b>Shared Group Pattern:</b> All pods use the same group ID, allowing
    /// Kafka to distribute partition ownership among active consumers via
    /// consumer group rebalancing.
    /// </para>
    /// <para>
    /// <b>Partition Distribution:</b> Each partition is owned by exactly one
    /// consumer in the group at any time. Adding pods increases throughput
    /// up to the number of partitions.
    /// </para>
    /// <para>
    /// <b>Do Not Change:</b> Once deployed, changing the consumer group ID
    /// will cause consumers to start from the beginning of the topic, leading
    /// to duplicate processing of historical messages.
    /// </para>
    /// </remarks>
    public string ConsumerGroupId { get; init; } = "chatify-chat-history-writer";

    /// <summary>
    /// Gets or sets the prefix used for the client ID in broker logs.
    /// </summary>
    /// <value>
    /// The prefix for the client ID, which is combined with the pod identifier
    /// to create a unique client ID per instance. Default: "chatify-history-writer-".
    /// </value>
    /// <remarks>
    /// The full client ID format is: <c>{ClientIdPrefix}{PodId}</c>.
    /// This helps identify individual consumer instances in broker logs and monitoring.
    /// </remarks>
    public string ClientIdPrefix { get; init; } = "chatify-history-writer-";

    /// <summary>
    /// Gets or sets the maximum number of retry attempts for database write operations.
    /// </summary>
    /// <value>
    /// The maximum number of times to retry a failed database write before giving up.
    /// Default: 5.
    /// </value>
    /// <remarks>
    /// <para>
    /// <b>Retry Scope:</b> This setting applies only to transient database errors
    /// such as network issues, timeouts, and temporary unavailability. Permanent
    /// errors (deserialization, validation) are not retried.
    /// </para>
    /// <para>
    /// <b>Recommended Values:</b>
    /// <list type="bullet">
    /// <item>Development: 2-3 (fail fast for debugging)</item>
    /// <item>Production: 5-10 (handle transient failures)</item>
    /// <item>High Availability: 10+ (maximize persistence success)</item>
    /// </list>
    /// </para>
    /// </remarks>
    public int DatabaseRetryMaxAttempts { get; init; } = 5;

    /// <summary>
    /// Gets or sets the base delay in milliseconds for exponential backoff on database retry.
    /// </summary>
    /// <value>
    /// The initial delay for the first retry, in milliseconds. Default: 100.
    /// </value>
    /// <remarks>
    /// <para>
    /// <b>Backoff Calculation:</b> The delay for retry attempt N is calculated as:
    /// <c>BaseDelayMs × 2^(N-1) + jitter</c>, capped at <see cref="DatabaseRetryMaxDelayMs"/>.
    /// </para>
    /// <para>
    /// <b>Example with defaults:</b>
    /// <list type="bullet">
    /// <item>Retry 1: 100ms + jitter</item>
    /// <item>Retry 2: 200ms + jitter</item>
    /// <item>Retry 3: 400ms + jitter</item>
    /// <item>Retry 4: 800ms + jitter</item>
    /// <item>Retry 5: 1600ms + jitter</item>
    /// </list>
    /// </para>
    /// </remarks>
    public int DatabaseRetryBaseDelayMs { get; init; } = 100;

    /// <summary>
    /// Gets or sets the maximum delay in milliseconds for exponential backoff on database retry.
    /// </summary>
    /// <value>
    /// The maximum delay between retry attempts, in milliseconds. Default: 10000 (10 seconds).
    /// </value>
    /// <remarks>
    /// This value caps the exponential backoff calculation to prevent excessively
    /// long delays during extended outage scenarios.
    /// </remarks>
    public int DatabaseRetryMaxDelayMs { get; init; } = 10000;

    /// <summary>
    /// Gets or sets the jitter range in milliseconds to add to retry delays.
    /// </summary>
    /// <value>
    /// The maximum random jitter to add to each retry delay, in milliseconds.
    /// Default: 100.
    /// </value>
    /// <remarks>
    /// <para>
    /// <b>Purpose:</b> Jitter prevents the "thundering herd" problem where
    /// multiple consumers retry simultaneously, potentially overwhelming
    /// the database during recovery.
    /// </para>
    /// <para>
    /// <b>Calculation:</b> A random value between 0 and this value is added
    /// to each retry delay. For example, with 100ms jitter, a calculated
    /// delay of 200ms becomes 200-300ms.
    /// </para>
    /// </remarks>
    public int DatabaseRetryJitterMs { get; init; } = 100;

    /// <summary>
    /// Gets or sets the initial backoff delay in milliseconds for consumer-level errors.
    /// </summary>
    /// <value>
    /// The initial delay when the consumer itself encounters errors (e.g., broker
    /// connection issues), in milliseconds. Default: 1000 (1 second).
    /// </value>
    /// <remarks>
    /// <para>
    /// <b>Consumer-Level Errors:</b> These errors affect the entire consumer,
    /// not just individual messages. Examples include broker connection failures,
    /// authorization errors, and consumer group rebalancing issues.
    /// </para>
    /// <para>
    /// <b>Backoff Strategy:</b> The consumer uses exponential backoff starting
    /// at this value and doubling until <see cref="ConsumerBackoffMaxMs"/> is reached.
    /// </para>
    /// </remarks>
    public int ConsumerBackoffInitialMs { get; init; } = 1000;

    /// <summary>
    /// Gets or sets the maximum backoff delay in milliseconds for consumer-level errors.
    /// </summary>
    /// <value>
    /// The maximum delay between consumer retry attempts, in milliseconds.
    /// Default: 16000 (16 seconds).
    /// </value>
    /// <remarks>
    /// When the maximum backoff is reached, the service may allow itself to
    /// exit so Kubernetes can restart it. This prevents a stuck consumer from
    /// running indefinitely without making progress.
    /// </remarks>
    public int ConsumerBackoffMaxMs { get; init; } = 16000;

    /// <summary>
    /// Gets or sets the maximum payload size to log on deserialization failure, in bytes.
    /// </summary>
    /// <value>
    /// The maximum number of bytes to log from a failed payload. Default: 256.
    /// </value>
    /// <remarks>
    /// <para>
    /// <b>Privacy Protection:</b> This limit prevents logging of potentially
    /// sensitive user data in error messages. Only the first N bytes are logged.
    /// </para>
    /// <para>
    /// <b>Recommendation:</b> Keep this value small (100-500 bytes) to balance
    /// debugging utility with privacy concerns.
    /// </para>
    /// </remarks>
    public int MaxPayloadLogBytes { get; init; } = 256;

    /// <summary>
    /// Validates the configuration options.
    /// </summary>
    /// <returns>
    /// <c>true</c> if all required fields have valid values; otherwise, <c>false</c>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>Validation Rules:</b>
    /// <list type="bullet">
    /// <item><see cref="ConsumerGroupId"/> must not be null or whitespace</item>
    /// <item><see cref="ClientIdPrefix"/> must not be null or whitespace</item>
    /// <item><see cref="DatabaseRetryMaxAttempts"/> must be greater than zero</item>
    /// <item><see cref="DatabaseRetryBaseDelayMs"/> must be greater than zero</item>
    /// <item><see cref="DatabaseRetryMaxDelayMs"/> must be greater than <see cref="DatabaseRetryBaseDelayMs"/></item>
    /// <item><see cref="DatabaseRetryJitterMs"/> must be greater than or equal to zero</item>
    /// <item><see cref="ConsumerBackoffInitialMs"/> must be greater than zero</item>
    /// <item><see cref="ConsumerBackoffMaxMs"/> must be greater than <see cref="ConsumerBackoffInitialMs"/></item>
    /// <item><see cref="MaxPayloadLogBytes"/> must be greater than zero</item>
    /// </list>
    /// </para>
    /// </remarks>
    public bool IsValid()
    {
        return !string.IsNullOrWhiteSpace(ConsumerGroupId) &&
               !string.IsNullOrWhiteSpace(ClientIdPrefix) &&
               DatabaseRetryMaxAttempts > 0 &&
               DatabaseRetryBaseDelayMs > 0 &&
               DatabaseRetryMaxDelayMs > DatabaseRetryBaseDelayMs &&
               DatabaseRetryJitterMs >= 0 &&
               ConsumerBackoffInitialMs > 0 &&
               ConsumerBackoffMaxMs > ConsumerBackoffInitialMs &&
               MaxPayloadLogBytes > 0;
    }

    /// <summary>
    /// Returns a string representation of the options for logging.
    /// </summary>
    /// <returns>
    /// A formatted string containing the key configuration values.
    /// </returns>
    public override string ToString()
    {
        return $"ConsumerGroupId={ConsumerGroupId}, " +
               $"DatabaseRetryMaxAttempts={DatabaseRetryMaxAttempts}, " +
               $"DatabaseRetryBaseDelayMs={DatabaseRetryBaseDelayMs}, " +
               $"DatabaseRetryMaxDelayMs={DatabaseRetryMaxDelayMs}, " +
               $"ConsumerBackoffInitialMs={ConsumerBackoffInitialMs}, " +
               $"ConsumerBackoffMaxMs={ConsumerBackoffMaxMs}";
    }
}
