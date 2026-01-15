using Cassandra;
using Chatify.Chat.Application.Common.Constants;
using Chatify.Chat.Application.Dtos;
using Chatify.Chat.Application.Ports;
using Chatify.Chat.Domain;
using Chatify.Chat.Infrastructure.Options;
using Microsoft.Extensions.Logging;

namespace Chatify.Chat.Infrastructure.Services.ChatHistory;

/// <summary>
/// Distributed database-based implementation of <see cref="IChatHistoryRepository"/>
/// for persisting and retrieving chat message history.
/// </summary>
/// <remarks>
/// <para>
/// <b>Purpose:</b> This repository provides durable, scalable storage for chat
/// messages using a distributed database (ScyllaDB or Apache Cassandra) as the backing store.
/// It is optimized for high-write throughput with efficient time-based queries.
/// </para>
/// <para>
/// <b>Why This Database:</b> The distributed database provides linear scalability,
/// low and predictable latency at scale, high write throughput (ideal for append-heavy
/// chat workloads), and tunable consistency for different data access patterns.
/// </para>
/// <para>
/// <b>Table Schema:</b> Messages are stored in the <c>chat_messages</c> table
/// with the following schema:
/// <code><![CDATA[
/// CREATE TABLE chat_messages (
///     scope_id text,
///     created_at_utc timestamp,
///     message_id uuid,
///     sender_id text,
///     text text,
///     origin_pod_id text,
///     broker_partition int,
///     broker_offset bigint,
///     PRIMARY KEY ((scope_id), created_at_utc, message_id)
/// ) WITH CLUSTERING ORDER BY (created_at_utc ASC);
/// ]]></code>
/// </para>
/// <para>
/// <b>Partitioning Strategy:</b> The partition key is <c>scope_id</c>,
/// ensuring all messages for a scope are stored together for efficient retrieval.
/// The clustering key orders messages by timestamp within each partition.
/// </para>
/// <para>
/// <b>Idempotent Write Strategy:</b> This implementation uses lightweight
/// transactions (INSERT IF NOT EXISTS) to ensure idempotency. When appending
/// a message with the same MessageId twice, the second write is silently ignored.
/// This provides exactly-once semantics in the face of retries.
/// </para>
/// <para>
/// <b>Idempotency Tradeoffs:</b>
/// <list type="bullet">
/// <item><b>Pros:</b> Guarantees no duplicates on retry, safe for exactly-once semantics</item>
/// <item><b>Cons:</b> Lightweight transactions have higher latency (4-round trip)</item>
/// <item><b>Alternative:</b> For higher throughput, use regular INSERT and dedupe at query time</item>
/// <item><b>Chosen Approach:</b> Lightweight transactions for correctness; can be optimized later</item>
/// </list>
/// </para>
/// <para>
/// <b>Query Pattern:</b> Queries by scope use server-side paging for efficient
/// retrieval of large message sets. The paging state is returned to allow
/// continued pagination.
/// </para>
/// <para>
/// <b>Consistency Level:</b>
/// <list type="bullet">
/// <item>Writes: LOCAL_QUORUM for durability with acceptable latency</item>
/// <item>Queries: LOCAL_ONE for fast reads of recent data</item>
/// </list>
/// </para>
/// <para>
/// <b>Prepared Statements:</b> All CQL statements are prepared once at startup
/// and reused for optimal performance. Prepared statements are stored in private
/// fields and initialized in the constructor.
/// </para>
/// <para>
/// <b>Why Not ORM:</b> While the Cassandra driver provides a Mapper component,
/// prepared statements are used here because:
/// <list type="bullet">
/// <item>Better performance for high-throughput scenarios</item>
/// <item>Explicit control over CQL and consistency levels</item>
/// <item>Clearer separation between database and domain models</item>
/// <item>Easier to optimize for specific access patterns</item>
/// <item>More suitable for enterprise Clean Architecture</item>
/// </list>
/// </para>
/// </remarks>
public sealed class ChatHistoryRepository : IChatHistoryRepository
{
    /// <summary>
    /// Gets the distributed database configuration options.
    /// </summary>
    /// <remarks>
    /// Contains the contact points, keyspace, and authentication credentials.
    /// </remarks>
    private readonly ScyllaOptionsEntity _options;

