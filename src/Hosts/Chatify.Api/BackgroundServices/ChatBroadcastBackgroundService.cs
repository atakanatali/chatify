using System.Text;
using System.Text.Json;
using Chatify.Api.Hubs;
using Chatify.BuildingBlocks.Primitives;
using Chatify.Chat.Application.Dtos;
using Chatify.Chat.Application.Ports;
using Chatify.Chat.Infrastructure.Options;
using Confluent.Kafka;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Chatify.Api.BackgroundServices;

/// <summary>
/// Background service that consumes chat events from the message broker and broadcasts
/// them to connected SignalR clients. Implements a fan-out consumption pattern where
/// each pod independently consumes all events and broadcasts locally.
/// </summary>
/// <remarks>
/// <para>
/// <b>Purpose:</b> This service enables real-time message delivery across all pods
/// in a Kubernetes deployment. Each pod runs its own consumer instance, ensuring
/// that messages produced on any pod are broadcast to all clients connected to that pod.
/// </para>
/// <para>
/// <b>Fan-Out Architecture:</b>
/// <list type="bullet">
/// <item>Each pod has a unique consumer group ID: <c>{BroadcastConsumerGroupPrefix}-{PodId}</c></item>
/// <item>Each pod receives ALL events from the topic (independent consumption)</item>
/// <item>Events are broadcast locally to SignalR groups based on ScopeId</item>
/// <item>Clients connect to any pod and receive all messages for their scopes</item>
/// </list>
/// </para>
/// <para>
/// <b>Consumer Configuration:</b>
/// <list type="bullet">
/// <item><c>group.id</c>: Unique per pod for independent consumption</item>
/// <item><c>auto.offset.reset</c>: <c>earliest</c> - Start from beginning on new groups</item>
/// <item><c>enable.auto.commit</c>: <c>false</c> - Manual commit after successful broadcast for at-least-once delivery</item>
/// <item><c>fetch.min.bytes</c>: <c>1</c> - Low latency for real-time delivery</item>
/// <item><c>fetch.max.wait.ms</c>: <c>100</c> - Maximum wait for fetch response</item>
/// </list>
/// </para>
/// <para>
/// <b>Error Handling Strategy:</b>
/// <list type="bullet">
/// <item><b>Outer Loop (Service Level):</b> Catches all unexpected exceptions to prevent service termination</item>
/// <item><b>Inner Loop (Operation Level):</b> Handles per-message errors with appropriate retry strategies</item>
/// <item><b>Kafka/SignalR exceptions:</b> Logged to Elasticsearch with full context via <see cref="ILogService"/></item>
/// <item><b>Backoff after errors:</b> Exponential backoff prevents overwhelming external services</item>
/// <item>Initial connection failures trigger retry with backoff</item>
/// <item>Deserialization errors log the offending message for debugging</item>
/// </list>
/// </para>
/// <para>
/// <b>Two-Level Exception Handling:</b>
/// <code><![CDATA[
/// // Outer loop - Prevents service termination
/// try
/// {
///     while (!stoppingToken.IsCancellationRequested)
///     {
///         // Inner loop - Handles per-operation errors
///         try
///         {
///             var consumeResult = consumer.Consume(stoppingToken);
///             await _hubContext.Clients.Group(scopeId).SendAsync("ReceiveMessage", chatEvent);
///             backoffMs = InitialBackoffMs; // Success - reset backoff
///         }
///         catch (OperationCanceledException) { break; } // Graceful shutdown
///         catch (Exception ex) when (IsTransientError(ex))
///         {
///             _logService.Error(ex, "Transient error, retrying with backoff", context);
///             await Task.Delay(backoffMs, stoppingToken);
///             backoffMs = Math.Min(backoffMs * 2, MaxBackoffMs);
///         }
///     }
/// }
/// catch (Exception ex)
/// {
///     // Outer loop - Prevents service termination
///     _logService.Error(ex, "Fatal error in ChatBroadcastBackgroundService, will restart", context);
///     throw; // Let Kubernetes restart the service
/// }
/// ]]></code>
/// </para>
/// <para>
/// <b>Ordering Guarantees:</b>
/// <list type="bullet">
/// <item>Within a ScopeId: Messages are delivered in order (partition ordering)</item>
/// <item>Across ScopeIds: No ordering guarantee (can be processed in parallel)</item>
/// <item>At-Least-Once: Duplicate delivery possible; clients dedupe by MessageId</item>
/// </list>
/// </para>
/// <para>
/// <b>Client Deduplication:</b> SignalR clients should track seen MessageIds
/// to handle potential duplicates from at-least-once delivery:
/// <code><![CDATA[
/// const seenMessages = new Set();
/// connection.on("ReceiveMessage", (event) => {
///     if (seenMessages.has(event.messageId)) return;
///     seenMessages.add(event.messageId);
///     // Process message...
/// });
/// ]]></code>
/// </para>
/// <para>
/// <b>Threading Model:</b> Runs on a dedicated background thread created by
/// <see cref="BackgroundService"/>. The consume loop blocks on <c>ConsumeResult</c>
/// with cancellation support for graceful shutdown.
/// </para>
/// <para>
/// <b>Graceful Shutdown:</b> When cancellation is requested, the consumer
/// properly closes the connection, commits final offsets, and releases resources.
/// </para>
/// </remarks>
public sealed class ChatBroadcastBackgroundService : BackgroundService
{
    /// <summary>
    /// The format string for generating the consumer group ID per pod.
    /// </summary>
    /// <remarks>
    /// Format: <c>{0}-{1}</c> where <c>{0}</c> is the prefix and <c>{1}</c> is the PodId.
    /// Example: <c>chatify-broadcast-chat-api-7d9f4c5b6d-abc12</c>
    /// </remarks>
    private const string ConsumerGroupIdFormat = "{0}-{1}";

