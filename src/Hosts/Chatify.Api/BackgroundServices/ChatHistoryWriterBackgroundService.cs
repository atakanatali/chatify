using Chatify.BuildingBlocks.Primitives;
using Chatify.BuildingBlocks.Resilience;
using Chatify.Chat.Application.Ports;
using Chatify.Chat.Infrastructure.Options;
using Chatify.Chat.Infrastructure.Services.ChatHistory.ChatEventProcessing;
using Chatify.Chat.Infrastructure.Services.Consumers;
using Confluent.Kafka;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Chatify.Api.BackgroundServices;

/// <summary>
/// Background service that consumes chat events from the message broker and persists
/// them to ScyllaDB for durable chat history storage. Implements a shared consumer
/// group pattern where multiple pod instances share the message processing load.
/// </summary>
/// <remarks>
/// <para>
/// <b>Purpose:</b> This service enables asynchronous persistence of chat events
/// to the distributed database (ScyllaDB). Each pod participates in a shared consumer
/// group, allowing the workload to be distributed across all instances while ensuring
/// each message is processed exactly once.
/// </para>
/// <para>
/// <b>Architecture:</b> This background service follows SOLID principles by delegating
/// responsibilities to specialized components:
/// <list type="bullet">
/// <item><see cref="IConsumerFactory"/> - Creates and configures message broker consumers</item>
/// <item><see cref="IChatEventProcessor"/> - Handles deserialization, validation, and persistence</item>
/// <item><see cref="ExponentialBackoff"/> - Manages retry delays for transient errors</item>
/// <item><see cref="ILogService"/> - Logs all errors to Elasticsearch with correlation IDs</item>
/// </list>
/// </para>
/// <para>
/// <b>Shared Consumer Group Architecture:</b>
/// <list type="bullet">
/// <item>Group ID: Configured via <see cref="ChatHistoryWriterOptionsEntity.ConsumerGroupId"/> (shared across all pods)</item>
/// <item>Each pod receives a subset of partitions based on Kafka's consumer group rebalancing</item>
/// <item>Messages are persisted to ScyllaDB with exactly-once semantics (idempotent writes)</item>
/// <item>Offsets are committed after successful persistence (at-least-once delivery)</item>
/// </list>
/// </para>
/// <para>
/// <b>Consumer Configuration:</b>
/// <list type="bullet">
/// <item><c>group.id</c>: From options - Shared consumer group ID</item>
/// <item><c>auto.offset.reset</c>: <c>earliest</c> - Start from beginning on new groups</item>
/// <item><c>enable.auto.commit</c>: <c>false</c> - Manual commit after successful persistence</item>
/// <item><c>fetch.min.bytes</c>: <c>1</c> - Low latency for real-time processing</item>
/// <item><c>fetch.max.wait.ms</c>: <c>100</c> - Maximum wait for fetch response</item>
/// </list>
/// </para>
/// <para>
/// <b>Error Handling Strategy:</b>
/// <list type="bullet">
/// <item><b>Outer Loop (Service Level):</b> Catches all unexpected exceptions to prevent service termination</item>
/// <item><b>Inner Loop (Operation Level):</b> Handles per-message errors with appropriate retry strategies</item>
/// <item><b>Kafka/Redis/Scylla exceptions:</b> Logged to Elasticsearch with full context via <see cref="ILogService"/></item>
/// <item><b>Backoff after errors:</b> Exponential backoff prevents overwhelming external services</item>
/// <item><b>Permanent failures:</b> Processor returns <see cref="ProcessResultEntity.PermanentFailure"/>;
/// offset is committed to prevent poison message replay</item>
/// <item><b>Transient failures:</b> Processor throws exception; caught by inner loop and retried with backoff</item>
/// <item><b>Commit exceptions:</b> Logged but do not prevent consumption of next message</item>
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
///             await _processor.ProcessAsync(consumeResult.Message.Value, stoppingToken);
///             backoff.Reset(); // Success - reset backoff
///         }
///         catch (OperationCanceledException) { break; } // Graceful shutdown
///         catch (Exception ex) when (IsTransientError(ex))
///         {
///             _logService.Error(ex, "Transient error, retrying with backoff", context);
///             await Task.Delay(backoff.NextDelayWithJitter(), stoppingToken);
///         }
///         catch (Exception ex)
///         {
///             _logService.Error(ex, "Unexpected operation error, retrying with backoff", context);
///             await Task.Delay(backoff.NextDelayWithJitter(), stoppingToken);
///         }
///     }
/// }
/// catch (Exception ex)
/// {
///     // Outer loop - Prevents service termination
///     _logService.Error(ex, "Fatal error in ChatHistoryWriterBackgroundService, will restart", context);
///     throw; // Let Kubernetes restart the service
/// }
/// ]]></code>
/// </para>
/// <para>
/// <b>Poison Message Handling:</b> When the processor returns
/// <see cref="ProcessResultEntity.PermanentFailure"/>, the service commits the offset
/// to prevent infinite replay. This prevents the consumer from being stuck on a
/// malformed message. A future enhancement should write these messages to a
/// dead-letter queue (DLQ) topic for later analysis.
/// </para>
/// <para>
/// <b>Threading Model:</b> Runs on a dedicated background thread created by
/// <see cref="BackgroundService"/>. The consume loop blocks on <c>ConsumeResult</c>
/// with cancellation support for graceful shutdown.
/// </para>
/// <para>
/// <b>Graceful Shutdown:</b> When cancellation is requested, the consumer
/// properly closes the connection and releases resources. Offsets are committed
/// before shutdown for messages that were successfully processed.
/// </para>
/// </remarks>
public sealed class ChatHistoryWriterBackgroundService : BackgroundService
{
    /// <summary>
    /// Gets the message broker configuration options.
    /// </summary>
    private readonly KafkaOptionsEntity _brokerOptions;

