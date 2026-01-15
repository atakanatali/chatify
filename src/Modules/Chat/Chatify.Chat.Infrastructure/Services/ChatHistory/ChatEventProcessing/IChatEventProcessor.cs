namespace Chatify.Chat.Infrastructure.Services.ChatHistory.ChatEventProcessing;

/// <summary>
/// Defines a contract for processing chat event messages from the message broker.
/// </summary>
/// <remarks>
/// <para>
/// <b>Purpose:</b> This interface abstracts the core business logic of
/// processing chat events: deserialization, validation, and persistence.
/// It enables separation of concerns between message consumption (broker
/// interaction) and message processing (business logic).
/// </para>
/// <para>
/// <b>Single Responsibility:</b> The processor is responsible only for
/// transforming a raw message payload into a persisted chat event. All
/// broker interaction (consume, commit, offset management) is handled by
/// the consumer background service.
/// </para>
/// <para>
/// <b>Error Handling Strategy:</b>
/// <list type="bullet">
/// <item><b>Transient Errors:</b> Thrown as exceptions to trigger consumer
/// backoff and retry (e.g., network failures, temporary database unavailability)</item>
/// <item><b>Permanent Errors:</b> Returned as <see cref="ProcessResultEntity.PermanentFailure"/>
/// to signal the consumer to commit the offset and skip the message
/// (e.g., deserialization failures, validation errors)</item>
/// </list>
/// </para>
/// <para>
/// <b>Retry Policy:</b> The processor implementation should include retry
/// logic for transient errors using Polly or a similar resilience framework.
/// The retry policy should be configurable and include exponential backoff.
/// </para>
/// </remarks>
public interface IChatEventProcessor
{
    /// <summary>
    /// Processes a chat event message payload.
    /// </summary>
    /// <param name="payload">
    /// The raw message payload as a byte array. Must not be null.
    /// </param>
    /// <param name="cancellationToken">
    /// A cancellation token that signals when processing should be aborted.
    /// </param>
    /// <returns>
    /// A <see cref="Task{TResult}"/> that completes with the processing result:
    /// <list type="bullet">
    /// <item><see cref="ProcessResultEntity.Success"/> - Message was processed successfully</item>
    /// <item><see cref="ProcessResultEntity.PermanentFailure"/> - Message has a permanent error</item>
    /// </list>
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="payload"/> is null.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken"/> is canceled.
    /// </exception>
    /// <exception cref="Exception">
    /// Thrown for transient errors that should trigger consumer retry.
    /// Common transient errors include network issues, database timeouts,
    /// and temporary unavailability of downstream services.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>Processing Flow:</b>
    /// <list type="number">
    /// <item>Deserialize JSON payload to <see cref="Chatify.Chat.Application.Dtos.ChatEventDto"/></item>
    /// <item>Validate the deserialized event (required fields, data types)</item>
    /// <item>Persist to database via <see cref="Chatify.Chat.Application.Ports.IChatHistoryRepository.AppendAsync"/></item>
    /// <item>Return appropriate result based on outcome</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Idempotency:</b> The processor relies on the repository's idempotent
    /// write mechanism (lightweight transactions) to handle duplicate processing
    /// that may occur due to at-least-once delivery semantics.
    /// </para>
    /// <para>
    /// <b>Logging:</b> All processing outcomes should be logged with sufficient
    /// context for debugging:
    /// <list type="bullet">
    /// <item>MessageId for correlation</item>
    /// <item>Scope information (ScopeType, ScopeId)</item>
    /// <item>Error details with stack traces for failures</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Payload Privacy:</b> When logging deserialization failures, avoid
    /// logging the full payload if it may contain sensitive user data.
    /// Consider logging only the first N characters or a hash of the payload.
    /// </para>
    /// </remarks>
    Task<ProcessResultEntity> ProcessAsync(
        byte[] payload,
        CancellationToken cancellationToken);
}