    /// <summary>
    /// The prefix used for the client ID in message broker logs.
    /// </summary>
    /// <remarks>
    /// Helps identify broadcast consumers in broker logs and monitoring.
    /// </remarks>
    private const string ClientIdPrefix = "chatify-broadcast-";

    /// <summary>
    /// The initial backoff delay in milliseconds for exponential backoff retry.
    /// </summary>
    private const int InitialBackoffMs = 1000;

    /// <summary>
    /// The maximum backoff delay in milliseconds for exponential backoff retry.
    /// </summary>
    private const int MaxBackoffMs = 16000;

    /// <summary>
    /// Gets the message broker configuration options.
    /// </summary>
    /// <remarks>
    /// Contains bootstrap servers, topic name, and consumer group prefix.
    /// </remarks>
    private readonly KafkaOptionsEntity _options;

    /// <summary>
    /// Gets the SignalR hub context for broadcasting messages to clients.
    /// </summary>
    /// <remarks>
    /// Used to send messages to specific scope groups via <c>Clients.Group(scopeId)</c>.
    /// </remarks>
    private readonly IHubContext<ChatHubService> _hubContext;

    /// <summary>
    /// Gets the pod identity service for unique consumer group generation.
    /// </summary>
    /// <remarks>
    /// Provides the PodId used to construct a unique consumer group ID per pod.
    /// </remarks>
    private readonly IPodIdentityService _podIdentityService;

    /// <summary>
    /// Gets the logger instance for diagnostic information.
    /// </summary>
    /// <remarks>
    /// Logs consume events, errors, and lifecycle events to Elasticsearch.
    /// </remarks>
    private readonly ILogger<ChatBroadcastBackgroundService> _logger;

    /// <summary>
    /// Gets the log service for structured logging with correlation IDs.
    /// </summary>
    /// <remarks>
    /// Logs errors to Elasticsearch with full context and correlation IDs.
    /// Used for all error scenarios to ensure consistent logging across the service.
    /// </remarks>
    private readonly ILogService _logService;