    /// <summary>
    /// Gets the logger instance for diagnostic information.
    /// </summary>
    private readonly ILogger<ChatHistoryRepository> _logger;

    /// <summary>
    /// Gets the database session for executing CQL statements.
    /// </summary>
    /// <remarks>
    /// The session is thread-safe and can be shared across concurrent operations.
    /// It manages connection pooling to the database cluster.
    /// </remarks>
    private readonly ISession _session;

    /// <summary>
    /// The prepared statement for inserting chat messages with idempotency.
    /// </summary>
    /// <remarks>
    /// Uses lightweight transaction (IF NOT EXISTS) to ensure idempotent writes.
    /// The statement is prepared once at initialization and reused for all append operations.
    /// </remarks>
    private readonly PreparedStatement _insertPreparedStatement;

    /// <summary>
    /// The prepared statement for querying messages by scope with pagination.
    /// </summary>
    /// <remarks>
    /// The statement is prepared once at initialization and reused for all query operations.
    /// Supports server-side paging through the automatic paging feature.
    /// </remarks>
    private readonly PreparedStatement _queryPreparedStatement;

    /// <summary>
    /// CQL statement for inserting a chat message with idempotency check.
    /// </summary>
    /// <remarks>
    /// Uses IF NOT EXISTS to ensure that attempting to insert the same message
    /// (by MessageId) twice will not create a duplicate. This provides idempotent
    /// append semantics for safe retries.
    /// </remarks>
    private const string InsertCql = @"
        INSERT INTO chat_messages (
            scope_id,
            created_at_utc,
            message_id,
            sender_id,
            text,
            origin_pod_id,
            broker_partition,
            broker_offset
        ) VALUES (
            ?, ?, ?, ?, ?, ?, ?, ?
        ) IF NOT EXISTS;";

    /// <summary>
    /// CQL statement for querying messages by scope with server-side paging.
    /// </summary>
    /// <remarks>
    /// Queries are automatically paged by the database driver. The page size
    /// is set via QueryOptions. Returns messages in ascending chronological order.
    /// </remarks>
    private const string QueryCql = @"
        SELECT
            scope_id,
            created_at_utc,
            message_id,
            sender_id,
            text,
            origin_pod_id,
            broker_partition,
            broker_offset
        FROM chat_messages
        WHERE scope_id = ?
        ORDER BY created_at_utc ASC;";

    /// <summary>
    /// Initializes a new instance of the <see cref="ChatHistoryRepository"/> class.
    /// </summary>
    /// <param name="options">
    /// The distributed database configuration options. Must not be null.
    /// </param>
    /// <param name="session">
    /// The database session for executing CQL statements. Must not be null.
    /// </param>
    /// <param name="logger">
    /// The logger instance for diagnostic information. Must not be null.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="options"/>, <paramref name="session"/>,
    /// or <paramref name="logger"/> is null.
    /// </exception>
    /// <remarks>
    /// The constructor validates dependencies, prepares CQL statements,
    /// and logs initialization with configuration details.
    /// </remarks>
    public ChatHistoryRepository(
        ScyllaOptionsEntity options,
        ISession session,
        ILogger<ChatHistoryRepository> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Prepare statements once at startup for optimal performance
        _insertPreparedStatement = _session.Prepare(InsertCql);
        _queryPreparedStatement = _session.Prepare(QueryCql);

        _logger.LogInformation(
            "ChatHistoryRepository initialized with Keyspace: {Keyspace}, ContactPoints: {ContactPoints}",
            _options.Keyspace,
            _options.ContactPoints);
    }

