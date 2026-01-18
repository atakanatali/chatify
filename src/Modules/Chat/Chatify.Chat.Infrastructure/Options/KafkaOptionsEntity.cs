namespace Chatify.Chat.Infrastructure.Options;

/// <summary>
/// Configuration options for message broker integration in Chatify.
/// </summary>
/// <remarks>
/// <para>
/// <b>Purpose:</b> This options class encapsulates all configuration required to connect
/// to and interact with a message broker for producing and consuming
/// chat events in the Chatify system.
/// </para>
/// <para>
/// <b>Configuration Binding:</b> These options are bound from the IConfiguration
/// instance provided to the DI container. The typical configuration section is
/// "Chatify:MessageBroker". Example appsettings.json:
/// <code><![CDATA[
/// {
///   "Chatify": {
///     "MessageBroker": {
///       "BootstrapServers": "localhost:9092",
///       "TopicName": "chat-events",
///       "Partitions": 3,
///       "BroadcastConsumerGroupPrefix": "chatify-broadcast"
///     }
///   }
/// }
/// ]]></code>
/// </para>
/// <para>
/// <b>Partition Strategy:</b> Chatify uses a partitioning strategy based on
/// <c>(ScopeType, ScopeId)</c> to ensure message ordering guarantees within each
/// chat scope. All messages for the same scope are routed to the same partition,
/// maintaining strict ordering while allowing parallel processing across different scopes.
/// </para>
/// <para>
/// <b>Consumer Groups:</b> The BroadcastConsumerGroupPrefix is used to construct
/// consumer group names for broadcast consumers that deliver chat events to all
/// subscribers (e.g., WebSocket connections). Each unique consumer group receives
/// a copy of all messages.
/// </para>
/// <para>
/// <b>Validation:</b> These options are validated when registered via DI.
/// Required fields include BootstrapServers and TopicName. Partitions must be
/// a positive integer.
/// </para>
/// </remarks>
public record KafkaOptionsEntity
{
    /// <summary>
    /// Gets the comma-separated list of Kafka broker addresses in the format
    /// <c>host1:port1,host2:port2,...</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Required:</b> This field must not be null or whitespace.
    /// </para>
    /// <para>
    /// <b>Format:</b> Each broker address consists of a hostname or IP address
    /// followed by a port number. Multiple brokers are separated by commas.
    /// </para>
    /// <para>
    /// <b>Examples:</b>
    /// <list type="bullet">
    /// <item><c>localhost:9092</c> - Single local broker</item>
    /// <item><c>kafka1.example.com:9092,kafka2.example.com:9092</c> - Multiple brokers</item>
    /// <item><c>192.168.1.10:9092,192.168.1.11:9092,192.168.1.12:9092</c> - IP addresses</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Redpanda:</b> Redpanda is Kafka-compatible and uses the same connection format.
    /// </para>
    /// </remarks>
    public string BootstrapServers { get; init; } = string.Empty;

    /// <summary>
    /// Gets the name of the Kafka topic used for chat event streaming.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Required:</b> This field must not be null or whitespace.
    /// </para>
    /// <para>
    /// <b>Naming Conventions:</b> Kafka topic names should be lowercase and use
    /// hyphens or underscores. Avoid special characters and spaces.
    /// </para>
    /// <para>
    /// <b>Examples:</b> <c>chat-events</c>, <c>chatify.events</c>, <c>chat_messages</c>
    /// </para>
    /// <para>
    /// <b>Topic Creation:</b> The topic should be created with the number of partitions
    /// specified in <see cref="Partitions"/> before Chatify starts. If using
    /// auto-creation, ensure the broker is configured to allow it.
    /// </para>
    /// </remarks>
    public string TopicName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the number of partitions for the chat events topic.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Default:</b> 3 partitions if not specified.
    /// </para>
    /// <para>
    /// <b>Partition Count Considerations:</b>
    /// <list type="bullet">
    /// <item>More partitions = higher parallelism for different scopes</item>
    /// <item>More partitions = more resources required on the broker</item>
    /// <item>Start with 3-6 partitions and scale based on load</item>
    /// <item>Cannot decrease partitions after topic creation</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Ordering Implications:</b> Chatify ensures ordering within a scope by
    /// using <c>(ScopeType, ScopeId)</c> as the partition key. Different scopes
    /// may be distributed across partitions, but all messages for a single scope
    /// go to the same partition, maintaining ordering guarantees.
    /// </para>
    /// <para>
    /// <b>Validation:</b> Must be a positive integer greater than zero.
    /// </para>
    /// </remarks>
    public int Partitions { get; init; } = 3;

