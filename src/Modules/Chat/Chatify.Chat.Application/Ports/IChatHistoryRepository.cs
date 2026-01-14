using Chatify.Chat.Application.Dtos;
using Chatify.Chat.Domain;

namespace Chatify.Chat.Application.Ports;

/// <summary>
/// Defines a contract for persisting and retrieving chat message history.
/// This port represents the persistence interface of the Chatify application
/// layer, abstracting the details of storage implementations.
/// </summary>
/// <remarks>
/// <para>
/// <b>Port Role:</b> This is a secondary adapter port in Clean Architecture terms.
/// The application layer depends on this abstraction, while the infrastructure
/// layer provides concrete implementations (e.g., ScyllaDB, Cassandra, PostgreSQL).
/// This inversion keeps application logic decoupled from persistence technology.
/// </para>
/// <para>
/// <b>Storage Model:</b> The repository is designed for append-only write patterns
/// with time-series query characteristics. Messages are written once and never
/// modified, supporting high-throughput ingestion and efficient time-based queries.
/// </para>
/// <para>
/// <b>Consistency:</b> Implementations should provide strong consistency guarantees
/// for append operations. Queries should return messages in the order they were
/// written to maintain conversation coherence.
/// </para>
/// <para>
/// <b>Performance:</b> Given the chat workload pattern (many writes, reads by scope),
/// implementations should optimize for:
/// <list type="bullet">
/// <item>High append throughput (thousands of writes per second)</item>
/// <item>Efficient range queries by scope and time</item>
/// <item>Low latency reads for recent messages</item>
/// </list>
/// </para>
/// </remarks>
public interface IChatHistoryRepository
{
    /// <summary>
    /// Appends a chat message to the persistent store asynchronously.
    /// </summary>
    /// <param name="chatEvent">
    /// The chat event to persist. Contains all message data including scope,
    /// sender, text, timestamps, and origin pod information.
    /// Must not be null.
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
    /// <b>Idempotency:</b> Implementations should handle duplicate appends
    /// gracefully. Using MessageId as a primary key ensures that attempting
    /// to append the same message twice (e.g., due to retry logic) results
    /// in no duplicate data.
    /// </para>
    /// <para>
    /// <b>Write Durability:</b> This method should not return until the
    /// message has been durably written according to the configured
    /// consistency level. For distributed stores like ScyllaDB/Cassandra,
    /// this means waiting for the appropriate number of replica acknowledgments.
    /// </para>
    /// <para>
    /// <b>Storage Layout:</b> Messages should be stored in a schema optimized
    /// for time-series queries. The recommended primary key is (ScopeType, ScopeId,
    /// CreatedAtUtc, MessageId) to support efficient range queries by scope.
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
    Task AppendAsync(ChatEventDto chatEvent, CancellationToken cancellationToken);

    /// <summary>
    /// Queries chat messages for a specific scope within an optional time range.
    /// </summary>
    /// <param name="scopeType">
    /// The type of scope to query (Channel or DirectMessage). Determines which
    /// set of messages to search within.
    /// </param>
    /// <param name="scopeId">
    /// The scope identifier to query. Combined with scopeType, this uniquely
    /// identifies the conversation context.
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
    /// Maximum number of messages to return. Must be positive. Implementations
    /// may enforce a maximum limit to prevent excessive memory usage.
    /// </param>
    /// <param name="cancellationToken">
    /// A cancellation token that can be used to cancel the asynchronous operation.
    /// </param>
    /// <returns>
    /// A read-only list of <see cref="ChatEventDto"/> representing messages
    /// in the specified scope and time range, ordered by <see cref="CreatedAtUtc"/>
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
    /// <b>Pagination:</b> For large result sets, callers should use the
    /// CreatedAtUtc of the last message as the fromUtc parameter for the
    /// next query to implement pagination.
    /// </para>
    /// <para>
    /// <b>Performance Considerations:</b>
    /// <list type="bullet">
    /// <item>Queries without time bounds may scan large amounts of data</item>
    /// <item>Higher limit values increase memory and network usage</item>
    /// <item>Consider implementing server-side pagination for large histories</item>
    /// </list>
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
    Task<IReadOnlyList<ChatEventDto>> QueryByScopeAsync(
        ChatScopeTypeEnum scopeType,
        string scopeId,
        DateTime? fromUtc,
        DateTime? toUtc,
        int limit,
        CancellationToken cancellationToken);
}