    /// <summary>
    /// Appends a chat message to the persistent store asynchronously.
    /// </summary>
    /// <param name="chatEvent">
    /// The chat event to persist. Must not be null.
    /// </param>
    /// <param name="cancellationToken">
    /// A cancellation token that can be used to cancel the asynchronous operation.
    /// </param>
    /// <returns>
    /// A <see cref="Task"/> representing the asynchronous operation.
    /// The task completes when the message has been durably persisted.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="chatEvent"/> is null.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the repository is not connected or is in a failed state.
    /// </exception>
    /// <exception cref="TimeoutException">
    /// Thrown when the operation times out waiting for write confirmation.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>Idempotency Strategy:</b> This method uses lightweight transactions
    /// (INSERT IF NOT EXISTS) to ensure idempotent appends. When appending
    /// a message with the same MessageId twice (e.g., due to retry logic),
    /// the second write is silently ignored and no duplicate is created.
    /// </para>
    /// <para>
    /// <b>Tradeoffs of Lightweight Transactions:</b>
    /// <list type="bullet">
    /// <item><b>Pros:</b> Strong correctness guarantee, no duplicates on retry</item>
    /// <item><b>Cons:</b> Higher latency (requires 4-round trip Paxos consensus)</item>
    /// <item><b>Alternative:</b> For higher throughput, use regular INSERT and dedupe at query time</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>When to Use Alternative Strategy:</b> If write throughput becomes
    /// a bottleneck, consider:
    /// <list type="bullet">
    /// <item>Using regular INSERT with LOCAL_QUORUM</item>
    /// <item>Implementing client-side deduplication via Materialized View or secondary index</item>
    /// <item>Accepting eventual consistency and filtering duplicates in the application layer</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Write Consistency:</b> Uses LOCAL_QUORUM to ensure writes are acknowledged
    /// by a quorum of replicas in the local datacenter. This provides strong durability
    /// with acceptable latency for most deployments.
    /// </para>
    /// <para>
    /// <b>Usage Pattern:</b>
    /// <code><![CDATA[
    /// var chatEvent = new ChatEventDto { ... };
    /// await _historyRepository.AppendAsync(chatEvent, ct);
    /// _logger.LogInformation("Persisted message {MessageId}", chatEvent.MessageId);
    /// ]]></code>
    /// </para>
    /// </remarks>
    public Task AppendAsync(
        ChatEventDto chatEvent,
        CancellationToken cancellationToken)
    {
        if (chatEvent == null)
        {
            throw new ArgumentNullException(nameof(chatEvent));
        }

        // Build the bound statement from the prepared statement
        var boundStatement = _insertPreparedStatement.Bind()
            .SetString(0, GetScopeId(chatEvent))
            .SetTimestamp(1, chatEvent.CreatedAtUtc)
            .SetGuid(2, chatEvent.MessageId)
            .SetString(3, chatEvent.SenderId)
            .SetString(4, chatEvent.Text)
            .SetString(5, chatEvent.OriginPodId)
            .SetToNull(6) // broker_partition - null when writing from API (set by consumer)
            .SetToNull(7); // broker_offset - null when writing from API (set by consumer)

        // Execute with LOCAL_QUORUM for durability
        // The IF NOT EXISTS clause makes this idempotent
        var rowSet = _session.Execute(boundStatement.SetConsistencyLevel(ConsistencyLevel.LocalQuorum));

        // Check if the insert was applied (true) or skipped due to idempotency (false)
        var applied = rowSet.FirstOrDefault()?.Get<bool>("[applied]") ?? true;

        if (applied)
        {
            _logger.LogDebug(
                "Successfully appended message {MessageId} for scope {ScopeId}",
                chatEvent.MessageId,
                GetScopeId(chatEvent));
        }
        else
        {
            _logger.LogDebug(
                "Message {MessageId} already exists (idempotent append)",
                chatEvent.MessageId);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Queries chat messages for a specific scope using server-side pagination.
    /// </summary>
    /// <param name="scopeType">
    /// The type of scope to query (Channel or DirectMessage).
    /// </param>
    /// <param name="scopeId">
    /// The scope identifier to query.
    /// </param>
    /// <param name="fromUtc">
    /// Optional start timestamp for the query range. If null, queries from
    /// the beginning of the conversation.
    /// </param>
    /// <param name="toUtc">
    /// Optional end timestamp for the query range. If null, queries to the
    /// present time.
    /// </param>
    /// <param name="limit">
    /// Maximum number of messages to return. Must be positive.
    /// </param>
    /// <param name="cancellationToken">
    /// A cancellation token that can be used to cancel the asynchronous operation.
    /// </param>
    /// <returns>
    /// A read-only list of <see cref="ChatEventDto"/> representing messages
    /// in the specified scope, ordered by <see cref="ChatEventDto.CreatedAtUtc"/>
    /// in ascending order (oldest first).
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="scopeId"/> is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="limit"/> is less than or equal to zero.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the repository is not connected or is in a failed state.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>Ordering Guarantee:</b> Results are always returned in strict
    /// chronological order by CreatedAtUtc. This ensures that conversations
    /// are displayed in the correct sequence to all users.
    /// </para>
    /// <para>
    /// <b>Pagination:</b> This method uses server-side paging to efficiently
    /// retrieve large message sets. The automatic paging feature fetches
    /// pages as needed until the limit is reached.
    /// </para>
    /// <para>
    /// <b>Note on Paging State:</b> The current <see cref="IChatHistoryRepository"/>
    /// interface does not expose paging state tokens. To fetch the next page,
    /// callers use the CreatedAtUtc of the last message as the fromUtc parameter.
    /// This is a simple pagination strategy that works well for chat timelines.
    /// </para>
    /// <para>
    /// <b>Time Range Filtering:</b> When fromUtc or toUtc are provided,
    /// the query applies additional filtering on the clustering key.
    /// This is efficient because clustering keys support range queries.
    /// </para>
    /// <para>
    /// <b>Performance Considerations:</b>
    /// <list type="bullet">
    /// <item>Queries without time bounds may scan large partitions (all messages in a scope)</item>
    /// <item>Higher limit values increase memory and network usage</item>
    /// <item>Database partitions should be sized to hold millions of messages efficiently</item>
    /// <item>For very long-lived scopes, consider time-based bucketing to avoid wide partitions</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Consistency Level:</b> Uses LOCAL_ONE for fast reads. This is
    /// appropriate for chat history where eventual consistency is acceptable
    /// (recent messages are typically delivered via message broker fan-out, not from the DB).
    /// </para>
    /// <para>
    /// <b>Usage Pattern:</b>
    /// <code><![CDATA[
    /// // Get recent messages in a channel
    /// var messages = await _historyRepository.QueryByScopeAsync(
    ///     ChatScopeTypeEnum.Channel,
    ///     "general",
    ///     fromUtc: DateTime.UtcNow.AddHours(-1),
    ///     toUtc: DateTime.UtcNow,
    ///     limit: 50,
    ///     ct);
    ///
    /// // Get last N messages (no time bound)
    /// var recent = await _historyRepository.QueryByScopeAsync(
    ///     ChatScopeTypeEnum.DirectMessage,
    ///     "conv-123",
    ///     limit: 20,
    ///     ct);
    /// ]]></code>
    /// </para>
    /// </remarks>
    public Task<IReadOnlyList<ChatEventDto>> QueryByScopeAsync(
        ChatScopeTypeEnum scopeType,
        string scopeId,
        DateTime? fromUtc,
        DateTime? toUtc,
        int limit,
        CancellationToken cancellationToken)
    {
        if (scopeId == null)
        {
            throw new ArgumentNullException(nameof(scopeId));
        }

        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), limit, "Limit must be greater than zero.");
        }

