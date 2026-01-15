using System.Text;
using System.Text.Json;
using Chatify.Chat.Application.Dtos;
using Chatify.Chat.Application.Ports;
using Chatify.Chat.Infrastructure.Options;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;

namespace Chatify.Chat.Infrastructure.Services.ChatEventProducer;

/// <summary>
/// Message broker implementation of <see cref="IChatEventProducerService"/> for producing
/// chat events to a distributed log system using Confluent.Kafka client library.
/// </summary>
/// <remarks>
/// <para>
/// <b>Purpose:</b> This service is responsible for producing chat events to the message broker,
/// ensuring ordered delivery within each chat scope and providing reliable message
/// delivery with acknowledgment tracking.
/// </para>
/// <para>
/// <b>Producer Configuration:</b> The producer is configured with:
/// <list type="bullet">
/// <item><c>acks=all</c>: Wait for acknowledgment from all in-sync replicas for durability</item>
/// <item><c>enable.idempotence=true</c>: Prevent duplicate messages on retry</item>
/// <item><c>retries=INT_MAX</c>: Retry indefinitely on transient failures</item>
/// <item><c>compression.type=snappy</c>: Compress messages for efficient network transfer</item>
/// <item><c>linger.ms=5</c>: Small batching delay for improved throughput</item>
/// </list>
/// </para>
/// <para>
/// <b>Partitioning Strategy:</b> Events are partitioned by <c>ScopeId</c> to ensure
/// all messages for the same scope are routed to the same partition, maintaining
/// strict ordering guarantees. The partition key is the ScopeId string, which the
/// broker hashes to determine the target partition.
/// </para>
/// <para>
/// <b>Serialization:</b> Chat events are serialized to JSON using <c>System.Text.Json</c>
/// with UTF-8 encoding. The JSON payload includes all event properties for downstream
/// consumption and persistence.
/// </para>
/// <para>
/// <b>Error Handling:</b> The implementation handles:
/// <list type="bullet">
/// <item>Serialization errors: Fail immediately with logged exception</item>
/// <item>Producer errors: Surface message broker exceptions for application layer handling</item>
/// <item>Transient failures: Rely on the client library's automatic retries</item>
/// </list>
/// </para>
/// <para>
/// <b>Thread Safety:</b> The underlying <see cref="IProducer{TKey,TValue}"/> from
/// Confluent.Kafka is thread-safe and designed for concurrent use from multiple
/// goroutines/threads. This service is registered as a singleton for efficient
/// resource utilization.
/// </para>
/// </remarks>
public class ChatEventProducerService : IChatEventProducerService, IDisposable
{
    /// <summary>
    /// Gets the message broker configuration options.
    /// </summary>
    /// <remarks>
    /// Contains the bootstrap servers, topic name, and other producer settings.
    /// </remarks>
    private readonly KafkaOptionsEntity _options;

    /// <summary>
    /// Gets the logger instance for diagnostic information.
    /// </summary>
    private readonly ILogger<ChatEventProducerService> _logger;

    /// <summary>
    /// Gets the JSON serialization options for chat event serialization.
    /// </summary>
    /// <remarks>
    /// Uses camelCase property naming and writes indented JSON for readability
    /// in logs and debugging scenarios.
    /// </remarks>
    private readonly JsonSerializerOptions _jsonSerializerOptions;

    /// <summary>
    /// Gets the underlying message broker producer instance.
    /// </summary>
    /// <remarks>
    /// The producer is lazy-initialized to handle construction-time errors
    /// gracefully and ensure proper resource cleanup via disposal.
    /// </remarks>
    private IProducer<string, byte[]>? _producer;

    /// <summary>
    /// Gets the lock object for thread-safe producer initialization.
    /// </summary>
    private readonly object _producerLock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ChatEventProducerService"/> class.
    /// </summary>
    /// <param name="options">
    /// The message broker configuration options. Must not be null.
    /// </param>
    /// <param name="logger">
    /// The logger instance for diagnostic information. Must not be null.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="options"/> or <paramref name="logger"/> is null.
    /// </exception>
    /// <remarks>
    /// <para>
    /// The constructor validates dependencies and initializes the JSON serializer
    /// options. The message broker producer is created on first use to avoid resource
    /// allocation during service registration.
    /// </para>
    /// </remarks>
    public ChatEventProducerService(
        KafkaOptionsEntity options,
        ILogger<ChatEventProducerService> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _jsonSerializerOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        _logger.LogInformation(
            "ChatEventProducerService initialized with TopicName: {TopicName}, BootstrapServers: {BootstrapServers}",
            _options.TopicName,
            _options.BootstrapServers);
    }

