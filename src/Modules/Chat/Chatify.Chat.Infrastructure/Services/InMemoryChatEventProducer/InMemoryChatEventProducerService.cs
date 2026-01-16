using Chatify.Chat.Application.Dtos;
using Chatify.Chat.Application.Ports;
using Microsoft.Extensions.Logging;

namespace Chatify.Chat.Infrastructure.Services.InMemoryChatEventProducer;

/// <summary>
/// In-memory implementation of <see cref="IChatEventProducerService"/> for testing
/// and development scenarios. This service simulates message broker behavior without
/// requiring an external Kafka broker.
/// </summary>
/// <remarks>
/// <para>
/// <b>Purpose:</b> This in-memory producer enables testing and development of Chatify
/// without the overhead of running Kafka or Redpanda. It simulates partitioning and
/// offset assignment while storing events in memory for test verification.
/// </para>
/// <para>
/// <b>Thread Safety:</b> This implementation uses thread-safe collections and locks
/// to support concurrent access from multiple test scenarios. All operations are
/// atomic and consistent across threads.
/// </para>
/// <para>
/// <b>Limitations:</b>
/// <list type="bullet">
/// <item>No persistence across application restarts</item>
/// <item>No cross-process communication</item>
/// <item>Events are only available within the same application instance</item>
/// <item>No delivery guarantees beyond in-memory storage</item>
/// <item>Should never be used in production</item>
/// </list>
/// </para>
/// <para>
/// <b>Partitioning Strategy:</b> Events are assigned to partitions using a simple
/// hash-based strategy similar to Kafka. The ScopeId is hashed to determine the
/// target partition, ensuring all events for the same scope go to the same partition.
/// </para>
/// <para>
/// <b>Offset Assignment:</b> Offsets are assigned sequentially within each partition,
/// starting from 0 and incrementing with each produced event. This mimics Kafka's
/// offset behavior for testing consumer implementations.
/// </para>
/// </remarks>
public class InMemoryChatEventProducerService : IChatEventProducerService
{
    /// <summary>
    /// Gets the default number of partitions for the in-memory broker.
    /// </summary>
    /// <remarks>
    /// This value matches the default partition count used by Kafka in production.
    /// Tests can verify partition assignment behavior using this constant.
    /// </remarks>
    private const int DefaultPartitionCount = 3;

    /// <summary>
    /// Gets the logger instance for diagnostic information.
    /// </summary>
    private readonly ILogger<InMemoryChatEventProducerService> _logger;

    /// <summary>
    /// Gets the array storing events by partition.
    /// </summary>
    /// <remarks>
    /// Each index corresponds to a partition ID. Each element is a list of events
    /// in that partition in the order they were produced. Access to each list is
    /// protected by a dedicated lock to enable concurrent writes to different partitions.
    /// </remarks>
    private readonly List<(ChatEventDto Event, long Offset)>[] _eventsByPartition;

    /// <summary>
    /// Gets the array of locks for each partition.
    /// </summary>
    /// <remarks>
    /// Each partition has its own lock to enable concurrent writes to different
    /// partitions without contention.
    /// </remarks>
    private readonly object[] _partitionLocks;

    /// <summary>
    /// Gets the array of offset counters for each partition.
    /// </summary>
    /// <remarks>
    /// Using an array allows us to use Interlocked.Increment for atomic offset
    /// assignment within each partition.
    /// </remarks>
    private readonly long[] _offsets;