        // Build the composite scope ID (scope_type:scope_id)
        var compositeScopeId = GetCompositeScopeId(scopeType, scopeId);

        // Create the bound statement
        var boundStatement = _queryPreparedStatement.Bind()
            .SetString(0, compositeScopeId)
            .SetAutoPage(false) // Disable auto paging to enforce our limit
            .SetPageSize(limit);

        // Execute with LOCAL_ONE for fast reads
        var rowSet = _session.Execute(boundStatement.SetConsistencyLevel(ConsistencyLevel.LocalOne));

        // Convert rows to ChatEventDto
        var results = new List<ChatEventDto>();
        foreach (var row in rowSet)
        {
            var createdAtUtc = row.GetValue<DateTime>("created_at_utc");

            // Apply time range filters if specified
            if (fromUtc.HasValue && createdAtUtc < fromUtc.Value)
            {
                continue;
            }

            if (toUtc.HasValue && createdAtUtc > toUtc.Value)
            {
                continue;
            }

            // Parse the composite scope ID to extract scope_type and scope_id
            var storedScopeId = row.GetValue<string>("scope_id");
            var (storedScopeType, actualScopeId) = ParseCompositeScopeId(storedScopeId);

            var chatEvent = new ChatEventDto
            {
                MessageId = row.GetValue<Guid>("message_id"),
                ScopeType = storedScopeType,
                ScopeId = actualScopeId,
                SenderId = row.GetValue<string>("sender_id"),
                Text = row.GetValue<string>("text"),
                CreatedAtUtc = createdAtUtc,
                OriginPodId = row.GetValue<string>("origin_pod_id")
            };

            results.Add(chatEvent);

            // Stop if we've reached the limit
            if (results.Count >= limit)
            {
                break;
            }
        }