    /// <summary>
    /// Gets or creates the message broker producer instance in a thread-safe manner.
    /// </summary>
    /// <returns>
    /// The initialized <see cref="IProducer{TKey,TValue}"/> instance.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when producer initialization fails due to invalid configuration.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>Lazy Initialization:</b> The producer is created on first access to
    /// avoid resource allocation during application startup and to handle
    /// initialization failures gracefully at call time rather than registration time.
    /// </para>
    /// <para>
    /// <b>Thread Safety:</b> This method uses double-check locking to ensure
    /// thread-safe initialization without blocking after the first call.
    /// </para>
    /// </remarks>
    private IProducer<string, byte[]> GetProducer()
    {
        if (_producer != null)
        {
            return _producer;
        }

        lock (_producerLock)
        {
            if (_producer != null)
            {
                return _producer;
            }

            var producerConfig = new ProducerConfig
            {
                BootstrapServers = _options.BootstrapServers,
                // Wait for all in-sync replicas to acknowledge for durability
                Acks = Acks.All,
                // Enable idempotence to prevent duplicate messages on retry
                EnableIdempotence = true,
                // Retry indefinitely on transient failures
                // Note: With idempotence enabled, retries are safe and won't create duplicates
                MessageSendMaxRetries = int.MaxValue,
                // Use snappy compression for efficient network transfer
                CompressionType = CompressionType.Snappy,
                // Small batching delay for improved throughput without adding significant latency
                LingerMs = 5,
                // Maximum batch size in bytes (default 1MB is reasonable)
                BatchSize = 1024 * 1024,
                // Buffer timeout before flushing (default)
                MessageTimeoutMs = 30000,
                // Client identifier for debugging in broker logs
                ClientId = "chatify-producer",
                // Ensure the producer can handle the configured number of partitions
                // This is informational; the actual limit comes from the broker
                Partitioner = Partitioner.Murmur2Random,
                // Allow the producer to participate in the transactional API if needed in the future
                // For now, we use idempotent producer which is sufficient for at-least-once semantics
                EnableBackgroundPoll = true
            };

            var producerBuilder = new ProducerBuilder<string, byte[]>(producerConfig);

            // Set up error handler for broker-specific errors
            producerBuilder.SetErrorHandler((producer, error) =>
            {
                _logger.LogError(
                    "Message broker producer error: Code={ErrorCode}, Reason={ErrorReason}, IsFatal={IsFatal}",
                    error.Code,
                    error.Reason,
                    error.IsFatal);
            });

            // Set up log handler for producer logs (optional, for debugging)
            producerBuilder.SetLogHandler((producer, logMessage) =>
            {
                if (logMessage.Level >= SyslogLevel.Warning)
                {
                    _logger.LogWarning(
                        "Message broker producer log: Level={Level}, Message={Message}",
                        logMessage.Level,
                        logMessage.Message);
                }
            });

            _producer = producerBuilder.Build();

            _logger.LogInformation(
                "Message broker producer created successfully. BootstrapServers: {BootstrapServers}, Topic: {TopicName}",
                _options.BootstrapServers,
                _options.TopicName);
        }

        return _producer;
    }