    /// <summary>
    /// Gets the prefix used to construct consumer group names for broadcast consumers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Purpose:</b> Broadcast consumers deliver chat events to all subscribers,
    /// such as WebSocket connections or SignalR hubs. Each consumer group receives
    /// a copy of all messages in the topic, enabling fan-out delivery patterns.
    /// </para>
    /// <para>
    /// <b>Default:</b> <c>"chatify-broadcast"</c> if not specified.
    /// </para>
    /// <para>
    /// <b>Usage Pattern:</b> The full consumer group name is constructed by appending
    /// a unique identifier to this prefix. For example: <c>chatify-broadcast-pod-1</c>.
    /// </para>
    /// <para>
    /// <b>Kubernetes:</b> In a Kubernetes deployment, each pod may have its own
    /// consumer group using the pod name or hostname as the unique identifier.
    /// This ensures each pod receives all broadcast messages for local delivery
    /// to connected clients.
    /// </para>
    /// <para>
    /// <b>Examples:</b>
    /// <list type="bullet">
    /// <item><c>chatify-broadcast</c> - Default prefix</item>
    /// <item><c>chatify-ws</c> - WebSocket-specific prefix</item>
    /// <item><c>chatify-signalr</c> - SignalR-specific prefix</item>
    /// </list>
    /// </para>
    /// </remarks>
    public string BroadcastConsumerGroupPrefix { get; init; } = "chatify-broadcast";

    /// <summary>
    /// Gets a value indicating whether to use an in-memory message broker instead of Kafka.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Purpose:</b> When set to <c>true</c>, Chatify uses an in-memory implementation
    /// of <see cref="Application.Ports.IChatEventProducerService"/> for testing and
    /// development scenarios. This bypasses the need for an external Kafka broker.
    /// </para>
    /// <para>
    /// <b>Default:</b> <c>false</c> if not specified.
    /// </para>
    /// <para>
    /// <b>Usage Scenarios:</b>
    /// <list type="bullet">
    /// <item>Unit testing and integration testing without external dependencies</item>
    /// <item>Local development when Kafka is not available</item>
    /// <item>CI/CD pipelines to reduce infrastructure requirements</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Configuration Example:</b>
    /// <code><![CDATA[
    /// {
    ///   "Chatify": {
    ///     "MessageBroker": {
    ///       "UseInMemoryBroker": true,
    ///       "BootstrapServers": "localhost:9092",
    ///       "TopicName": "chat-events",
    ///       "Partitions": 3,
    ///       "BroadcastConsumerGroupPrefix": "chatify-broadcast"
    ///     }
    ///   }
    /// }
    /// ]]></code>
    /// </para>
    /// <para>
    /// <b>Important:</b> The in-memory broker does not provide persistence or
    /// cross-process communication. Events are only available within the same
    /// application instance. This setting should never be used in production.
    /// </para>
    /// </remarks>
    public bool UseInMemoryBroker { get; init; }

    /// <summary>
    /// Validates the Kafka options configuration.
    /// </summary>
    /// <returns>
    /// <c>true</c> if all required fields are present and valid; otherwise, <c>false</c>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This method performs the following validations:
    /// <list type="bullet">
    /// <item><see cref="BootstrapServers"/> is not null or whitespace (unless <see cref="UseInMemoryBroker"/> is true)</item>
    /// <item><see cref="TopicName"/> is not null or whitespace (unless <see cref="UseInMemoryBroker"/> is true)</item>
    /// <item><see cref="Partitions"/> is greater than zero (unless <see cref="UseInMemoryBroker"/> is true)</item>
    /// <item><see cref="BroadcastConsumerGroupPrefix"/> is not null or whitespace (unless <see cref="UseInMemoryBroker"/> is true)</item>
    /// </list>
    /// </para>
    /// <para>
    /// This method is called by the DI extension when registering Kafka services.
    /// If validation fails, an <see cref="ArgumentException"/> is thrown during
    /// service registration to fail fast before the application starts.
    /// </para>
    /// <para>
    /// <b>In-Memory Mode:</b> When <see cref="UseInMemoryBroker"/> is <c>true</c>,
    /// validation of <see cref="BootstrapServers"/>, <see cref="TopicName"/>,
    /// <see cref="Partitions"/>, and <see cref="BroadcastConsumerGroupPrefix"/> is
    /// skipped since these fields are not used by the in-memory implementation.
    /// </para>
    /// </remarks>
    public bool IsValid()
    {
        // When using in-memory broker, skip validation of broker-specific fields
        if (UseInMemoryBroker)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(BootstrapServers))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(TopicName))
        {
            return false;
        }

        if (Partitions <= 0)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(BroadcastConsumerGroupPrefix))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Returns a string representation of the Kafka options for logging purposes.
    /// </summary>
    /// <returns>
    /// A string containing the key configuration properties, excluding sensitive data.
    /// </returns>
    /// <remarks>
    /// This method is useful for logging the Kafka configuration on startup without
    /// exposing sensitive connection details. It includes the topic name, partition
    /// count, and consumer group prefix.
    /// </remarks>
    public override string ToString()
    {
        return $"KafkaOptionsEntity {{ TopicName = {TopicName}, Partitions = {Partitions}, BroadcastConsumerGroupPrefix = {BroadcastConsumerGroupPrefix} }}";
    }
}