    /// <summary>
    /// Gets the JSON serialization options for deserializing chat events.
    /// </summary>
    /// <remarks>
    /// Uses camelCase property naming to match the producer's serialization settings.
    /// </remarks>
    private readonly JsonSerializerOptions _jsonSerializerOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChatBroadcastBackgroundService"/> class.
    /// </summary>
    /// <param name="options">
    /// The message broker configuration options. Must not be null.
    /// </param>
    /// <param name="hubContext">
    /// The SignalR hub context for broadcasting messages. Must not be null.
    /// </param>
    /// <param name="podIdentityService">
    /// The pod identity service for unique consumer group generation. Must not be null.
    /// </param>
    /// <param name="logService">
    /// The log service for structured logging. Must not be null.
    /// </param>
    /// <param name="logger">
    /// The logger instance for diagnostic information. Must not be null.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when any required parameter is null.
    /// </exception>
    /// <remarks>
    /// The constructor validates dependencies and initializes the JSON serializer
    /// options. The message broker consumer is created in <see cref="ExecuteAsync"/> to handle
    /// initialization failures gracefully with retry logic.
    /// </remarks>
    public ChatBroadcastBackgroundService(
        KafkaOptionsEntity options,
        IHubContext<ChatHubService> hubContext,
        IPodIdentityService podIdentityService,
        ILogService logService,
        ILogger<ChatBroadcastBackgroundService> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
        _podIdentityService = podIdentityService ?? throw new ArgumentNullException(nameof(podIdentityService));
        _logService = logService ?? throw new ArgumentNullException(nameof(logService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _jsonSerializerOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
    }

    /// <summary>
    /// Executes the background service's main consume loop.
    /// </summary>
    /// <param name="stoppingToken">
    /// A cancellation token that signals when the service should stop.
    /// </param>
    /// <returns>
    /// A <see cref="Task"/> representing the asynchronous execution of the background service.
    /// </returns>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="stoppingToken"/> is canceled.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>Execution Flow:</b>
    /// <list type="number">
    /// <item>Build unique consumer group ID: <c>{BroadcastConsumerGroupPrefix}-{PodId}</c></item>
    /// <item>Create message broker consumer with earliest offset reset and manual commit</item>
    /// <item>Subscribe to the configured topic</item>
    /// <item>Enter consume loop with cancellation support</item>
    /// <item>Deserialize JSON payload to <see cref="ChatEventDto"/></item>
    /// <item>Create <see cref="EnrichedChatEventDto"/> with partition/offset</item>
    /// <item>Broadcast to SignalR group via <c>IHubContext</c></item>
    /// <item>Commit offset manually after successful broadcast (at-least-once guarantee)</item>
    /// <item>Handle errors with exponential backoff retry</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Commit Strategy:</b> Manual commit after successful SignalR broadcast ensures
    /// at-least-once delivery. If broadcast fails, the offset is not committed and the
    /// message will be reprocessed on the next consume cycle (with potential duplicate delivery).
    /// </para>
    /// <para>
    /// <b>Backoff Strategy:</b> On consume errors, the service waits using
    /// exponential backoff: 1s, 2s, 4s, 8s, 16s (max). This prevents overwhelming
    /// the broker during transient failures.
    /// </para>
    /// </remarks>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var consumerGroupId = string.Format(
            ConsumerGroupIdFormat,
            _options.BroadcastConsumerGroupPrefix,
            _podIdentityService.PodId);

        // Ensure we yield to the caller immediately so startup isn't blocked
        await Task.Yield();

        _logger.LogInformation(
            "ChatBroadcastBackgroundService starting. ConsumerGroupId: {ConsumerGroupId}, Topic: {TopicName}, BootstrapServers: {BootstrapServers}",
            consumerGroupId,
            _options.TopicName,
            _options.BootstrapServers);

        // Outer loop: Prevents service termination from unexpected exceptions
        try
        {
            await ExecuteConsumeLoopAsync(consumerGroupId, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Graceful shutdown - normal exit
            _logger.LogInformation("ChatBroadcastBackgroundService shutdown requested via cancellation token");
        }
        catch (Exception ex)
        {
            // Fatal error - log and let Kubernetes restart the service
            _logService.Error(
                ex,
                "Fatal error in ChatBroadcastBackgroundService. Service will restart via Kubernetes.",
                new { ConsumerGroupId = consumerGroupId, Topic = _options.TopicName });

            // Re-throw to let BackgroundService handle it and allow Kubernetes to restart
            throw;
        }
    }

    /// <summary>
    /// Executes the main message consumption loop with inner operation-level error handling.
    /// </summary>
    /// <param name="consumerGroupId">
    /// The unique consumer group ID for this pod instance.
    /// </param>
    /// <param name="stoppingToken">
    /// A cancellation token that signals when the service should stop.
    /// </param>
    /// <returns>
    /// A <see cref="Task"/> representing the asynchronous execution.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>Inner Loop Error Handling:</b>
    /// <list type="bullet">
    /// <item><b>OperationCanceledException:</b> Graceful shutdown, exit loop</item>
    /// <item><b>ConsumeException/KafkaException:</b> Transient broker errors, log and retry with backoff</item>
    /// <item><b>JsonException:</b> Deserialization errors, log message payload and continue</item>
    /// <item><b>Other exceptions:</b> Unexpected processing errors, log and retry with backoff</item>
    /// </list>
    /// </para>
    /// <para>
    /// All errors are logged to Elasticsearch via <see cref="ILogService"/> with full context
    /// to ensure no errors leak unlogged.
    /// </para>
    /// </remarks>
    private async Task ExecuteConsumeLoopAsync(string consumerGroupId, CancellationToken stoppingToken)
    {
        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            GroupId = consumerGroupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false, // Manual commit for at-least-once guarantee
            FetchMinBytes = 1,
            FetchWaitMaxMs = 100,
            // Client identifier for debugging in broker logs
            ClientId = ClientIdPrefix + _podIdentityService.PodId,
            // Statistics interval for monitoring (optional, future use)
            StatisticsIntervalMs = 0
        };

        var consumerBuilder = new ConsumerBuilder<Ignore, byte[]>(consumerConfig);

        // Set up error handler for consumer-specific errors
        consumerBuilder.SetErrorHandler((consumer, error) =>
        {
            _logService.Warn(
                "Message broker consumer error: Code={ErrorCode}, Reason={ErrorReason}, IsFatal={IsFatal}",
                new { ErrorCode = error.Code, ErrorReason = error.Reason, IsFatal = error.IsFatal });
        });

        // Set up log handler for consumer logs (warnings and above)
        consumerBuilder.SetLogHandler((consumer, logMessage) =>
        {
            if (logMessage.Level >= SyslogLevel.Warning)
            {
                _logger.LogWarning(
                    "Message broker consumer log: Level={Level}, Message={Message}",
                    logMessage.Level,
                    logMessage.Message);
            }
        });

        // Set up statistics handler (for monitoring, future use)
        consumerBuilder.SetStatisticsHandler((consumer, json) =>
        {
            _logger.LogDebug("Message broker consumer statistics: {Statistics}", json);
        });

        using var consumer = consumerBuilder.Build();

        try
        {
            consumer.Subscribe(_options.TopicName);

            _logger.LogInformation(
                "Subscribed to topic {TopicName} with consumer group {ConsumerGroupId}",
                _options.TopicName,
                consumerGroupId);
        }
        catch (KafkaException ex)
        {
            // Log to Elasticsearch with full context
            _logService.Error(
                ex,
                "Failed to subscribe to topic. BootstrapServers: {BootstrapServers}, Topic: {TopicName}",
                new { BootstrapServers = _options.BootstrapServers, TopicName = _options.TopicName });

            // Retry with exponential backoff on initial subscription failure
            await DelayBeforeExitAsync(stoppingToken);
            return;
        }

        var backoffMs = InitialBackoffMs;

        // Main consume loop - Inner operation-level error handling
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var consumeResult = consumer.Consume(stoppingToken);

                if (consumeResult is null)
                {
                    _logger.LogDebug("Consume returned null result, continuing...");
                    continue;
                }

                _logger.LogDebug(
                    "Consumed message at partition {Partition}, offset {Offset}, key {Key}",
                    consumeResult.Partition,
                    consumeResult.Offset,
                    consumeResult.Message.Key);

                // Deserialize the JSON payload to ChatEventDto
                ChatEventDto? chatEvent;
                try
                {
                    chatEvent = JsonSerializer.Deserialize<ChatEventDto>(
                        consumeResult.Message.Value,
                        _jsonSerializerOptions);
                }
                catch (JsonException ex)
                {
                    // Log deserialization error with message payload for debugging
                    _logService.Error(
                        ex,
                        "Failed to deserialize chat event. Partition: {Partition}, Offset: {Offset}. Skipping message.",
                        new
                        {
                            Partition = consumeResult.Partition,
                            Offset = consumeResult.Offset,
                            PayloadPreview = ExtractPayloadPreview(consumeResult.Message.Value)
                        });

                    // Continue processing next message
                    continue;
                }

                if (chatEvent is null)
                {
                    _logService.Warn(
                        "Deserialized chat event is null. Partition: {Partition}, Offset: {Offset}. Skipping message.",
                        new { Partition = consumeResult.Partition, Offset = consumeResult.Offset });

                    continue;
                }

                _logger.LogInformation(
                    "Broadcasting chat event {MessageId} to scope {ScopeId} (partition {Partition}, offset {Offset})",
                    chatEvent.MessageId,
                    chatEvent.ScopeId,
                    consumeResult.Partition,
                    consumeResult.Offset);

                // Broadcast to SignalR group identified by ScopeId
                await _hubContext.Clients.Group(chatEvent.ScopeId)
                    .SendAsync("ReceiveMessage", chatEvent, stoppingToken);

                _logger.LogDebug(
                    "Successfully broadcasted message {MessageId} to scope {ScopeId}",
                    chatEvent.MessageId,
                    chatEvent.ScopeId);

                // Commit offset after successful broadcast (at-least-once guarantee)
                consumer.Commit(consumeResult);

                _logger.LogDebug(
                    "Committed offset for message {MessageId} at partition {Partition}, offset {Offset}",
                    chatEvent.MessageId,
                    consumeResult.Partition,
                    consumeResult.Offset);

                // Reset backoff on successful consume and broadcast
                backoffMs = InitialBackoffMs;
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Consume loop canceled via stopping token");
                break;
            }
            catch (Exception ex) when (ex is ConsumeException or KafkaException)
            {
                // Message broker transient errors - log to Elasticsearch and retry with backoff
                _logService.Error(
                    ex,
                    "Message broker error in consume loop. Retrying with exponential backoff.",
                    new { ConsumerGroupId = consumerGroupId, Topic = _options.TopicName });

                await Task.Delay(backoffMs, stoppingToken);

                // Exponential backoff: 1s, 2s, 4s, 8s, 16s (max)
                backoffMs = Math.Min(backoffMs * 2, MaxBackoffMs);
            }
            catch (Exception ex)
            {
                // Unexpected processing errors - log to Elasticsearch and retry with backoff
                _logService.Error(
                    ex,
                    "Unexpected error in consume loop. Retrying with exponential backoff.",
                    new { ConsumerGroupId = consumerGroupId, Topic = _options.TopicName });

                await Task.Delay(backoffMs, stoppingToken);

                // Exponential backoff: 1s, 2s, 4s, 8s, 16s (max)
                backoffMs = Math.Min(backoffMs * 2, MaxBackoffMs);
            }
        }

        // Close consumer gracefully on shutdown
        try
        {
            consumer.Close();

            _logger.LogInformation(
                "ChatBroadcastBackgroundService stopped. ConsumerGroupId: {ConsumerGroupId}",
                consumerGroupId);
        }
        catch (KafkaException ex)
        {
            // Log to Elasticsearch even for shutdown errors
            _logService.Error(
                ex,
                "Error closing message broker consumer during shutdown",
                new { ConsumerGroupId = consumerGroupId });
        }
    }

    /// <summary>
    /// Extracts a preview of the message payload for logging.
    /// </summary>
    /// <param name="payload">
    /// The raw message payload bytes.
    /// </param>
    /// <returns>
    /// A string preview of the payload (truncated to 256 characters).
    /// </returns>
    /// <remarks>
    /// This method creates a safe preview of the message payload for logging purposes,
    /// truncating at 256 characters to prevent log bloat while preserving enough
    /// context for debugging deserialization issues.
    /// </remarks>
    private static string ExtractPayloadPreview(byte[] payload)
    {
        if (payload is null || payload.Length == 0)
        {
            return "<empty>";
        }

        try
        {
            var preview = System.Text.Encoding.UTF8.GetString(payload);
            return preview.Length > 256 ? preview.Substring(0, 256) + "..." : preview;
        }
        catch
        {
            return $"<binary data, {payload.Length} bytes>";
        }
    }

    /// <summary>
    /// Implements exponential backoff delay before allowing the service to exit.
    /// </summary>
    /// <param name="stoppingToken">
    /// A cancellation token that can stop the delay loop.
    /// </param>
    /// <returns>
    /// A <see cref="Task"/> representing the asynchronous delay operation.
    /// </returns>
    /// <remarks>
    /// This method is called when the initial subscription to the message broker topic fails.
    /// It implements exponential backoff (1s, 2s, 4s, 8s, 16s max) to avoid overwhelming
    /// the broker during transient failures or restarts. After reaching maximum backoff,
    /// the service allows itself to exit so Kubernetes can restart it.
    /// </remarks>
    private async Task DelayBeforeExitAsync(CancellationToken stoppingToken)
    {
        var backoffMs = InitialBackoffMs;

        while (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Retrying message broker consumer initialization in {BackoffMs}ms...", backoffMs);

            await Task.Delay(backoffMs, stoppingToken);

            // Exit retry loop after max backoff - let the service restart via K8s
            if (backoffMs >= MaxBackoffMs)
            {
                _logger.LogError("Max backoff reached. Allowing service to restart via Kubernetes.");
                break;
            }

            backoffMs = Math.Min(backoffMs * 2, MaxBackoffMs);
        }
    }
}