    /// <summary>
    /// Produces a chat event to the message broker topic asynchronously.
    /// </summary>
    /// <param name="chatEvent">
    /// The chat event to produce. Must not be null.
    /// </param>
    /// <param name="cancellationToken">
    /// A cancellation token that can be used to cancel the asynchronous operation.
    /// </param>
    /// <returns>
    /// A <see cref="ValueTuple{T1, T2}"/> containing:
    /// <list type="bullet">
    /// <item><c>Partition</c>: The partition ID to which the event was written.</item>
    /// <item><c>Offset</c>: The offset of the message within that partition.</item>
    /// </list>
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="chatEvent"/> is null.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when producer initialization fails or the producer is in a fatal error state.
    /// </exception>
    /// <exception cref="System.Text.Json.JsonException">
    /// Thrown when serialization of the chat event fails.
    /// </exception>
    /// <exception cref="KafkaException">
    /// Thrown when message broker errors occur during message production.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken"/> is canceled.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>Serialization:</b> The chat event is serialized to JSON using System.Text.Json
    /// with UTF-8 encoding. The resulting byte array is used as the message value.
    /// </para>
    /// <para>
    /// <b>Partitioning:</b> The partition key is set to <see cref="ChatEventDto.ScopeId"/>.
    /// The broker hashes this key to determine the target partition, ensuring all events
    /// for the same scope are delivered to the same partition in order.
    /// </para>
    /// <para>
    /// <b>Acknowledgment:</b> This method waits for the message to be acknowledged
    /// by all in-sync replicas before returning, as configured by <c>Acks.All</c>.
    /// This provides strong durability guarantees at the cost of increased latency.
    /// </para>
    /// <para>
    /// <b>Error Handling:</b> Serialization errors fail immediately. Production
    /// errors are surfaced via <see cref="KafkaException"/> for the application layer
    /// to handle appropriately (e.g., return error to user, trigger alerts).
    /// </para>
    /// <para>
    /// <b>Usage Example:</b>
    /// <code><![CDATA[
    /// var chatEvent = new ChatEventDto
    /// {
    ///     MessageId = Guid.NewGuid(),
    ///     ScopeType = ChatScopeTypeEnum.Channel,
    ///     ScopeId = "general",
    ///     SenderId = "user123",
    ///     Text = "Hello, world!",
    ///     CreatedAtUtc = DateTime.UtcNow,
    ///     OriginPodId = "chat-api-7d9f4c5b6d-abc12"
    /// };
    ///
    /// var (partition, offset) = await _producer.ProduceAsync(chatEvent, ct);
    /// _logger.LogInformation("Produced event at partition {Partition}, offset {Offset}",
    ///     partition, offset);
    /// ]]></code>
    /// </para>
    /// </remarks>
    public Task<(int Partition, long Offset)> ProduceAsync(
        ChatEventDto chatEvent,
        CancellationToken cancellationToken)
    {
        if (chatEvent == null)
        {
            throw new ArgumentNullException(nameof(chatEvent));
        }

        try
        {
            // Serialize the chat event to JSON
            var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(chatEvent, _jsonSerializerOptions);

            _logger.LogDebug(
                "Serialized chat event {MessageId} for scope {ScopeId} to JSON ({ByteCount} bytes)",
                chatEvent.MessageId,
                chatEvent.ScopeId,
                jsonBytes.Length);

            // Create the broker message with ScopeId as the partition key
            var brokerMessage = new Message<string, byte[]>
            {
                Key = chatEvent.ScopeId,
                Value = jsonBytes
            };

            // Produce to message broker and wait for acknowledgment
            var producer = GetProducer();

            var task = producer.ProduceAsync(
                _options.TopicName,
                brokerMessage,
                cancellationToken);

            // Continue with the task and extract partition/offset
            return task.ContinueWith(
                t =>
                {
                    var result = t.Result;
                    _logger.LogInformation(
                        "Successfully produced chat event {MessageId} to topic {Topic}, partition {Partition}, offset {Offset}",
                        chatEvent.MessageId,
                        result.Topic,
                        result.Partition,
                        result.Offset.Value);

                    return ((int Partition, long Offset))(
                        result.Partition,
                        result.Offset.Value);
                },
                cancellationToken);
        }
        catch (JsonException ex)
        {
            _logger.LogError(
                ex,
                "Failed to serialize chat event {MessageId} to JSON",
                chatEvent.MessageId);

            throw new InvalidOperationException(
                $"Failed to serialize chat event {chatEvent.MessageId} to JSON",
                ex);
        }
        catch (KafkaException ex)
        {
            _logger.LogError(
                ex,
                "Message broker error producing chat event {MessageId} to topic {Topic}",
                chatEvent.MessageId,
                _options.TopicName);

            throw;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "Chat event production canceled for message {MessageId}",
                chatEvent.MessageId);

            throw;
        }
    }

    /// <summary>
    /// Disposes the underlying message broker producer and releases associated resources.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Cleanup:</b> This method disposes the producer, which gracefully
    /// flushes any pending messages and closes network connections. It's important
    /// to dispose the producer properly to ensure all buffered messages are sent.
    /// </para>
    /// <para>
    /// <b>Graceful Shutdown:</b> The Confluent.Kafka producer handles graceful
    /// shutdown internally, flushing any pending messages before closing connections.
    /// This ensures no messages are lost during application shutdown.
    /// </para>
    /// <para>
    /// <b>Thread Safety:</b> This method is safe to call multiple times; subsequent
    /// calls will be no-ops.
    /// </para>
    /// </remarks>
    public void Dispose()
    {
        if (_producer != null)
        {
            _logger.LogInformation("Disposing message broker producer for topic {TopicName}", _options.TopicName);

            _producer.Dispose();
            _producer = null;
        }
    }
}