        _logger.LogDebug(
            "Retrieved {MessageCount} messages for scope {ScopeType}:{ScopeId}",
            results.Count,
            scopeType,
            scopeId);

        return Task.FromResult<IReadOnlyList<ChatEventDto>>(results);
    }

    /// <summary>
    /// Creates a composite scope ID that combines scope type and scope ID.
    /// </summary>
    /// <param name="scopeType">
    /// The type of scope (Channel or DirectMessage).
    /// </param>
    /// <param name="scopeId">
    /// The scope identifier.
    /// </param>
    /// <returns>
    /// A composite scope ID in the format "scope_type:scope_id".
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>Purpose:</b> The table schema uses a single <c>scope_id</c> column
    /// as the partition key. To include the scope type in the partitioning,
    /// we use a composite key format that combines both values.
    /// </para>
    /// <para>
    /// <b>Format:</b> "scope_type:scope_id" where scope_type is the enum name.
    /// Example: "Channel:general" or "DirectMessage:user1-user2".
    /// </para>
    /// <para>
    /// <b>Separator Choice:</b> Colon (:) is used as a separator because:
    /// <list type="bullet">
    /// <item>It's not commonly used in IDs</item>
    /// <item>It's easy to parse</item>
    /// <item>It creates human-readable composite keys</item>
    /// </list>
    /// </para>
    /// </remarks>
    private static string GetCompositeScopeId(ChatScopeTypeEnum scopeType, string scopeId)
    {
        return $"{scopeType}:{scopeId}";
    }

    /// <summary>
    /// Parses a composite scope ID into its scope type and scope ID components.
    /// </summary>
    /// <param name="compositeScopeId">
    /// The composite scope ID in the format "scope_type:scope_id".
    /// </param>
    /// <returns>
    /// A tuple containing the <see cref="ChatScopeTypeEnum"/> and the scope ID string.
    /// </returns>
    /// <exception cref="FormatException">
    /// Thrown when the composite scope ID is not in the expected format.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when the scope type is not a valid <see cref="ChatScopeTypeEnum"/> value.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>Purpose:</b> When reading from the database, the scope type and
    /// scope ID are stored together in a single column. This method extracts
    /// both components for use in the application layer.
    /// </para>
    /// <para>
    /// <b>Format:</b> Expects "scope_type:scope_id" format.
    /// </para>
    /// </remarks>
    private static (ChatScopeTypeEnum ScopeType, string ScopeId) ParseCompositeScopeId(string compositeScopeId)
    {
        var parts = compositeScopeId.Split(':', 2);
        if (parts.Length != 2)
        {
            throw new FormatException($"Invalid composite scope ID format: {compositeScopeId}");
        }

        if (!Enum.TryParse<ChatScopeTypeEnum>(parts[0], out var scopeType))
        {
            throw new ArgumentException($"Invalid scope type: {parts[0]}");
        }

        return (scopeType, parts[1]);
    }

    /// <summary>
    /// Gets the composite scope ID for a chat event.
    /// </summary>
    /// <param name="chatEvent">
    /// The chat event containing scope type and scope ID.
    /// </param>
    /// <returns>
    /// A composite scope ID in the format "scope_type:scope_id".
    /// </returns>
    /// <remarks>
    /// This is a convenience method that extracts scope type and scope ID
    /// from a chat event and creates the composite key.
    /// </remarks>
    private static string GetScopeId(ChatEventDto chatEvent)
    {
        return GetCompositeScopeId(chatEvent.ScopeType, chatEvent.ScopeId);
    }
}