    /// <summary>
    /// Gets the history writer configuration options.
    /// </summary>
    private readonly ChatHistoryWriterOptionsEntity _options;

    /// <summary>
    /// Gets the factory for creating message broker consumers.
    /// </summary>
    private readonly IConsumerFactory _consumerFactory;

    /// <summary>
    /// Gets the processor for handling chat events.
    /// </summary>
    private readonly IChatEventProcessor _processor;

    /// <summary>
    /// Gets the pod identity service for unique client identification.
    /// </summary>
    private readonly IPodIdentityService _podIdentityService;

    /// <summary>
    /// Gets the logger instance for diagnostic information.
    /// </summary>
    private readonly ILogger<ChatHistoryWriterBackgroundService> _logger;

    /// <summary>
    /// Gets the log service for structured logging with correlation IDs.
    /// </summary>
    /// <remarks>
    /// Logs errors to Elasticsearch with full context and correlation IDs.
    /// Used for all error scenarios to ensure consistent logging across the service.
    /// </remarks>
    private readonly ILogService _logService;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChatHistoryWriterBackgroundService"/> class.
    /// </summary>
    /// <param name="brokerOptions">
    /// The message broker configuration options. Must not be null.
    /// </param>
    /// <param name="options">
    /// The history writer configuration options. Must not be null.
    /// </param>
    /// <param name="consumerFactory">
    /// The factory for creating message broker consumers. Must not be null.
    /// </param>
    /// <param name="processor">
    /// The processor for handling chat events. Must not be null.
    /// </param>
    /// <param name="podIdentityService">
    /// The pod identity service for unique client identification. Must not be null.
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
    public ChatHistoryWriterBackgroundService(
        KafkaOptionsEntity brokerOptions,
        ChatHistoryWriterOptionsEntity options,
        IConsumerFactory consumerFactory,
        IChatEventProcessor processor,
        IPodIdentityService podIdentityService,
        ILogService logService,
        ILogger<ChatHistoryWriterBackgroundService> logger)
    {
        _brokerOptions = brokerOptions ?? throw new ArgumentNullException(nameof(brokerOptions));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _consumerFactory = consumerFactory ?? throw new ArgumentNullException(nameof(consumerFactory));
        _processor = processor ?? throw new ArgumentNullException(nameof(processor));
        _podIdentityService = podIdentityService ?? throw new ArgumentNullException(nameof(podIdentityService));
        _logService = logService ?? throw new ArgumentNullException(nameof(logService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
    /// <item>Create message broker consumer via factory with shared group ID and manual commit</item>
    /// <item>Subscribe to the configured topic</item>
    /// <item>Enter consume loop with cancellation support</item>
    /// <item>For each message: call <see cref="IChatEventProcessor.ProcessAsync"/></item>
    /// <item>On success: commit offset to mark message as processed</item>
    /// <item>On permanent failure: commit offset to prevent poison message replay</item>
    /// <item>On transient error: catch exception and apply backoff before retry</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Commit Strategy:</b>
    /// <list type="bullet">
    /// <item><b>Success:</b> Commit offset after successful processing (at-least-once guarantee)</item>
    /// <item><b>Permanent Failure:</b> Commit offset to skip poison message (prevents infinite replay)</item>
    /// <item><b>Transient Error:</b> Do NOT commit; message will be redelivered on next consume</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Backoff Strategy:</b> On consume/processing errors, the service uses
    /// <see cref="ExponentialBackoff"/> with jitter to prevent overwhelming the broker
    /// or database during transient failures.
    /// </para>
    /// </remarks>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "ChatHistoryWriterBackgroundService starting. ConsumerGroupId: {ConsumerGroupId}, Topic: {TopicName}, Options: {Options}",
            _options.ConsumerGroupId,
            _brokerOptions.TopicName,
            _options);

        // Outer loop: Prevents service termination from unexpected exceptions
        try
        {
            await ExecuteConsumeLoopAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Graceful shutdown - normal exit
            _logger.LogInformation("ChatHistoryWriterBackgroundService shutdown requested via cancellation token");
        }
        catch (Exception ex)
        {
            // Fatal error - log and let Kubernetes restart the service
            _logService.Error(
                ex,
                "Fatal error in ChatHistoryWriterBackgroundService. Service will restart via Kubernetes.",
                new { ConsumerGroupId = _options.ConsumerGroupId, Topic = _brokerOptions.TopicName });

            // Re-throw to let BackgroundService handle it and allow Kubernetes to restart
            throw;
        }
    }

    /// <summary>
    /// Executes the main message consumption loop with inner operation-level error handling.
    /// </summary>
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
    /// <item><b>Other exceptions:</b> Unexpected processing errors, log and retry with backoff</item>
    /// </list>
    /// </para>
    /// <para>
    /// All errors are logged to Elasticsearch via <see cref="ILogService"/> with full context
    /// to ensure no errors leak unlogged.
    /// </para>
    /// </remarks>
    private async Task ExecuteConsumeLoopAsync(CancellationToken stoppingToken)
    {
        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = _brokerOptions.BootstrapServers,
            GroupId = _options.ConsumerGroupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false, // Manual commit for at-least-once guarantee
            FetchMinBytes = 1,
            FetchWaitMaxMs = 100,
            // Client identifier for debugging in broker logs
            ClientId = _options.ClientIdPrefix + _podIdentityService.PodId,
            // Statistics interval for monitoring (optional, future use)
            StatisticsIntervalMs = 0
        };

        using var consumer = _consumerFactory.Create(consumerConfig);

        try
        {
            consumer.Subscribe(_brokerOptions.TopicName);

            _logger.LogInformation(
                "Subscribed to topic {TopicName} with consumer group {ConsumerGroupId}",
                _brokerOptions.TopicName,
                _options.ConsumerGroupId);
        }
        catch (KafkaException ex)
        {
            // Log to Elasticsearch with full context
            _logService.Error(
                ex,
                "Failed to subscribe to topic. BootstrapServers: {BootstrapServers}, Topic: {TopicName}",
                new { BootstrapServers = _brokerOptions.BootstrapServers, TopicName = _brokerOptions.TopicName });

            // Retry with exponential backoff on initial subscription failure
            await DelayBeforeExitAsync(stoppingToken);
            return;
        }

        var backoff = new ExponentialBackoff(
            initial: TimeSpan.FromMilliseconds(_options.ConsumerBackoffInitialMs),
            max: TimeSpan.FromMilliseconds(_options.ConsumerBackoffMaxMs));

        // Main consume loop - Inner operation-level error handling
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var consumeResult = consumer.Consume(stoppingToken);

                if (consumeResult is null || consumeResult.Message.Value is null)
                {
                    _logger.LogDebug("Consume returned null result or empty payload, continuing...");
                    continue;
                }

                _logger.LogDebug(
                    "Consumed message at partition {Partition}, offset {Offset}",
                    consumeResult.Partition,
                    consumeResult.Offset);

                // Process the message via the processor
                var result = await _processor.ProcessAsync(consumeResult.Message.Value, stoppingToken);

                if (result == ProcessResultEntity.Success)
                {
                    // Commit offset on success
                    TryCommit(consumer, consumeResult);

                    // Reset backoff on success
                    backoff.Reset();
                }
                else if (result == ProcessResultEntity.PermanentFailure)
                {
                    // Permanent failure: commit offset to prevent poison message replay
                    _logService.Warn(
                        "Permanent failure processing message. Partition: {Partition}, Offset: {Offset}. Committing offset to skip message.",
                        new { Partition = consumeResult.Partition, Offset = consumeResult.Offset });

                    TryCommit(consumer, consumeResult);

                    // Reset backoff - we successfully handled this failure
                    backoff.Reset();
                }
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
                    new { ConsumerGroupId = _options.ConsumerGroupId, Topic = _brokerOptions.TopicName });

