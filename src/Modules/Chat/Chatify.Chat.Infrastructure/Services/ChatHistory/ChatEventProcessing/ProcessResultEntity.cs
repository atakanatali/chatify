namespace Chatify.Chat.Infrastructure.Services.ChatHistory.ChatEventProcessing;

/// <summary>
/// Represents the outcome of processing a chat event message.
/// </summary>
/// <remarks>
/// <para>
/// <b>Purpose:</b> This enum provides a clear, typed result for chat event
/// processing, enabling the consumer to make appropriate decisions about
/// offset management and error handling.
/// </para>
/// <para>
/// <b>Consumer Behavior:</b> The background service uses this result to
/// determine whether to commit the message offset, retry processing, or
/// handle the message as a poison pill (permanent failure).
/// </para>
/// </remarks>
public enum ProcessResultEntity
{
    /// <summary>
    /// The message was processed successfully and can be committed.
    /// </summary>
    /// <remarks>
    /// When this result is returned, the consumer will commit the message
    /// offset and continue to the next message. The message will not be
    /// reprocessed unless the consumer group is reset.
    /// </remarks>
    Success = 0,

    /// <summary>
    /// The message failed processing due to a permanent error.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Permanent Errors:</b> These are errors that will not succeed on retry:
    /// <list type="bullet">
    /// <item>JSON deserialization failures (malformed payload)</item>
    /// <item>Schema validation failures (missing required fields)</item>
    /// <item>Data type mismatches (e.g., invalid GUID format)</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Consumer Behavior:</b> When this result is returned, the consumer
    /// should commit the offset to prevent infinite replay of the poison message.
    /// The message should be logged to Elasticsearch for analysis.
    /// </para>
    /// <para>
    /// <b>Future Enhancement:</b> Permanent failures should be written to a
    /// dead-letter queue (DLQ) topic for later analysis and reprocessing if needed.
    /// </para>
    /// </remarks>
    PermanentFailure = 1
}
