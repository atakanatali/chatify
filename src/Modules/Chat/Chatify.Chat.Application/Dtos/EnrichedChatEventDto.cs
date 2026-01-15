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
/// <param name="ChatEvent">
/// The base chat event containing message information.
/// Must not be null.
/// </param>
/// <param name="Partition">
/// The broker partition to which the event was written.
/// Must be non-negative.
/// </param>
/// <param name="Offset">
/// The broker offset for the message within its partition.
/// Must be non-negative.
/// </param>
public record EnrichedChatEventDto(
    ChatEventDto ChatEvent,
    int Partition,
    long Offset);