                await Task.Delay(backoff.NextDelayWithJitter(), stoppingToken);
            }
            catch (Exception ex)
            {
                // Unexpected processing errors - log to Elasticsearch and retry with backoff
                _logService.Error(
                    ex,
                    "Unexpected error in consume loop. Retrying with exponential backoff.",
                    new { ConsumerGroupId = _options.ConsumerGroupId, Topic = _brokerOptions.TopicName });

                await Task.Delay(backoff.NextDelayWithJitter(), stoppingToken);
            }
        }

        // Close consumer gracefully on shutdown
        try
        {
            consumer.Close();

            _logger.LogInformation(
                "ChatHistoryWriterBackgroundService stopped. ConsumerGroupId: {ConsumerGroupId}",
                _options.ConsumerGroupId);
        }
        catch (KafkaException ex)
        {
            // Log to Elasticsearch even for shutdown errors
            _logService.Error(
                ex,
                "Error closing message broker consumer during shutdown",
                new { ConsumerGroupId = _options.ConsumerGroupId });
        }
    }

    /// <summary>
    /// Attempts to commit the message offset, logging any failures without throwing.
    /// </summary>
    /// <param name="consumer">
    /// The Kafka consumer to use for committing.
    /// </param>
    /// <param name="consumeResult">
    /// The consume result containing the offset to commit.
    /// </param>
    /// <remarks>
    /// Commit failures are logged but do not prevent consumption of the next message.
    /// This is intentional: a commit failure may be transient (e.g., broker temporarily
    /// unavailable), and continuing allows the consumer to keep making progress.
    /// The uncommitted offset will cause redelivery on restart, which is acceptable
    /// for at-least-once semantics.
    /// </remarks>
    private void TryCommit(IConsumer<Ignore, byte[]> consumer, ConsumeResult<Ignore, byte[]> consumeResult)
    {
        try
        {
            consumer.Commit(consumeResult);

            _logger.LogDebug(
                "Committed offset for message at partition {Partition}, offset {Offset}",
                consumeResult.Partition,
                consumeResult.Offset);
        }
        catch (KafkaException ex)
        {
            _logger.LogError(
                ex,
                "Failed to commit offset for message at partition {Partition}, offset {Offset}",
                consumeResult.Partition,
                consumeResult.Offset);
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
    /// It implements exponential backoff to avoid overwhelming the broker during transient
    /// failures or restarts. After reaching maximum backoff, the service allows itself
    /// to exit so Kubernetes can restart it.
    /// </remarks>
    private async Task DelayBeforeExitAsync(CancellationToken stoppingToken)
    {
        var backoff = new ExponentialBackoff(
            initial: TimeSpan.FromMilliseconds(_options.ConsumerBackoffInitialMs),
            max: TimeSpan.FromMilliseconds(_options.ConsumerBackoffMaxMs));

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = backoff.NextDelayWithJitter();

            _logger.LogInformation("Retrying message broker consumer initialization in {DelayMs}ms...", delay.TotalMilliseconds);

            await Task.Delay(delay, stoppingToken);

            // Exit retry loop after reaching max backoff threshold
            // This allows the service to restart via Kubernetes
            if (backoff.CurrentAttempt >= 5)
            {
                _logger.LogError("Max retry attempts reached. Allowing service to restart via Kubernetes.");
                break;
            }
        }
    }
}