    /// <summary>
    /// Gets the number of partitions configured for this in-memory broker.
    /// </summary>
    private readonly int _partitionCount;

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryChatEventProducerService"/> class.
    /// </summary>
    /// <param name="logger">
    /// The logger instance for diagnostic information. Must not be null.
    /// </param>
    /// <param name="partitionCount">
    /// The number of partitions to simulate. Defaults to <see cref="DefaultPartitionCount"/>.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="logger"/> is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="partitionCount"/> is less than or equal to zero.
    /// </exception>
    /// <remarks>
    /// <para>
    /// The constructor initializes the internal data structures for storing events
    /// and tracking offsets. All partitions are pre-allocated and ready for use.
    /// </para>
    /// </remarks>
    public InMemoryChatEventProducerService(
        ILogger<InMemoryChatEventProducerService> logger,
        int partitionCount = DefaultPartitionCount)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (partitionCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(partitionCount), "Partition count must be greater than zero.");
        }

        _partitionCount = partitionCount;
        _eventsByPartition = new List<(ChatEventDto, long)>[partitionCount];
        _partitionLocks = new object[partitionCount];
        _offsets = new long[partitionCount];

        // Initialize each partition's event list and lock
        for (int i = 0; i < partitionCount; i++)
        {
            _eventsByPartition[i] = new List<(ChatEventDto, long)>();
            _partitionLocks[i] = new object();
        }

        _logger.LogInformation(
            "InMemoryChatEventProducerService initialized with {PartitionCount} partitions",
            _partitionCount);
    }

    /// <summary>
    /// Produces a chat event to the in-memory broker asynchronously.
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
    /// <remarks>
    /// <para>
    /// <b>Partition Assignment:</b> The implementation determines which partition
    /// to write the event to by hashing the <see cref="ChatEventDto.ScopeId"/>.
    /// This ensures all events for the same scope are routed to the same partition,
    /// maintaining strict ordering guarantees similar to Kafka.
    /// </para>
    /// <para>
    /// <b>Offset Assignment:</b> Offsets are assigned sequentially within each
    /// partition. The first event in a partition receives offset 0, the second
    /// receives offset 1, and so on. Offsets are unique within a partition.
    /// </para>
    /// <para>
    /// <b>Thread Safety:</b> This method is thread-safe and can be called concurrently
    /// from multiple threads. Offsets are assigned atomically within each partition
    /// using <see cref="Interlocked.Increment(ref long)"/>.
    /// </para>
    /// <para>
    /// <b>Event Storage:</b> Events are stored in memory and can be retrieved using
    /// <see cref="GetEventsByPartition"/> for test verification. The stored events
    /// include the original <see cref="ChatEventDto"/> and their assigned offset.
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

        // Determine partition using hash of ScopeId (similar to Kafka's partitioning)
        var partition = DeterminePartition(chatEvent.ScopeId);

        // Assign offset atomically
        var offset = Interlocked.Increment(ref _offsets[partition]) - 1;

        // Store the event
        var partitionLock = _partitionLocks[partition];
        lock (partitionLock)
        {
            _eventsByPartition[partition].Add((chatEvent, offset));
        }

        _logger.LogDebug(
            "Produced chat event {MessageId} to partition {Partition}, offset {Offset}",
            chatEvent.MessageId,
            partition,
            offset);

        return Task.FromResult((partition, offset));
    }

    /// <summary>
    /// Gets all events produced to a specific partition.
    /// </summary>
    /// <param name="partition">
    /// The partition ID to query. Must be between 0 and <see cref="_partitionCount"/> - 1.
    /// </param>
    /// <returns>
    /// A read-only list of tuples containing the chat event and its assigned offset,
    /// ordered by offset in ascending order.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="partition"/> is outside the valid range.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>Purpose:</b> This method enables test code to verify that events were
    /// produced correctly and are in the expected order. It provides read-only
    /// access to the internal event storage.
    /// </para>
    /// <para>
    /// <b>Ordering Guarantee:</b> The returned list is always ordered by offset
    /// in ascending order, matching the order in which events were produced.
    /// </para>
    /// </remarks>
    public IReadOnlyList<(ChatEventDto Event, long Offset)> GetEventsByPartition(int partition)
    {
        if (partition < 0 || partition >= _partitionCount)
        {
            throw new ArgumentOutOfRangeException(nameof(partition),
                $"Partition must be between 0 and {_partitionCount - 1}.");
        }

        var partitionLock = _partitionLocks[partition];
        lock (partitionLock)
        {
            return _eventsByPartition[partition].ToList();
        }
    }

    /// <summary>
    /// Gets all events across all partitions, ordered by partition and offset.
    /// </summary>
    /// <returns>
    /// A list of tuples containing the partition ID, chat event, and its assigned offset.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>Purpose:</b> This method provides a convenient way for tests to verify
    /// all produced events without needing to query each partition individually.
    /// </para>
    /// <para>
    /// <b>Ordering:</b> Results are ordered first by partition ID, then by offset
    /// within each partition.
    /// </para>
    /// </remarks>
    public IReadOnlyList<(int Partition, ChatEventDto Event, long Offset)> GetAllEvents()
    {
        var result = new List<(int, ChatEventDto, long)>();

        for (int partition = 0; partition < _partitionCount; partition++)
        {
            var partitionLock = _partitionLocks[partition];
            lock (partitionLock)
            {
                foreach (var (evt, offset) in _eventsByPartition[partition])
                {
                    result.Add((partition, evt, offset));
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Clears all stored events and resets offsets to zero.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Purpose:</b> This method is useful for test isolation, allowing tests
    /// to start with a clean state without creating a new producer instance.
    /// </para>
    /// </remarks>
    public void Clear()
    {
        for (int partition = 0; partition < _partitionCount; partition++)
        {
            var partitionLock = _partitionLocks[partition];
            lock (partitionLock)
            {
                _eventsByPartition[partition].Clear();
            }

            Interlocked.Exchange(ref _offsets[partition], 0);
        }

        _logger.LogDebug("InMemoryChatEventProducerService cleared all events and reset offsets");
    }

    /// <summary>
    /// Gets the current event count for a specific partition.
    /// </summary>
    /// <param name="partition">
    /// The partition ID to query.
    /// </param>
    /// <returns>
    /// The number of events produced to the specified partition.
    /// </returns>
    /// <remarks>
    /// This is useful for test assertions that verify the expected number of
    /// events in a partition.
    /// </remarks>
    public int GetEventCount(int partition)
    {
        if (partition < 0 || partition >= _partitionCount)
        {
            throw new ArgumentOutOfRangeException(nameof(partition),
                $"Partition must be between 0 and {_partitionCount - 1}.");
        }

        var partitionLock = _partitionLocks[partition];
        lock (partitionLock)
        {
            return _eventsByPartition[partition].Count;
        }
    }

    /// <summary>
    /// Determines the target partition for an event based on its ScopeId.
    /// </summary>
    /// <param name="scopeId">
    /// The scope identifier to hash for partition assignment.
    /// </param>
    /// <returns>
    /// The partition ID between 0 and <see cref="_partitionCount"/> - 1.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This method uses a simple hash-based partitioning strategy similar to
    /// Kafka's default partitioner. The same ScopeId always maps to the same
    /// partition, ensuring ordering guarantees.
    /// </para>
    /// <para>
    /// The implementation uses GetHashCode() with modulo operation to determine
    /// the partition. In production Kafka, a more sophisticated Murmur2 hash
    /// is used, but this is sufficient for testing.
    /// </para>
    /// </remarks>
    private int DeterminePartition(string scopeId)
    {
        // Use a simple hash similar to Kafka's partitioning strategy
        // This ensures the same scope ID always maps to the same partition
        var hash = scopeId.GetHashCode();
        var partition = Math.Abs(hash) % _partitionCount;

        return partition;
    }
}
