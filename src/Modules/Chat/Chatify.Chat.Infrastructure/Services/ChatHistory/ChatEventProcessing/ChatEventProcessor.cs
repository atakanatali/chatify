using System.Text;
using System.Text.Json;
using Chatify.Chat.Application.Dtos;
using Chatify.Chat.Application.Ports;
using Chatify.Chat.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace Chatify.Chat.Infrastructure.Services.ChatHistory.ChatEventProcessing;

/// <summary>
/// Default implementation of <see cref="IChatEventProcessor"/> that handles
/// deserialization, validation, and persistence of chat events with retry logic.
/// </summary>
/// <remarks>
/// <para>
/// <b>Purpose:</b> This processor transforms raw message payloads from the
/// message broker into persisted chat events in ScyllaDB. It implements
/// comprehensive error handling with separate strategies for transient and
/// permanent failures.
/// </para>
/// <para>
/// <b>Error Handling Strategy:</b>
/// <list type="bullet">
/// <item><b>Permanent Errors:</b> Deserialization failures, null payloads,
/// and validation errors return <see cref="ProcessResultEntity.PermanentFailure"/>.
/// These errors will not succeed on retry.</item>
/// <item><b>Transient Errors:</b> Database connectivity issues, timeouts,
/// and temporary unavailability trigger retry via Polly. The caller should
/// catch these exceptions and implement backoff.</item>
/// </list>
/// </para>
/// <para>
/// <b>Retry Policy:</b> Database operations are wrapped in a Polly retry
/// policy that implements exponential backoff with jitter. The policy is
/// configured via <see cref="ChatHistoryWriterOptionsEntity"/>.
/// </para>
/// <para>
/// <b>Idempotency:</b> The processor relies on the repository's lightweight
/// transaction (INSERT IF NOT EXISTS) for idempotent writes. Duplicate messages
/// are silently ignored by the database.
/// </para>
/// </remarks>
public sealed class ChatEventProcessor : IChatEventProcessor
{
    /// <summary>
    /// Gets the chat history repository for persisting messages.
    /// </summary>
    private readonly IChatHistoryRepository _historyRepository;

    /// <summary>
    /// Gets the logger instance for diagnostic information.
    /// </summary>
    private readonly ILogger<ChatEventProcessor> _logger;

    /// <summary>
    /// Gets the JSON serialization options for deserializing chat events.
    /// </summary>
    private readonly JsonSerializerOptions _jsonSerializerOptions;

    /// <summary>
    /// Gets the Polly retry policy for database write operations.
    /// </summary>
    private readonly AsyncRetryPolicy _databaseRetryPolicy;

