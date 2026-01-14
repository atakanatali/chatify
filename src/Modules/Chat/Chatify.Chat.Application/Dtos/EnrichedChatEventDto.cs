namespace Chatify.Chat.Application.Dtos;

/// <summary>
/// Enriched data transfer object that extends <see cref="ChatEventDto"/> with
/// messaging system metadata such as broker partition and offset information.
/// This DTO is used when consuming chat events from a streaming platform
/// where delivery metadata is required for tracking and exactly-once processing.
/// </summary>
/// <remarks>
/// <para>
/// <b>Purpose:</b> The EnrichedChatEventDto wraps a base <see cref="ChatEventDto"/>
/// with additional metadata from the messaging infrastructure. This metadata
/// is essential for implementing consumer offset management, exactly-once
/// processing guarantees, and message delivery tracking.
/// </para>
/// <para>
/// <b>Broker Metadata:</b> When consuming from a message broker (e.g., Kafka,
/// Redpanda), the partition and offset uniquely identify a message's position
/// in the topic. Consumers store this information to track their progress and
/// resume from the correct position after restarts or failovers.
/// </para>
/// <para>
/// <b>Usage Context:</b> This DTO is typically used when:
/// <list type="bullet">
/// <item>Consuming events from a message broker for persistence to ScyllaDB</item>
/// <item>Implementing consumer offset tracking for fault tolerance</item>
/// <item>Debugging message delivery issues</item>
/// <item>Implementing exactly-once processing semantics</item>
/// </list>
/// </para>
/// <para>
/// <b>Conversion:</b> Use the implicit conversion operator to create an
/// enriched event from a base event plus metadata, or access the base
/// <see cref="ChatEvent"/> property directly when only the chat data
/// is needed.
/// </para>
/// </remarks>
public record EnrichedChatEventDto
{
    /// <summary>
    /// Gets the base chat event data containing the message information.
    /// </summary>
    /// <value>
    /// A <see cref="ChatEventDto"/> containing all the core chat event data
    /// including message ID, scope information, sender, text, and timestamps.
    /// </value>
    /// <remarks>
    /// This property provides access to the underlying chat event data without
    /// the messaging infrastructure metadata. Use this when you only need
    /// the chat message information and don't require partition/offset details.
    /// </remarks>
    public required ChatEventDto ChatEvent { get; init; }

    /// <summary>
    /// Gets the message broker partition to which this chat event was written.
    /// </summary>
    /// <value>
    /// A non-negative integer representing the partition ID within the broker topic.
    /// Partition IDs are zero-indexed and range from 0 to (number of partitions - 1).
    /// </value>
    /// <remarks>
    /// <para>
    /// <b>Partitioning Strategy:</b> In Chatify, partition assignment is determined
    /// by a hash of (ScopeType, ScopeId) to ensure all messages for the same scope
    /// are delivered to the same partition. This guarantees ordering within a scope.
    /// </para>
    /// <para>
    /// <b>Consumer Implications:</b> Consumers track the partition and offset
    /// to manage their consumption progress. Each partition can be consumed by
    /// a single consumer within a consumer group, enabling parallel processing
    /// across partitions while maintaining ordering per partition.
    /// </para>
    /// <para>
    /// <b>Scaling:</b> The partition count determines the maximum parallelism
    /// for consuming chat events. More partitions allow more consumers to
    /// process events simultaneously, at the cost of increased resource usage.
    /// </para>
    /// </remarks>
    public required int Partition { get; init; }

    /// <summary>
    /// Gets the message broker offset for this message within its partition.
    /// </summary>
    /// <value>
    /// A non-negative integer representing the sequential offset of this message
    /// within the partition. Offsets are monotonically increasing and unique
    /// within each partition.
    /// </value>
    /// <remarks>
    /// <para>
    /// <b>Offset Semantics:</b> Message broker offsets represent the position of a
    /// message within a partition. Each new message receives an offset one greater
    /// than the previous message. Offsets are immutable and assigned by the broker.
    /// </para>
    /// <para>
    /// <b>Consumer Progress Tracking:</b> Consumers periodically commit their
    /// current offset to the broker to track their progress. After a restart or
    /// rebalance, consumers resume from the last committed offset. The offset
    /// stored here represents the position of this specific message.
    /// </para>
    /// <para>
    /// <b>Exactly-Once Processing:</b> For implementations requiring exactly-once
    /// semantics, the (partition, offset) tuple serves as a unique identifier
    /// that can be used for idempotency checks when persisting events to
    /// downstream systems like ScyllaDB.
    /// </para>
    /// </remarks>
    public required long Offset { get; init; }

    /// <summary>
    /// Initializes a new instance of the <see cref="EnrichedChatEventDto"/> record
    /// with the specified chat event and broker metadata.
    /// </summary>
    /// <param name="chatEvent">
    /// The base chat event containing message information.
    /// Must not be null.
    /// </param>
    /// <param name="partition">
    /// The broker partition to which the event was written.
    /// Must be non-negative.
    /// </param>
    /// <param name="offset">
    /// The broker offset for the message within its partition.
    /// Must be non-negative.
    /// </param>
    /// <remarks>
    /// This constructor creates a complete enriched event with both the chat
    /// message data and the messaging infrastructure metadata. Use this when
    /// consuming events from a message broker or similar streaming platforms.
    /// </remarks>
    public EnrichedChatEventDto(ChatEventDto chatEvent, int partition, long offset)
    {
        ChatEvent = chatEvent;
        Partition = partition;
        Offset = offset;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="EnrichedChatEventDto"/> record
    /// using the default parameterless constructor for record serialization support.
    /// </summary>
    /// <remarks>
    /// This parameterless constructor exists to support serialization frameworks
    /// that require a parameterless constructor. When using this constructor,
    /// ensure that all required properties are set before using the instance.
    /// </remarks>
    public EnrichedChatEventDto() { }
}
