using Chatify.Chat.Application.Dtos;

namespace Chatify.Chat.Application.Ports;

/// <summary>
/// Defines a contract for producing chat events to a messaging system.
/// This port represents the outbound messaging interface of the Chatify
/// application layer, abstracting the details of event streaming implementations.
/// </summary>
/// <remarks>
/// <para>
/// <b>Port Role:</b> This is a secondary adapter port in Clean Architecture terms.
/// The application layer depends on this abstraction, while the infrastructure
/// layer provides concrete implementations (e.g., Kafka producer, Redpanda producer,
/// Redis pub/sub). This inversion keeps the application logic decoupled from
/// messaging infrastructure.
/// </para>
/// <para>
/// <b>Delivery Guarantees:</b> Implementations should provide reliable delivery
/// guarantees. For message brokers like Kafka or Redpanda, this typically means
/// at-least-once semantics with idempotent writes. The application layer may
/// retry on transient failures.
/// </para>
/// <para>
/// <b>Partitioning Strategy:</b> Events should be partitioned by (ScopeType, ScopeId)
/// to maintain ordering guarantees within each scope. All messages for the same
/// scope must be delivered to the same partition in the same order they were
/// produced.
/// </para>
/// <para>
/// <b>Error Handling:</b> Implementations should handle transient failures
/// (network issues, temporary unavailability) with retries. Permanent failures
/// (invalid configuration, authentication failures) should be surfaced as
/// exceptions for the application layer to handle appropriately.
/// </para>
/// </remarks>
public interface IChatEventProducerService
{
    /// <summary>
    /// Produces a chat event to the messaging system asynchronously.
    /// </summary>
    /// <param name="chatEvent">
    /// The chat event to produce. Contains all message data including scope,
    /// sender, text, timestamps, and origin pod information.
    /// Must not be null.
    /// </param>
    /// <param name="cancellationToken">
    /// A cancellation token that can be used to cancel the asynchronous operation.
    /// </param>
    /// <returns>
    /// A <see cref="ValueTuple{T1, T2}"/> containing:
    /// <list type="bullet">
    /// <item><c>Item1 (int)</c>: The broker partition ID to which the event was written.</item>
    /// <item><c>Item2 (long)</c>: The broker offset of the message within that partition.</item>
    /// </list>
    /// These values uniquely identify the event's position in the messaging system
    /// and can be used for delivery tracking and consumer offset management.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="chatEvent"/> is null.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the producer is not configured or is in a failed state.
    /// </exception>
    /// <exception cref="TimeoutException">
    /// Thrown when the operation times out waiting for acknowledgment from
    /// the messaging system.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>Partition Assignment:</b> The implementation determines which partition
    /// to write the event to based on a partitioning strategy. For Chatify,
    /// the strategy is to hash (ScopeType, ScopeId) to ensure all messages
    /// for a scope are ordered together in the same partition.
    /// </para>
    /// <para>
    /// <b>Offset Assignment:</b> The offset is assigned by the messaging system
    /// (e.g., Kafka, Redpanda) and represents the sequential position of this
    /// message within the partition. Offsets are monotonically increasing within
    /// a partition and are guaranteed to be unique.
    /// </para>
    /// <para>
    /// <b>Acknowledgment:</b> This method should not return until the event has
    /// been durably written and acknowledged by the messaging system according
    /// to the configured acknowledgment level (e.g., "all" replicas for brokers
    /// like Kafka). This ensures no messages are lost on producer failure.
    /// </para>
    /// <para>
    /// <b>Usage Pattern:</b>
    /// <code><![CDATA[
    /// var chatEvent = new ChatEventDto { ... };
    /// var (partition, offset) = await _producer.ProduceAsync(chatEvent, ct);
    /// // Use partition/offset for tracking or logging
    /// _logger.LogInformation("Produced event at partition {Partition}, offset {Offset}",
    ///     partition, offset);
    /// ]]></code>
    /// </para>
    /// </remarks>
    Task<(int Partition, long Offset)> ProduceAsync(ChatEventDto chatEvent, CancellationToken cancellationToken);
}