    /// <summary>
    /// Gets the configuration options for the history writer.
    /// </summary>
    private readonly ChatHistoryWriterOptionsEntity _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChatEventProcessor"/> class.
    /// </summary>
    /// <param name="historyRepository">
    /// The chat history repository for persisting messages. Must not be null.
    /// </param>
    /// <param name="options">
    /// The configuration options for retry behavior. Must not be null.
    /// </param>
    /// <param name="logger">
    /// The logger instance for diagnostic information. Must not be null.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when any required parameter is null.
    /// </exception>
    public ChatEventProcessor(
        IChatHistoryRepository historyRepository,
        ChatHistoryWriterOptionsEntity options,
        ILogger<ChatEventProcessor> logger)
    {
        _historyRepository = historyRepository ?? throw new ArgumentNullException(nameof(historyRepository));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _jsonSerializerOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        // Configure Polly retry policy for database operations
        // Uses exponential backoff with jitter to prevent thundering herd
        _databaseRetryPolicy = Policy
            .Handle<Exception>(IsTransientDatabaseError)
            .WaitAndRetryAsync(
                retryCount: _options.DatabaseRetryMaxAttempts,
                sleepDurationProvider: retryAttempt =>
                {
                    // Exponential backoff: base * 2^(attempt-1)
                    var baseDelay = TimeSpan.FromMilliseconds(
                        _options.DatabaseRetryBaseDelayMs * Math.Pow(2, retryAttempt - 1));

                    // Cap at maximum delay
                    var cappedDelay = baseDelay.TotalMilliseconds > _options.DatabaseRetryMaxDelayMs
                        ? TimeSpan.FromMilliseconds(_options.DatabaseRetryMaxDelayMs)
                        : baseDelay;

                    // Add jitter to prevent synchronized retries
                    var jitter = TimeSpan.FromMilliseconds(
                        Random.Shared.NextDouble() * _options.DatabaseRetryJitterMs);

                    return cappedDelay + jitter;
                },
                onRetry: (exception, timespan, retryAttempt, context) =>
                {
                    context.TryGetValue("MessageId", out var messageIdObj);
                    var messageId = messageIdObj?.ToString() ?? "Unknown";

                    _logger.LogWarning(
                        exception,
                        "Database write failed for message {MessageId}. Retry {RetryAttempt}/{MaxRetries} after {DelayMs}ms",
                        messageId,
                        retryAttempt,
                        _options.DatabaseRetryMaxAttempts,
                        timespan.TotalMilliseconds);
                });
    }

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
    /// A <see cref="Task{ProcessResultEntity}"/> that completes with the processing result.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="payload"/> is null.
    /// </exception>
    /// <exception cref="Exception">
    /// Thrown for transient database errors after all retry attempts are exhausted.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>Processing Flow:</b>
    /// <list type="number">
    /// <item>Validate payload is not null or empty</item>
    /// <item>Deserialize JSON to <see cref="ChatEventDto"/></item>
    /// <item>Validate deserialized event (not null, required fields present)</item>
    /// <item>Persist to database via <see cref="IChatHistoryRepository.AppendAsync"/> with Polly retry</item>
    /// <item>Return <see cref="ProcessResultEntity.Success"/> on success</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Permanent Failure Handling:</b>
    /// <list type="bullet">
    /// <item>Null or empty payload → <see cref="ProcessResultEntity.PermanentFailure"/></item>
    /// <item>JSON deserialization exception → <see cref="ProcessResultEntity.PermanentFailure"/></item>
    /// <item>Null deserialized event → <see cref="ProcessResultEntity.PermanentFailure"/></item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Transient Failure Handling:</b>
    /// <list type="bullet">
    /// <item>Database connection errors → Retry via Polly</item>
    /// <item>Database timeouts → Retry via Polly</item>
    /// <item>After max retries → Exception thrown (caller should backoff)</item>
    /// </list>
    /// </para>
    /// </remarks>
    public Task<ProcessResultEntity> ProcessAsync(
        byte[] payload,
        CancellationToken cancellationToken)
    {
        if (payload == null || payload.Length == 0)
        {
            _logger.LogWarning("Received null or empty payload");
            return Task.FromResult(ProcessResultEntity.PermanentFailure);
        }

        // Deserialize JSON payload to ChatEventDto
        ChatEventDto? chatEvent;
        try
        {
            chatEvent = JsonSerializer.Deserialize<ChatEventDto>(
                payload,
                _jsonSerializerOptions);
        }
        catch (JsonException ex)
        {
            // Log only first N bytes to avoid PII exposure
            var payloadPreview = GetPayloadPreview(payload);

            _logger.LogError(
                ex,
                "Failed to deserialize chat event. Payload preview (first {PreviewBytes} bytes): {PayloadPreview}",
                _options.MaxPayloadLogBytes,
                payloadPreview);

            return Task.FromResult(ProcessResultEntity.PermanentFailure);
        }

        if (chatEvent == null)
        {
            _logger.LogWarning("Deserialized chat event is null");
            return Task.FromResult(ProcessResultEntity.PermanentFailure);
        }

        // Validate required fields (basic validation)
        if (string.IsNullOrWhiteSpace(chatEvent.SenderId))
        {
            _logger.LogWarning("Chat event {MessageId} has null or empty SenderId", chatEvent.MessageId);
            return Task.FromResult(ProcessResultEntity.PermanentFailure);
        }

        if (string.IsNullOrWhiteSpace(chatEvent.Text))
        {
            _logger.LogWarning("Chat event {MessageId} has null or empty Text", chatEvent.MessageId);
            return Task.FromResult(ProcessResultEntity.PermanentFailure);
        }

        // Persist to database with Polly retry for transient errors
        var contextData = new Dictionary<string, object>
        {
            ["MessageId"] = chatEvent.MessageId.ToString()
        };

        // Execute with retry policy - this will throw on transient failure after max retries
        return _databaseRetryPolicy.ExecuteAsync(async (ctx, ct) =>
        {
            await _historyRepository.AppendAsync(chatEvent, ct);

            _logger.LogInformation(
                "Successfully persisted message {MessageId} for scope {ScopeType}:{ScopeId}",
                chatEvent.MessageId,
                chatEvent.ScopeType,
                chatEvent.ScopeId);

            return ProcessResultEntity.Success;
        }, contextData, cancellationToken);
    }

    /// <summary>
    /// Determines if an exception represents a transient database error that should be retried.
    /// </summary>
    /// <param name="ex">
    /// The exception to evaluate.
    /// </param>
    /// <returns>
    /// <c>true</c> if the exception is transient and should be retried; otherwise, <c>false</c>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>Transient Errors:</b> These are temporary failures that may resolve on retry:
    /// <list type="bullet">
    /// <item>Network issues (timeouts, connection failures)</item>
    /// <item>Temporary database unavailability</item>
    /// <item>Query timeouts under high load</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Permanent Errors:</b> These should not be retried:
    /// <list type="bullet">
    /// <item>Invalid data types (deserialization errors - already handled before this point)</item>
    /// <item>Authentication/authorization failures (won't resolve on retry)</item>
    /// <item>Schema mismatches (require intervention)</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Note:</b> This is a basic implementation. For production, consider using
    /// specific exception types from the Cassandra driver rather than string matching.
    /// </para>
    /// </remarks>
    private static bool IsTransientDatabaseError(Exception ex)
    {
        // Timeout exceptions are always transient
        if (ex is TimeoutException)
        {
            return true;
        }

        // Check for Cassandra/ScyllaDB specific transient errors
        // Note: In production, use exception type checks instead of string matching
        var message = ex.Message;
        if (message.Contains("No host available", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Query timeout", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Connection", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Check inner exception
        if (ex.InnerException is not null)
        {
            var innerMessage = ex.InnerException.Message;
            if (innerMessage.Contains("No host available", StringComparison.OrdinalIgnoreCase) ||
                innerMessage.Contains("Query timeout", StringComparison.OrdinalIgnoreCase) ||
                innerMessage.Contains("Connection", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Gets a safe preview of the payload for logging.
    /// </summary>
    /// <param name="payload">
    /// The payload bytes.
    /// </param>
    /// <returns>
    /// A string representation of the first N bytes of the payload, truncated
    /// to the configured maximum log size.
    /// </returns>
    /// <remarks>
    /// This method limits the amount of data logged to protect user privacy
    /// while still providing useful debugging information.
    /// </remarks>
    private string GetPayloadPreview(byte[] payload)
    {
        var length = Math.Min(payload.Length, _options.MaxPayloadLogBytes);
        var previewBytes = new byte[length];
        Array.Copy(payload, previewBytes, length);

        var preview = Encoding.UTF8.GetString(previewBytes);

        // Truncate if still too long (multi-byte characters)
        if (preview.Length > _options.MaxPayloadLogBytes)
        {
            preview = preview.Substring(0, _options.MaxPayloadLogBytes);
        }

        return preview + (payload.Length > _options.MaxPayloadLogBytes ? "..." : string.Empty);
    }
}
